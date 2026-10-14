using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Manager;
using Monitor;
using Monitor.Game;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class FakeMarker { }
    private sealed class FakeLease
    {
        public NoteData Note;
        public float EndMsec;
        public int Monitor;
    }
    private static ConditionalWeakTable<NoteData, FakeMarker> FakeNotes = new ConditionalWeakTable<NoteData, FakeMarker>();
    private static readonly Dictionary<Component, FakeLease> FakeOwners = new();
    private static ConditionalWeakTable<Transform, Transform> FakeLaunchers = new ConditionalWeakTable<Transform, Transform>();
    private static readonly HashSet<Transform> FakeLauncherTransforms = new();
    private static bool pendingFakeMarker { get => RuntimeCharts.Current.pendingFakeMarker; set => RuntimeCharts.Current.pendingFakeMarker = value; }
    private static bool removedFakeMarker { get => RuntimeCharts.Current.removedFakeMarker; set => RuntimeCharts.Current.removedFakeMarker = value; }

    // FK precedes the optional stream id and follows all native fields/DZ.
    private static void ReadFakeMarker(MA2Record rec)
    {
        pendingFakeMarker = removedFakeMarker = false;
        if (rec?._str == null || rec._str.Count < 5 || rec._str[rec._str.Count - 1] != "FK") return;
        pendingFakeMarker = removedFakeMarker = true;
        rec._str.RemoveAt(rec._str.Count - 1);
    }
    private static void RestoreFakeMarker(MA2Record rec)
    {
        if (removedFakeMarker) rec._str.Add("FK");
        removedFakeMarker = false;
    }
    private static void ApplyFakeMarker(NoteData note)
    {
        FakeNotes.Remove(note);
        if (pendingFakeMarker)
        {
            FakeNotes.Add(note, new FakeMarker());
            note.playAnsSoundHead = note.playAnsSoundTail = true;
        }
        pendingFakeMarker = false;
    }
    public static bool IsFakeNote(NoteData note) => note != null && FakeNotes.TryGetValue(note, out _);
    public static bool IsFakeNoteOwner(Component owner) => owner != null && FakeOwners.ContainsKey(owner);

    public static NoteDataList ScoringNoteList(NoteDataList source)
    {
        if (!source.Any(IsFakeNote)) return source;
        var result = new NoteDataList();
        foreach (var note in source) if (!IsFakeNote(note)) result.Add(note);
        return result;
    }

    private static IEnumerable<MethodInfo> FakeOwnerMethods()
        => new[] { typeof(NoteBase).Assembly, typeof(CustomNoteTypes).Assembly }.Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(NoteBase).IsAssignableFrom(t) || typeof(SlideRoot).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsAbstract && m.GetMethodBody() != null);

    [HarmonyPatch]
    public static class FakeInitializePatch
    {
        public static IEnumerable<MethodBase> TargetMethods() => FakeOwnerMethods().Where(m => m.Name == "Initialize"
            && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(NoteData));




        [HarmonyPostfix]
        [HarmonyPriority(0)]
        public static void Postfix(Component __instance, NoteData __0)
        {
            if (!FeaturesEnabled(__instance))
            {
                return;
            }
            FakeOwners.Remove(__instance);
            NoteData note = __0;
            if (!IsFakeNote(note))
            {
                return;
            }
            Traverse traverse = Traverse.Create(__instance);
            int monitor = ((__instance is NoteBase noteBase) ? noteBase.MonitorId : ((SlideRoot)__instance).MonitorId);
            FakeOwners[__instance] = new FakeLease
            {
                Note = note,
                EndMsec = traverse.Field("TailMsec").GetValue<float>(),
                Monitor = monitor
            };
            if (!(__instance is NoteBase))
            {
                return;
            }
            Transform parent = __instance.transform.parent;
            if (!(parent == null) && !FakeLauncherTransforms.Contains(parent))
            {
                if (!FakeLaunchers.TryGetValue(parent, out var value) || value == null)
                {
                    FakeLaunchers.Remove(parent);
                    value = new GameObject("AquaMai Fake Visual Launcher").transform;
                    value.SetParent(parent.parent, worldPositionStays: false);
                    value.localPosition = parent.localPosition;
                    value.localRotation = parent.localRotation;
                    value.localScale = parent.localScale;
                    FakeLaunchers.Add(parent, value);
                    FakeLauncherTransforms.Add(value);
                }
                __instance.transform.SetParent(value, worldPositionStays: false);
            }
        }
    }

    [HarmonyPatch]
    public static class FakeGameplayGuardPatch
    {
        private static readonly HashSet<string> Names = new()
        {
            "NoteCheck", "NoteCheck_old", "IsJudgeNote", "Judge", "JudgeToolate", "SetAutoPlayJudge",
            "SetChainPlayResult", "SetTouchHoldChainPlayResult", "SetForcePlayResult", "SetPlayResult", "EndNote",
            "PlayJudgeSe", "PlayJudgeHeadSe", "ReserveSlideTouchSe", "ReserveSlideSe"
        };
        public static IEnumerable<MethodBase> TargetMethods() => FakeOwnerMethods().Where(m => Names.Contains(m.Name));
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static bool Prefix(Component __instance) {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return true; }
        return !IsFakeNoteOwner(__instance);
    }
    }

    [HarmonyPatch(typeof(NotesReader), "calcTotal")]
    public static class FakeTotalPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = AccessTools.Field(typeof(NotesData), "_noteData");
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, list))
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CustomNoteTypes), nameof(ScoringNoteList)));
            }
        }
    }

    [HarmonyPatch(typeof(GameScoreList), "Initialize")]
    public static class FakeScoreInitializePatch
    {
        [HarmonyPostfix]
        public static void Postfix(int monitorIndex, ref JudgeResultSt[] ____judgeResultList)
        {
        if (!(CustomNoteTypes.FeaturesForMonitor(monitorIndex))) {  return; }

            var notes = NotesManager.Instance(monitorIndex)?.getReader()?.GetNoteList();
            if (notes == null || !notes.Any(IsFakeNote)) return;
            // Native results use the full reader's indexNote. Totals exclude
            // fake carriers, so the native allocation is shorter than that
            // index space. Keep its real results and reserve neutral slots.
            Array.Resize(ref ____judgeResultList, notes.Count);
            for (var i = 0; i < notes.Count; i++)
            {
                if (!IsFakeNote(notes[i]) && !notes[i].type.isConnectSlide()) continue;
                ____judgeResultList[i] = new JudgeResultSt
                {
                    Judged = true,
                    Type = NoteScore.EScoreType.End,
                    Timing = (int)NoteJudge.ETiming.End
                };
            }
        }
    }

    [HarmonyPatch(typeof(NotesReader), "IsTouchNext")]
    public static class FakeTouchChainPatch
    {
        public static bool Prefix(NoteData temp, NoteData link, ref bool __result)
        {
        if (!(CustomNoteTypes.FeaturesEnabled())) {  return true; }

            if (!IsFakeNote(temp) && !IsFakeNote(link)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameScoreList), "SetResult")]
    public static class FakeResultPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static bool Prefix(int ____monitorIndex, int index)
        {
        if (!(CustomNoteTypes.FeaturesForMonitor(____monitorIndex))) {  return true; }

            var list = NotesManager.Instance(____monitorIndex)?.getReader()?.GetNoteList();
            return list == null || index < 0 || index >= list.Count || !IsFakeNote(list[index]);
        }
    }

    [HarmonyPatch(typeof(GameCtrl), "GetTouchReserve")]
    public static class FakeTouchReservePatch
    {
        public static void Postfix(GameCtrl __instance, List<TouchReserve.ReserveData> touchNoteIndexs)
        {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

            var notes = NotesManager.Instance(__instance.MonitorIndex).getReader().GetNoteList();
            touchNoteIndexs.RemoveAll(item => item.Index >= 0 && item.Index < notes.Count && IsFakeNote(notes[item.Index]));
        }
    }

    private static void RetireFake(Component owner, FakeLease lease)
    {
        var fields = Traverse.Create(owner);
        fields.Field("EndFlag").SetValue(true);
        if (owner is SlideRoot slide)
        {
            slide.ResetArrowObject();
        }
        else if (owner is NoteBase)
        {
            fields.Field("NoteStat").SetValue(NoteBase.NoteStatus.End);
            if (fields.Field("NeedGuide").GetValue<bool>())
                fields.Field("GuideObj").GetValue<NoteGuide>()?.ReturnToBase();
        }
        // Lifecycle completion never calls EndNote/SetResult or grade/effect/audio.
        lease.Note.isJudged = true;
        owner.gameObject.SetActive(false);
        var parent = owner is NoteBase note ? note.ParentTransform : ((SlideRoot)owner).ParentTransform;
        if (parent != null) owner.transform.SetParent(parent, false);
    }

    [HarmonyPatch(typeof(GameCtrl), "UpdateNotes")]
    public static class FakeLifetimePatch
    {
        public static void Postfix(GameCtrl __instance)
        {
            if (!FeaturesEnabled(__instance))
            {
                return;
            }
            float currentMsec = NotesManager.GetCurrentMsec();
            // Same snapshot as FakeOwners.ToArray() (Dictionary order), taken into a reused list.
            List<KeyValuePair<Component, FakeLease>> list = FakeLifetimeScratch;
            list.Clear();
            foreach (KeyValuePair<Component, FakeLease> fakeOwner in FakeOwners)
            {
                list.Add(fakeOwner);
            }
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    KeyValuePair<Component, FakeLease> keyValuePair = list[i];
                    if (keyValuePair.Key == null)
                    {
                        FakeOwners.Remove(keyValuePair.Key);
                    }
                    else if (keyValuePair.Value.Monitor == __instance.MonitorIndex)
                    {
                        if (keyValuePair.Key.gameObject.activeSelf && currentMsec > keyValuePair.Value.EndMsec + 150f)
                        {
                            RetireFake(keyValuePair.Key, keyValuePair.Value);
                        }
                        if (!keyValuePair.Key.gameObject.activeSelf)
                        {
                            FakeOwners.Remove(keyValuePair.Key);
                        }
                    }
                }
            }
            finally
            {
                list.Clear();
            }
        }
    }

    [HarmonyPatch(typeof(GameCtrl), "ForceNoteCollect")]
    public static class FakeCollectPatch
    {
        public static void Postfix(GameCtrl __instance)
        {
            foreach (var pair in FakeOwners.Where(p => p.Value.Monitor == __instance.MonitorIndex).ToArray())
                FakeOwners.Remove(pair.Key);
            // Native practice collection clears these flags for every note.
            // Fake answer sounds must remain suppressed after a rewind as well.
            foreach (var note in NotesManager.Instance(__instance.MonitorIndex).getReader().GetNoteList())
                if (IsFakeNote(note)) note.playAnsSoundHead = note.playAnsSoundTail = true;
        }
    }
}
