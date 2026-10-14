using HarmonyLib;
using Manager;
using MelonLoader;
using Monitor.Game;
using Monitor;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    [HarmonyPostfix]
    [HarmonyPriority(HarmonyLib.Priority.First)]
    [HarmonyPatch(typeof(GameCtrl), "CreateNotePool")]
    public static void CreateNotePoolPostfix(GameCtrl __instance)
    {
        if (!FeaturesEnabled(__instance) || MinePools.ContainsKey(__instance))
        {
            return;
        }
        LoadMineTextures();
        MineAudio.EnsurePlayers();
        List<NoteGuide> value = Traverse.Create(__instance).Field("_guideObjectList").GetValue<List<NoteGuide>>();
        GameObject value2 = Traverse.Create(__instance).Field("_guideListParent").GetValue<GameObject>();
        if (value != null && value2 != null)
        {
            while (value.Count < 256)
            {
                NoteGuide noteGuide = UnityEngine.Object.Instantiate(GameNotePrefabContainer.Guide, value2.transform);
                noteGuide.gameObject.SetActive(value: false);
                noteGuide.ParentTransform = value2.transform;
                value.Add(noteGuide);
            }
        }
        // Build only the mirror pools this chart's notes can request; the rest are built on first request
        // (TryBuildDeferredMirrorPool) with the same size, so every pool ever handed out is still the same pool.
        HashSet<string>[] need = ScanMirrorPoolNeeds(__instance);
        DeferredMirrorPools deferred = null;
        Dictionary<string, object> dictionary = BuildMirrorPoolSet(__instance, 0, need, ref deferred);
        MinePools[__instance] = dictionary;
        TouchBreakPools[__instance] = BuildMirrorPoolSet(__instance, 1, need, ref deferred);
        TouchStarPools[__instance] = BuildMirrorPoolSet(__instance, 2, need, ref deferred);
        MineTouchStarPools[__instance] = BuildMirrorPoolSet(__instance, 3, need, ref deferred);
        if (deferred != null)
        {
            RecordDeferredNativeCounts(__instance, deferred);
            DeferredMirrorPoolsByCtrl[__instance.GetInstanceID()] = deferred;
        }
        MelonLogger.Msg("[CustomNoteType] Mine pools ready: " + string.Join(", ", dictionary.Select((KeyValuePair<string, object> p) => $"{p.Key}={((ICollection)p.Value).Count}")));
    }

    private static readonly string[][] MirrorPoolFields = new string[4][]
        {
        new string[13] { "_tapObjectList", "_holdObjectList", "_breakHoldObjectList", "_starObjectList", "_breakStarObjectList", "_breakObjectList", "_touchBObjectList", "_touchCTapObjectList", "_touchBHoldObjectList", "_touchCHoldObjectList", "_slideObjectList", "_fanSlideObjectList", "_arrowObjectList" },
        new string[4] { "_touchBObjectList", "_touchCTapObjectList", "_touchBHoldObjectList", "_touchCHoldObjectList" },
        new string[2] { "_touchBObjectList", "_touchCTapObjectList" },
        new string[2] { "_touchBObjectList", "_touchCTapObjectList" }
        };

    private static readonly Dictionary<int, DeferredMirrorPools> DeferredMirrorPoolsByCtrl = new Dictionary<int, DeferredMirrorPools>();

    private sealed class DeferredMirrorPools
    {
        // Per set: the MirrorPoolFields entries whose pool was not built.
        public readonly HashSet<string>[] Fields = new HashSet<string>[4]
        {
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>()
        };

        // Native Count of each list a deferred pool is sized from, read in CreateNotePoolPostfix: the size the pool
        // would have copied had it been built then, before any pool extender grew the native lists. No entry: that
        // list was null then, so no pool would have been built.
        public readonly Dictionary<string, int> NativeCounts = new Dictionary<string, int>();
    }

    private static int MirrorPoolSetOf(CustomNoteKind kind)
    {
        if (kind == CustomNoteKind.Mine || kind == CustomNoteKind.MineTouchBreak)
        {
            return 0;
        }
        if ((uint)(kind - 2) <= 1u)
        {
            return 1;
        }
        if ((uint)(kind - 4) <= 1u)
        {
            return 2;
        }
        if (kind == CustomNoteKind.MineTouchStar)
        {
            return 3;
        }
        return -1;
    }

    private static HashSet<string>[] ScanMirrorPoolNeeds(GameCtrl instance)
    {
        try
        {
            ChartRuntimeState chartRuntimeState = RuntimeMonitor(instance.MonitorIndex);
            NoteDataList noteDataList = NotesManager.Instance(instance.MonitorIndex)?.getReader()?.GetNoteList();
            if (chartRuntimeState == null || noteDataList == null)
            {
                return null;
            }
            HashSet<string>[] array = new HashSet<string>[4]
            {
                new HashSet<string>(),
                new HashSet<string>(),
                new HashSet<string>(),
                new HashSet<string>()
            };
            if (chartRuntimeState.NoteKinds.Count == 0)
            {
                return array;
            }
            HashSet<long> hashSet = new HashSet<long>();
            foreach (NoteData item in noteDataList)
            {
                if (item != null && (object)item.type != null && chartRuntimeState.NoteKinds.TryGetValue(item.indexNote, out var value))
                {
                    int num = MirrorPoolSetOf(value);
                    NotesTypeID.Def def = item.type.getEnum();
                    if (num >= 0 && hashSet.Add(((long)num << 32) | (uint)def))
                    {
                        array[num].UnionWith(GetMineFieldsForNoteType(def));
                    }
                }
            }
            return array;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object> BuildMirrorPoolSet(GameCtrl instance, int set, HashSet<string>[] need, ref DeferredMirrorPools deferred)
    {
        Dictionary<string, object> dictionary = new Dictionary<string, object>();
        string[] array = MirrorPoolFields[set];
        foreach (string text in array)
        {
            if (need == null || need[set].Contains(text))
            {
                BuildMirrorPool(dictionary, instance, set, text, null);
            }
            else
            {
                (deferred ?? (deferred = new DeferredMirrorPools())).Fields[set].Add(text);
            }
        }
        return dictionary;
    }

    private static void RecordDeferredNativeCounts(GameCtrl instance, DeferredMirrorPools deferred)
    {
        for (int i = 0; i < deferred.Fields.Length; i++)
        {
            foreach (string field in deferred.Fields[i])
            {
                RecordNativeCount(instance, deferred, field);
                if (field == "_arrowObjectList")
                {
                    RecordNativeCount(instance, deferred, "_breakArrowObjectList");
                }
            }
        }
    }

    private static void RecordNativeCount(GameCtrl instance, DeferredMirrorPools deferred, string field)
    {
        if (!deferred.NativeCounts.ContainsKey(field) && Traverse.Create(instance).Field(field).GetValue() is ICollection collection)
        {
            deferred.NativeCounts[field] = collection.Count;
        }
    }

    private static bool TryBuildDeferredMirrorPool(Dictionary<string, object> pools, GameCtrl instance, int set, string fieldName, out object pool)
    {
        pool = null;
        if (DeferredMirrorPoolsByCtrl.Count == 0 || !DeferredMirrorPoolsByCtrl.TryGetValue(instance.GetInstanceID(), out var value))
        {
            return false;
        }
        string text = ((fieldName == "_breakArrowObjectList") ? "_arrowObjectList" : fieldName);
        if (!value.Fields[set].Remove(text))
        {
            return false;
        }
        BuildMirrorPool(pools, instance, set, text, value.NativeCounts);
        return pools.TryGetValue(fieldName, out pool);
    }

    private static void BuildMirrorPool(Dictionary<string, object> pools, GameCtrl instance, int set, string field, Dictionary<string, int> nativeCounts)
    {
        switch (set)
        {
            case 0:
                switch (field)
                {
                    case "_tapObjectList":
                        AddMinePoolWithFactory(pools, instance, "_tapObjectList", "_tapListParent", (Func<Transform, TapNote>)((Transform p) => MineTapNote.CreateFrom(GameNotePrefabContainer.Tap, p)), true, 192, nativeCounts);
                        break;
                    case "_holdObjectList":
                        AddMinePoolWithFactory(pools, instance, "_holdObjectList", "_holdListParent", (Func<Transform, HoldNote>)((Transform p) => MineHoldNote.CreateFrom(GameNotePrefabContainer.Hold, p)), true, 0, nativeCounts);
                        break;
                    case "_breakHoldObjectList":
                        AddMinePoolWithFactory(pools, instance, "_breakHoldObjectList", "_breakHoldListParent", (Func<Transform, BreakHoldNote>)((Transform p) => MineBreakHoldNote.CreateFrom(GameNotePrefabContainer.BreakHold, p)), true, 0, nativeCounts);
                        break;
                    case "_starObjectList":
                        AddMinePoolWithFactory(pools, instance, "_starObjectList", "_starListParent", (Func<Transform, StarNote>)((Transform p) => MineStarNote.CreateFrom(GameNotePrefabContainer.Star, p)), true, 0, nativeCounts);
                        break;
                    case "_breakStarObjectList":
                        AddMinePoolWithFactory(pools, instance, "_breakStarObjectList", "_breakStarListParent", (Func<Transform, BreakStarNote>)((Transform p) => MineBreakStarNote.CreateFrom(GameNotePrefabContainer.BreakStar, p)), true, 0, nativeCounts);
                        break;
                    case "_breakObjectList":
                        AddMinePoolWithFactory(pools, instance, "_breakObjectList", "_breakListParent", (Func<Transform, BreakNote>)((Transform p) => MineBreakNote.CreateFrom(GameNotePrefabContainer.Break, p)), true, 0, nativeCounts);
                        break;
                    case "_touchBObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchBObjectList", "_touchListParent", (Func<Transform, TouchNoteB>)((Transform p) => MineTouchNoteB.CreateFrom(GameNotePrefabContainer.TouchTapB, p)), true, 0, nativeCounts);
                        break;
                    case "_touchCTapObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchCTapObjectList", "_touchCTapListParent", (Func<Transform, TouchNoteC>)((Transform p) => MineTouchNoteC.CreateFrom(GameNotePrefabContainer.TouchTapC, p)), true, 0, nativeCounts);
                        break;
                    case "_touchBHoldObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchBHoldObjectList", "_touchHoldListParent", (Func<Transform, TouchHoldC>)((Transform p) => MineTouchHoldC.CreateFrom(GameNotePrefabContainer.TouchHoldC, p)), true, 0, nativeCounts);
                        break;
                    case "_touchCHoldObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchCHoldObjectList", "_touchCHoldListParent", (Func<Transform, TouchHoldC>)((Transform p) => MineTouchHoldC.CreateFrom(GameNotePrefabContainer.TouchHoldC, p)), true, 0, nativeCounts);
                        break;
                    case "_slideObjectList":
                        AddMinePoolWithFactory(pools, instance, "_slideObjectList", "_slideListParent", (Func<Transform, SlideRoot>)((Transform p) => MineSlideRoot.CreateFrom(GameNotePrefabContainer.Slide, p)), false, 0, nativeCounts);
                        break;
                    case "_fanSlideObjectList":
                        AddMinePoolWithFactory(pools, instance, "_fanSlideObjectList", "_fanSlideListParent", (Func<Transform, SlideFan>)((Transform p) => MineSlideFan.CreateFrom(GameNotePrefabContainer.SlideFan, p)), false, 0, nativeCounts);
                        break;
                    case "_arrowObjectList":
                        AddArrowPools(pools, instance, nativeCounts);
                        break;
                }
                break;
            case 1:
                switch (field)
                {
                    case "_touchBObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchBObjectList", "_touchListParent", (Func<Transform, TouchNoteB>)((Transform p) => TouchBreakNoteB.CreateFrom(GameNotePrefabContainer.TouchTapB, p)), true, 0, nativeCounts);
                        break;
                    case "_touchCTapObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchCTapObjectList", "_touchCTapListParent", (Func<Transform, TouchNoteC>)((Transform p) => TouchBreakNoteC.CreateFrom(GameNotePrefabContainer.TouchTapC, p)), false, 0, nativeCounts);
                        break;
                    case "_touchBHoldObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchBHoldObjectList", "_touchHoldListParent", (Func<Transform, TouchHoldC>)((Transform p) => TouchBreakHoldC.CreateFrom(GameNotePrefabContainer.TouchHoldC, p)), true, 0, nativeCounts);
                        break;
                    case "_touchCHoldObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchCHoldObjectList", "_touchCHoldListParent", (Func<Transform, TouchHoldC>)((Transform p) => TouchBreakHoldC.CreateFrom(GameNotePrefabContainer.TouchHoldC, p)), false, 0, nativeCounts);
                        break;
                }
                break;
            case 2:
                switch (field)
                {
                    case "_touchBObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchBObjectList", "_touchListParent", (Func<Transform, TouchNoteB>)((Transform p) => TouchStarNoteB.CreateFrom(GameNotePrefabContainer.TouchTapB, p)), false, 0, nativeCounts);
                        break;
                    case "_touchCTapObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchCTapObjectList", "_touchCTapListParent", (Func<Transform, TouchNoteC>)((Transform p) => TouchStarNoteC.CreateFrom(GameNotePrefabContainer.TouchTapC, p)), false, 0, nativeCounts);
                        break;
                }
                break;
            case 3:
                switch (field)
                {
                    case "_touchBObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchBObjectList", "_touchListParent", (Func<Transform, TouchNoteB>)((Transform p) => MineTouchStarNoteB.CreateFrom(GameNotePrefabContainer.TouchTapB, p)), false, 0, nativeCounts);
                        break;
                    case "_touchCTapObjectList":
                        AddMinePoolWithFactory(pools, instance, "_touchCTapObjectList", "_touchCTapListParent", (Func<Transform, TouchNoteC>)((Transform p) => MineTouchStarNoteC.CreateFrom(GameNotePrefabContainer.TouchTapC, p)), false, 0, nativeCounts);
                        break;
                }
                break;
        }
    }

    private static bool TryGetMirrorPoolSize(ICollection native, Dictionary<string, int> nativeCounts, string field, out int count)
    {
        if (native == null)
        {
            count = 0;
            return false;
        }
        if (nativeCounts == null)
        {
            count = native.Count;
            return true;
        }
        return nativeCounts.TryGetValue(field, out count);
    }

    private static void AddMinePoolWithFactory<TBase>(Dictionary<string, object> pools, GameCtrl instance, string listField, string parentField, Func<Transform, TBase> factory, bool applyTextures = true, int minimumCount = 0, Dictionary<string, int> nativeCounts = null) where TBase : Component
    {
        try
        {
            List<TBase> value = Traverse.Create(instance).Field(listField).GetValue<List<TBase>>();
            GameObject value2 = Traverse.Create(instance).Field(parentField).GetValue<GameObject>();
            if (value == null || value2 == null)
            {
                return;
            }
            // The factory never touches the native list, so its Count is constant during the loop.
            if (!TryGetMirrorPoolSize(value, nativeCounts, listField, out var value3))
            {
                return;
            }
            List<TBase> list = new List<TBase>();
            for (int i = 0; i < Math.Max(value3, minimumCount); i++)
            {
                TBase val = factory(value2.transform);
                val.gameObject.SetActive(value: false);
                TrySetParentTransform(val, value2.transform);
                if (applyTextures)
                {
                    ApplyMineTexturesToObject(val.gameObject);
                }
                list.Add(val);
            }
            pools[listField] = list;
        }
        catch (Exception arg)
        {
            MelonLogger.Error($"[CustomNoteType] Failed to create mine pool {listField}: {arg}");
        }
    }

    private static void AddArrowPools(Dictionary<string, object> pools, GameCtrl instance, Dictionary<string, int> nativeCounts = null)
    {
        try
        {
            GameObject value = Traverse.Create(instance).Field("_slideListParent").GetValue<GameObject>();
            Transform transform = ((value != null) ? value.transform : null);
            List<SpriteRenderer> value2 = Traverse.Create(instance).Field("_arrowObjectList").GetValue<List<SpriteRenderer>>();
            if (TryGetMirrorPoolSize(value2, nativeCounts, "_arrowObjectList", out var value3))
            {
                List<SpriteRenderer> list = new List<SpriteRenderer>();
                for (int i = 0; i < value3; i++)
                {
                    SpriteRenderer spriteRenderer = ((transform != null) ? UnityEngine.Object.Instantiate(GameNotePrefabContainer.Arrow, transform) : UnityEngine.Object.Instantiate(GameNotePrefabContainer.Arrow));
                    spriteRenderer.gameObject.SetActive(value: false);
                    ApplyMineArrowTexture(spriteRenderer);
                    list.Add(spriteRenderer);
                }
                pools["_arrowObjectList"] = list;
            }
            List<BreakSlide> value4 = Traverse.Create(instance).Field("_breakArrowObjectList").GetValue<List<BreakSlide>>();
            if (!TryGetMirrorPoolSize(value4, nativeCounts, "_breakArrowObjectList", out var value5))
            {
                return;
            }
            List<BreakSlide> list2 = new List<BreakSlide>();
            for (int j = 0; j < value5; j++)
            {
                BreakSlide breakSlide = ((transform != null) ? UnityEngine.Object.Instantiate(GameNotePrefabContainer.BreakArrow, transform) : UnityEngine.Object.Instantiate(GameNotePrefabContainer.BreakArrow));
                breakSlide.gameObject.SetActive(value: false);
                ApplyMineBreakArrowTexture(breakSlide);
                list2.Add(breakSlide);
            }
            pools["_breakArrowObjectList"] = list2;
        }
        catch (Exception arg)
        {
            MelonLogger.Error($"[CustomNoteType] Failed to create mine arrow pools: {arg}");
        }
    }
}
