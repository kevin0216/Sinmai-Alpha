using Manager.UserDatas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Notes.Libs;
using DB;
using HarmonyLib;
using MAI2.Util;
using Manager;
using MelonLoader;
using Monitor;
using Monitor.Game;
using Process;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class BorrowedBinding { public BorrowedTrajectory Route; public NotesReader Reader; }
    private sealed class BorrowedLease
    {
        public BorrowedBinding Binding;
        public BorrowedPath Path;
        public NoteData Note;
        public int Monitor;
        public bool Standalone;
        public SpriteRenderer Body, Guide;
        public Sprite Model, Picture, GuidePicture, TouchPetalPicture;
        public SpriteRenderer[] TouchPetals;
        public readonly Dictionary<SpriteRenderer, bool> Native = new();
        public void HideNative() { foreach (var sprite in Native.Keys) if (sprite != null) sprite.enabled = false; }
        public void Dispose()
        {
            foreach (var entry in Native) if (entry.Key != null) entry.Key.enabled = entry.Value;
            if (Body != null) { Body.gameObject.SetActive(false); Object.Destroy(Body.gameObject); }
            if (Guide != null) { Guide.gameObject.SetActive(false); Object.Destroy(Guide.gameObject); }
        }
    }
    private static ConditionalWeakTable<NoteData, BorrowedBinding> BorrowedNotes = new ConditionalWeakTable<NoteData, BorrowedBinding>();
    private static readonly Dictionary<Component, BorrowedLease> BorrowedOwners = new();
    public sealed class BorrowedCarrierOwner : MonoBehaviour { }
    private static string pendingBorrowed { get => RuntimeCharts.Current.pendingBorrowed; set => RuntimeCharts.Current.pendingBorrowed = value; }
    private static bool IsBorrowed(NoteData note) => note != null && BorrowedNotes.TryGetValue(note, out _);
    private static void ReadBorrowedMarker(MA2Record rec)
    {
        pendingBorrowed = null;
        if (rec?._str == null || rec._str.Count < 5) return;
        var tail = rec._str[rec._str.Count - 1];
        if (!tail.StartsWith("BT1|", StringComparison.Ordinal)) return;
        pendingBorrowed = tail; rec._str.RemoveAt(rec._str.Count - 1);
    }
    private static void RestoreBorrowedMarker(MA2Record rec)
    { if (pendingBorrowed != null) rec._str.Add(pendingBorrowed); }
    private static void ApplyBorrowedMarker(NoteData note, NotesReader reader, MA2Record rec)
    {
        BorrowedNotes.Remove(note);
        if (pendingBorrowed == null) return;
        BorrowedTrajectory route = null;
        if (rec._str[0] != "NMTAP" || !IsFakeNote(note) || !BorrowedTrajectory.Decode(pendingBorrowed, out route))
        { route = null; MelonLogger.Warning("[Borrowed Trajectory] Invalid carrier metadata; hiding the Fake placeholder"); }
        // Even a damaged BT/FK field must never turn a visual carrier into a
        // judged Tap. Invalid/unrenderable carriers remain invisible Fake notes.
        FakeNotes.Remove(note); FakeNotes.Add(note, new FakeMarker());
        note.playAnsSoundHead = note.playAnsSoundTail = true;
        BorrowedNotes.Add(note, new BorrowedBinding { Route = route, Reader = reader });
        pendingBorrowed = null;
    }
    [HarmonyPostfix, HarmonyPatch(typeof(NotesReader), "calcNoteTiming")]
    public static void SetBorrowedEndTimes(NotesReader __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        foreach (var note in __instance.GetNoteList())
            if (BorrowedNotes.TryGetValue(note, out var info) && info.Route != null)
            {
                note.end.setMsec(note.time.msec + info.Route.Seconds * 1000);
                note.end.calcByMsec(__instance);
            }
    }
    [HarmonyPostfix, HarmonyPatch(typeof(NotesReader), "calcEndTiming")]
    public static void ExtendBorrowedChartEnd(NotesReader __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        // Native Tap end calculation only considers its head. A visual carrier
        // can continue beyond the final judged note and must finish before results.
        var composition = __instance.GetCompositioin();
        foreach (var note in __instance.GetNoteList())
            if (IsBorrowed(note))
            {
                if (note.end.msec > composition._endGameTime.msec) composition._endGameTime = note.end;
                if (note.end.msec > composition._endNotesTime.msec) composition._endNotesTime = note.end;
            }
    }
    private static VisualNote BorrowedVisual(NoteData note, BorrowedTrajectory route)
        => VisualNotes.TryGetValue(note, out var payload) ? payload.Note : new VisualNote { Family = route.Family, Each = note.isEach };
    // Moving fake Tap/Star/Hold carriers must select exactly the same game sprites
    // as the native note SetEach / SetMulti methods. Keep
    // chart-authored custom skins above this default, and never own these assets.
    private static Sprite BorrowedNativePicture(string family, VisualNote visual, Manager.UserDatas.UserOption option)
    {
        if (family == "tap")
        {
            var index = (int)option.TapDesign;
            return visual.Break ? GameNoteImageContainer.NormalBreak[index]
                : visual.Each ? GameNoteImageContainer.EachTap[index] : GameNoteImageContainer.NormalTap[index];
        }
        if (family == "hold")
        {
            var index = (int)option.HoldDesign;
            return visual.Break ? GameNoteImageContainer.BreakHold[index]
                : visual.Each ? GameNoteImageContainer.EachHold[index] : GameNoteImageContainer.NormalHold[index];
        }
        if (family == "star")
        {
            var index = (int)option.SlideDesign;
            return visual.Break ? GameNoteImageContainer.BreakStar[index]
                : visual.Each ? GameNoteImageContainer.EachStar[index]
                : GameNoteImageContainer.NormalStar[index, (int)option.StarType];
        }
        if (family == "touch" || family == "touchhold")
            return visual.Each && !visual.Break ? GameNoteImageContainer.EachTouchPoint : GameNoteImageContainer.NormalTouchPoint;
        return null;
    }
    private static void RemoveBorrowedOwner(Component owner)
    {
        if (!BorrowedOwners.TryGetValue(owner, out var lease)) return;
        lease.Dispose(); BorrowedOwners.Remove(owner);
        if (lease.Standalone && owner != null) { owner.gameObject.SetActive(false); Object.Destroy(owner.gameObject); }
    }
    private static void ResetBorrowedNotes(NotesReader reader)
    { foreach (var pair in BorrowedOwners.Where(p => p.Value.Binding.Reader == reader).ToArray()) RemoveBorrowedOwner(pair.Key); }
    private static void ReleaseBorrowedNotes()
    {
        foreach (var pair in BorrowedOwners.ToArray()) RemoveBorrowedOwner(pair.Key);
        // Tint sprites reference carrier/skin textures. Release them first.
        ClearVisualTintCache();
    }
    [HarmonyPatch]
    public static class BorrowedInitializePatch
    {
        public static IEnumerable<MethodBase> TargetMethods() => FakeOwnerMethods().Where(m => typeof(NoteBase).IsAssignableFrom(m.DeclaringType) && m.Name == "Initialize"
            && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(NoteData));
        [HarmonyPrefix, HarmonyPriority(Priority.First + 10)]
        public static void Prefix(NoteBase __instance) {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }
        RemoveBorrowedOwner(__instance);
    }
        [HarmonyPostfix, HarmonyPriority(Priority.Last - 20)]
        public static void Postfix(NoteBase __instance, NoteData note)
        {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

            RemoveBorrowedOwner(__instance);
            if (!BorrowedNotes.TryGetValue(note, out var info)) return;
            var lease = new BorrowedLease { Binding = info, Note = note, Monitor = __instance.MonitorId };
            foreach (var sprite in __instance.GetComponentsInChildren<SpriteRenderer>(true))
                if (!VisualOverlayRenderers.Contains(sprite)) lease.Native[sprite] = sprite.enabled;
            BorrowedOwners[__instance] = lease; lease.HideNative();
            if (info.Route?.Renderable != true) return;
            var native = Traverse.Create(__instance).Field("SpriteRender").GetValue<SpriteRenderer>();
            // FakeInitialize has detached the placeholder from native input
            // siblings. Render in the common circle basis, outside its radial
            // NoteObj scale/launcher rotation; never change native queue TRS.
            var basis = __instance.transform.parent?.parent;
            BuildBorrowedDisplay(lease, native, basis);
        }
    }
    private static void BuildBorrowedDisplay(BorrowedLease lease, SpriteRenderer native, Transform basis)
    {
            var info = lease.Binding; var note = lease.Note;
            if (info.Route?.Renderable != true || native == null || basis == null) return;
            SpriteRenderer Make(string name, int order)
            {
                var obj = new GameObject(name); obj.layer = native.gameObject.layer;
                obj.transform.SetParent(basis, false);
                var render = obj.AddComponent<SpriteRenderer>(); render.sharedMaterial = native.sharedMaterial;
                render.sortingLayerID = native.sortingLayerID; render.sortingOrder = order;
                render.maskInteraction = native.maskInteraction; render.enabled = false; return render;
            }
            var visual = BorrowedVisual(note, info.Route);
            var option = Singleton<GamePlayManager>.Instance.GetGameScore(lease.Monitor).UserOption;
            lease.Path = new BorrowedPath(info.Route);
            lease.Model = BorrowedNativePicture(info.Route.Family, visual, option);
            lease.Picture = NoteSkins.TryGetValue(note, out var skin) ? skin.Assets.Create(skin.Path, lease.Model) ?? lease.Model : lease.Model;
            lease.Body = Make("Sinmai-Alpha Borrowed Carrier", 1000);
            // A native Touch is a point plus four independently rotated petals,
            // not one imported screenshot. A chart-supplied skin replaces this
            // composite as before; the native sprites remain game-owned.
            if ((info.Route.Family == "touch" || info.Route.Family == "touchhold") && lease.Picture == lease.Model)
            {
                lease.TouchPetalPicture = visual.Each && !visual.Break ? GameNoteImageContainer.EachTouch : GameNoteImageContainer.NormalTouch;
                lease.TouchPetals = new SpriteRenderer[4];
                var prefab = GameNotePrefabContainer.TouchTapB;
                var nativePetals = prefab != null ? Traverse.Create(prefab).Field("ColorsObject").GetValue<SpriteRenderer[]>() : null;
                for (var i = 0; i < 4; i++)
                {
                    var petal = Make("Native Touch petal " + i, 999);
                    petal.transform.SetParent(lease.Body.transform, false);
                    var prototype = nativePetals != null && i < nativePetals.Length ? nativePetals[i] : null;
                    petal.transform.localRotation = prototype != null ? prototype.transform.localRotation : Quaternion.Euler(0, 0, i * 90);
                    petal.transform.localScale = prototype != null ? prototype.transform.localScale : Vector3.one;
                    lease.TouchPetals[i] = petal;
                }
            }
            if (info.Route.Family != "touch" && info.Route.Family != "touchhold")
            {
                lease.GuidePicture = GameNoteImageContainer.Guide[visual.Break ? 3 : visual.Each ? 1 : info.Route.Family == "star" ? 2 : 0];
                lease.Guide = Make("AquaMai Borrowed Guide", -note.indexNote * 10 - 1);
            }
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(GameCtrl), "RegistNote")]
    public static bool RegisterBorrowedCarrier(GameCtrl __instance, NoteData note, ref bool __result)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return true; }

        if (!BorrowedNotes.TryGetValue(note, out var binding)) return true;
        note.playAnsSoundHead = note.playAnsSoundTail = true;
        // A moving Fake can outlive many normal Tap waves. Never lease the
        // native 64-Tap pool, guide pool or _activeNoteList for these carriers.
        var fields = Traverse.Create(__instance);
        var basis = fields.Field("_noteLauncherLayer").GetValue<GameObject>()?.transform;
        var model = fields.Field("_tapObjectList").GetValue<List<TapNote>>()?.FirstOrDefault();
        var native = model == null ? null : Traverse.Create(model).Field("SpriteRender").GetValue<SpriteRenderer>();
        var obj = new GameObject("AquaMai Borrowed Owner"); obj.transform.SetParent(basis, false);
        var owner = obj.AddComponent<BorrowedCarrierOwner>();
        var lease = new BorrowedLease { Binding = binding, Note = note, Monitor = __instance.MonitorIndex, Standalone = true };
        BorrowedOwners[owner] = lease; BuildBorrowedDisplay(lease, native, basis);
        __result = true; return false;
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(GameCtrl), "SkipRegistNote")]
    public static bool SkipBorrowedCarrier(NoteData note, ref bool __result)
    {
        if (!(CustomNoteTypes.FeaturesEnabled())) {  return true; }

        if (!IsBorrowed(note)) return true;
        __result = NotesManager.GetCurrentMsec() >= note.end.msec;
        if (__result) note.isJudged = true;
        return false;
    }
    private static Vector3 MirrorBorrowed(Vector3 point, OptionMirrorID mirror) => mirror switch
    {
        OptionMirrorID.LR => new Vector3(-point.x, point.y, point.z),
        OptionMirrorID.UD => new Vector3(point.x, -point.y, point.z),
        OptionMirrorID.UDLR => new Vector3(-point.x, -point.y, point.z), _ => point
    };
    private static void BorrowedLook(SpriteRenderer render, Sprite picture, VisualValue value, bool mine)
    {
        if (render == null || picture == null) return;
        if (ApplyBorrowedTintMaterial(render, picture, value, mine)) return;
        render.sprite = value.Color.HasValue ? GetVisualTintSprite(picture, value.Color.Value, Color.white)
            : mine ? GetBorrowedGraySprite(picture) : picture;
        render.color = new Color(1, 1, 1, value.Alpha ?? 1);
    }
    [HarmonyPostfix]
    [HarmonyPriority(-20)]
    [HarmonyPatch(typeof(GameCtrl), "UpdateNotes")]
    public static void UpdateBorrowedNotes(GameCtrl __instance)
    {
        if (!FeaturesEnabled(__instance))
        {
            return;
        }
        float currentMsec = NotesManager.GetCurrentMsec();
        NotesReader reader = NotesManager.Instance(__instance.MonitorIndex).getReader();
        VisualTimelines.TryGetValue(reader, out var value);
        UserOption userOption = Singleton<GamePlayManager>.Instance.GetGameScore(__instance.MonitorIndex).UserOption;
        int monitorIndex = __instance.MonitorIndex;
        List<KeyValuePair<Component, BorrowedLease>> list = BorrowedScratch;
        list.Clear();
        foreach (KeyValuePair<Component, BorrowedLease> borrowedOwner in BorrowedOwners)
        {
            if (borrowedOwner.Value.Monitor == monitorIndex)
            {
                list.Add(borrowedOwner);
            }
        }
        try
        {
            UpdateBorrowedOwners(list, currentMsec, value, userOption);
        }
        finally
        {
            list.Clear();
        }
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First + 10), HarmonyPatch(typeof(GameCtrl), "ForceNoteCollect")]
    public static void CollectBorrowedNotes(GameCtrl __instance)
    { foreach (var pair in BorrowedOwners.Where(p => p.Value.Monitor == __instance.MonitorIndex).ToArray()) RemoveBorrowedOwner(pair.Key); }

    private static readonly List<KeyValuePair<Component, FakeLease>> FakeLifetimeScratch = new List<KeyValuePair<Component, FakeLease>>();

    private static readonly List<KeyValuePair<TouchNoteB, TouchRadiusLease>> TouchRadiusScratch = new List<KeyValuePair<TouchNoteB, TouchRadiusLease>>();

    private static readonly List<KeyValuePair<Component, BorrowedLease>> BorrowedScratch = new List<KeyValuePair<Component, BorrowedLease>>();

    private static readonly List<KeyValuePair<NoteBase, SkinLease>> SkinScratch = new List<KeyValuePair<NoteBase, SkinLease>>();

    private static readonly List<KeyValuePair<NoteBase, StarHeadLease>> StarHeadScratch = new List<KeyValuePair<NoteBase, StarHeadLease>>();

    private static readonly List<KeyValuePair<Component, VisualLease>> VisualScratch = new List<KeyValuePair<Component, VisualLease>>();

    private static readonly VisualValue EmptyVisualValue = new VisualValue();

    private static void UpdateBorrowedOwners(List<KeyValuePair<Component, BorrowedLease>> array, float currentMsec, VisualTimeline value, UserOption userOption)
    {
        for (int num = 0; num < array.Count; num++)
        {
            KeyValuePair<Component, BorrowedLease> keyValuePair = array[num];
            Component key = keyValuePair.Key;
            BorrowedLease value2 = keyValuePair.Value;
            if (key == null || !key.gameObject.activeInHierarchy || (key is NoteBase noteBase && noteBase.IsEnd()))
            {
                RemoveBorrowedOwner(key);
                continue;
            }
            if (value2.Standalone && currentMsec > value2.Note.end.msec + 150f)
            {
                value2.Note.isJudged = true;
                RemoveBorrowedOwner(key);
                continue;
            }
            value2.HideNative();
            if (value2.Body == null)
            {
                continue;
            }
            BorrowedTrajectory route = value2.Binding.Route;
            NoteData note = value2.Note;
            bool flag = currentMsec >= note.time.msec && currentMsec <= note.end.msec && !GameManager.ForceHideNote(value2.Monitor);
            value2.Body.enabled = flag;
            if (value2.Guide != null)
            {
                value2.Guide.enabled = flag;
            }
            if (value2.TouchPetals != null)
            {
                SpriteRenderer[] touchPetals = value2.TouchPetals;
                for (int num2 = 0; num2 < touchPetals.Length; num2++)
                {
                    touchPetals[num2].enabled = flag;
                }
            }
            if (!flag)
            {
                continue;
            }
            float progress = MajdataSlideProgress(note, note.time.msec, route.Seconds * 1000f, currentMsec);
            UnityEngine.Vector3 vector = MirrorBorrowed(value2.Path.Evaluate(progress), userOption.MirrorMode);
            UnityEngine.Vector3 vector2 = MirrorBorrowed(value2.Path.Tangent(progress), userOption.MirrorMode);
            value2.Body.transform.localPosition = vector * 100f;
            bool flag2 = route.Family == "star" || route.Family == "hold" || route.Family == "touchhold";
            value2.Body.transform.localRotation = UnityEngine.Quaternion.Euler(0f, 0f, flag2 ? (Mathf.Atan2(vector2.y, vector2.x) * 57.29578f + 18f) : 0f);
            VisualNote visualNote = BorrowedVisual(note, route);
            VisualValue visualValue = new VisualValue();
            if (value != null && value.Streams.TryGetValue(VisualNotes.TryGetValue(note, out var value3) ? value3.Stream : "", out var value4))
            {
                visualValue = VisualState.Resolve(value4, currentMsec, visualNote);
            }
            VisualValue visualValue2 = new VisualValue
            {
                Color = (visualValue.Color ?? visualNote.Base.Color),
                Alpha = (visualValue.Alpha ?? visualNote.Base.Alpha),
                X = (visualValue.X ?? visualNote.Base.X),
                Y = (visualValue.Y ?? visualNote.Base.Y)
            };
            float num3 = ((route.Family == "star") ? 1.5f : 1f);
            value2.Body.transform.localScale = new UnityEngine.Vector3(num3 * (visualValue2.X ?? 1f), num3 * (visualValue2.Y ?? 1f), 1f);
            if ((route.Family == "touch" || route.Family == "touchhold") && visualNote.Break && !visualValue2.Color.HasValue)
            {
                visualValue2.Color = 16737592;
            }
            BorrowedLook(value2.Body, value2.Picture, visualValue2, visualNote.Mine);
            if (value2.TouchPetals != null)
            {
                SpriteRenderer[] touchPetals = value2.TouchPetals;
                for (int num2 = 0; num2 < touchPetals.Length; num2++)
                {
                    BorrowedLook(touchPetals[num2], value2.TouchPetalPicture, visualValue2, visualNote.Mine);
                }
            }
            if (!(value2.Guide == null))
            {
                float magnitude = vector.magnitude;
                value2.Guide.enabled = magnitude > 0.001f;
                value2.Guide.transform.localPosition = UnityEngine.Vector3.zero;
                value2.Guide.transform.localRotation = UnityEngine.Quaternion.Euler(0f, 0f, Mathf.Atan2(vector.y, vector.x) * 57.29578f - 90f);
                value2.Guide.transform.localScale = UnityEngine.Vector3.one * (magnitude / 4.8f);
                BorrowedLook(value2.Guide, value2.GuidePicture, visualNote.Base, visualNote.Mine);
            }
        }
    }
}
