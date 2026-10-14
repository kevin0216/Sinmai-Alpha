using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using SinmaiAlpha.ChartVisuals;
using HarmonyLib;
using Manager;
using MelonLoader;
using Monitor;
using Monitor.Game;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class StarHeadValue { public bool Rotate; public NotesReader Reader; }
    private sealed class StarHeadLease
    {
        public StarHeadValue Value;
        public Transform Picture, Basis;
        public Quaternion OriginalRotation;
        public float Angle, NativeSpeed;
        public int Monitor, LastFrame = -1;
    }
    private static ConditionalWeakTable<NoteData, StarHeadValue> StarHeads = new ConditionalWeakTable<NoteData, StarHeadValue>();
    private static readonly Dictionary<NoteBase, StarHeadLease> StarHeadOwners = new();
    private static string pendingStarHead { get => RuntimeCharts.Current.pendingStarHead; set => RuntimeCharts.Current.pendingStarHead = value; }

    private static void ReadStarHeadMarker(MA2Record rec)
    {
        pendingStarHead = null;
        if (rec?._str == null || rec._str.Count < 5) return;
        var tail = rec._str[rec._str.Count - 1];
        if (!tail.StartsWith("SH1|", StringComparison.Ordinal)) return;
        pendingStarHead = tail; rec._str.RemoveAt(rec._str.Count - 1);
    }
    private static void RestoreStarHeadMarker(MA2Record rec)
    { if (pendingStarHead != null) rec._str.Add(pendingStarHead); }
    private static void ApplyStarHeadMarker(NoteData note, NotesReader reader, MA2Record rec)
    {
        StarHeads.Remove(note);
        if (pendingStarHead == null) return;
        var tag = rec._str[0];
        if ((tag.EndsWith("STR", StringComparison.Ordinal) || tag == "BST" || tag == "XST") &&
            StarHead.Decode(pendingStarHead, out var rotate))
            StarHeads.Add(note, new StarHeadValue { Rotate = rotate, Reader = reader });
        else MelonLogger.Warning("[Star Head] Invalid standalone star metadata: " + pendingStarHead);
        pendingStarHead = null;
    }
    private static float StarHeadSpeed(NoteBase owner) => owner is StarNote star ? star.StarRotateSpeed : ((BreakStarNote)owner).StarRotateSpeed;
    private static void SetStarHeadSpeed(NoteBase owner, float speed)
    { if (owner is StarNote star) star.StarRotateSpeed = speed; else ((BreakStarNote)owner).StarRotateSpeed = speed; }
    private static void RestoreStarHead(NoteBase owner)
    {
        if (!StarHeadOwners.TryGetValue(owner, out var lease)) return;
        RestoreVisualLease(owner);
        if (lease.Picture != null) lease.Picture.localRotation = lease.OriginalRotation;
        if (owner != null) SetStarHeadSpeed(owner, lease.NativeSpeed);
        StarHeadOwners.Remove(owner);
    }
    private static void ResetStarHeads(NotesReader reader = null)
    {
        foreach (var pair in StarHeadOwners.Where(p => reader == null || p.Value.Value.Reader == reader).ToArray()) RestoreStarHead(pair.Key);
    }

    [HarmonyPatch(typeof(NoteBase), "Initialize")]
    public static class StarHeadRestorePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First + 20)]
        public static void Prefix(NoteBase __instance) {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }
        RestoreStarHead(__instance);
    }
    }
    [HarmonyPatch]
    public static class StarHeadInitializePatch
    {
        public static IEnumerable<MethodBase> TargetMethods() => new[] {
            AccessTools.DeclaredMethod(typeof(StarNote), "Initialize"), AccessTools.DeclaredMethod(typeof(BreakStarNote), "Initialize") };
        [HarmonyPostfix, HarmonyPriority(Priority.Last + 30)]
        public static void Postfix(NoteBase __instance, NoteData note)
        {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

            // Majdata only sets FakeRotate for a standalone ForceStar. A slide
            // head ($$ included) or borrowed trajectory keeps its own facing.
            if (IsBorrowed(note) || note.child != null && note.child.Count != 0 || !StarHeads.TryGetValue(note, out var value)) return;
            var picture = Traverse.Create(__instance).Field("NoteObj").GetValue<GameObject>()?.transform;
            var basis = __instance.transform.parent?.parent;
            if (picture == null || basis == null) return;
            StarHeadOwners[__instance] = new StarHeadLease { Value = value, Picture = picture, Basis = basis,
                OriginalRotation = picture.localRotation, NativeSpeed = StarHeadSpeed(__instance), Monitor = __instance.MonitorId };
            SetStarHeadSpeed(__instance, 0);
            picture.rotation = basis.rotation;
        }
    }
    [HarmonyPostfix]
    [HarmonyPriority(30)]
    [HarmonyPatch(typeof(GameCtrl), "UpdateNotes")]
    public static void UpdateStarHeads(GameCtrl __instance)
    {
        if (!FeaturesEnabled(__instance))
        {
            return;
        }
        int monitorIndex = __instance.MonitorIndex;
        List<KeyValuePair<NoteBase, StarHeadLease>> list = StarHeadScratch;
        list.Clear();
        foreach (KeyValuePair<NoteBase, StarHeadLease> starHeadOwner in StarHeadOwners)
        {
            if (starHeadOwner.Value.Monitor == monitorIndex)
            {
                list.Add(starHeadOwner);
            }
        }
        try
        {
            UpdateStarHeadOwners(list);
        }
        finally
        {
            list.Clear();
        }
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First + 20), HarmonyPatch(typeof(GameCtrl), "ForceNoteCollect")]
    public static void CollectStarHeads(GameCtrl __instance)
    { foreach (var pair in StarHeadOwners.Where(p => p.Value.Monitor == __instance.MonitorIndex).ToArray()) RestoreStarHead(pair.Key); }


    private static void UpdateStarHeadOwners(List<KeyValuePair<NoteBase, StarHeadLease>> array)
    {
        for (int num = 0; num < array.Count; num++)
        {
            KeyValuePair<NoteBase, StarHeadLease> keyValuePair = array[num];
            NoteBase key = keyValuePair.Key;
            StarHeadLease value = keyValuePair.Value;
            if (key == null || !key.gameObject.activeInHierarchy || key.IsEnd() || value.Picture == null || value.Basis == null)
            {
                RestoreStarHead(key);
                continue;
            }
            UnityEngine.Vector3 localScale = value.Picture.localScale;
            if (value.LastFrame != Time.frameCount && Mathf.Abs(localScale.x) > 1E-06f && Mathf.Abs(localScale.y) > 1E-06f && !GameManager.ForceHideNote(value.Monitor))
            {
                if (value.Value.Rotate)
                {
                    value.Angle = Mathf.Repeat(value.Angle + 400f * Time.deltaTime, 360f);
                }
                value.LastFrame = Time.frameCount;
            }
            value.Picture.rotation = value.Basis.rotation * UnityEngine.Quaternion.Euler(0f, 0f, value.Angle);
        }
    }
}
