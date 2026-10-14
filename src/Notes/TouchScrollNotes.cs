using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Manager;
using Monitor;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class TouchMotionLease
    {
        internal float Alpha, LastMsec = float.NaN;
        internal bool GaugeVisible, NoticeVisible, Applied;
        internal SpriteRenderer Gauge;
        internal bool OriginalGaugeEnabled;
        internal float OriginalGaugeAlpha;
    }
    private static ConditionalWeakTable<TouchNoteB, TouchMotionLease> TouchMotionOwners = new ConditionalWeakTable<TouchNoteB, TouchMotionLease>();
    private static readonly FieldInfo TouchPetals = AccessTools.Field(typeof(TouchNoteB), "ColorsObject");
    private static readonly FieldInfo TouchPositions = AccessTools.Field(typeof(TouchNoteB), "DefaultCorlsPos");
    private static readonly FieldInfo TouchNotice = AccessTools.Field(typeof(TouchNoteB), "NoticeObject");
    private static readonly FieldInfo TouchGauge = AccessTools.Field(typeof(TouchHoldC), "HoldGaugeObject");
    private static float TouchViewSpeed(float nativeMsec, float multiplier)
    {
        if (multiplier == 0) return 0;
        // Sinmai Touch uses its own speed setting: D to close, plus D/4 fade.
        // Preserve the reference petal curve, but fit its total duration to the
        // native 1.25D/abs(HS) window. Multiplying a preview speed label by HS
        // would instead scale time by HS^0.954962 and miss the native baseline.
        var window = Math.Max(nativeMsec * .00125f, .000001f);
        return Math.Sign(multiplier) * (float)Math.Pow(3.209385682f * Math.Abs(multiplier) / window, 1 / .9549621752f);
    }
    private static float TouchVisualActivationLead(int index, float appear, float nativeMsec, float fallback)
    {
        if (!NoteScrollTableByNoteIndex.TryGetValue(index, out var table) || table.Count == 0 ||
            !HsMultByNoteIndex.TryGetValue(index, out var hs) || !SvScrollPosByNoteIndex.TryGetValue(index, out var target)) return fallback;
        var speed = TouchViewSpeed(nativeMsec, hs);
        if (Math.Abs(speed) <= ScrollVisualTiming.Epsilon) return Math.Min(10000f, Math.Max(fallback, appear - table[0].Msec));
        var threshold = target - Math.Sign(speed) * ScrollVisualTiming.TouchDuration(speed) * 1000f;
        bool Crossed(float scroll) => speed > 0 ? scroll >= threshold : scroll <= threshold;
        for (var i = 0; i < table.Count && table[i].Msec <= appear; i++)
        {
            var point = table[i];
            if (Crossed(point.Scroll)) return Math.Min(10000f, Math.Max(fallback, appear - point.Msec));
            if (i + 1 == table.Count || !Crossed(table[i + 1].Scroll)) continue;
            var next = table[i + 1];
            var delta = next.Scroll - point.Scroll;
            if (Math.Abs(delta) < .00001f) continue;
            var crossing = point.Msec + (next.Msec - point.Msec) * (threshold - point.Scroll) / delta;
            return Math.Min(10000f, Math.Max(fallback, appear - crossing));
        }
        return fallback;
    }
    private static void RestoreTouchMotion(TouchNoteB owner, bool remove)
    {
        if (!TouchMotionOwners.TryGetValue(owner, out var value))
        {
            return;
        }
        if (value.Applied)
        {
            SpriteRenderer[] array = TouchPetalsOf(owner);
            UnityEngine.Vector3[] array2 = TouchPositionsOf(owner);
            for (int i = 0; i < array.Length && i < array2.Length; i++)
            {
                if (array[i] != null)
                {
                    array[i].transform.localPosition = array2[i];
                }
            }
            if (value.Gauge != null)
            {
                value.Gauge.enabled = value.OriginalGaugeEnabled;
                Color color = value.Gauge.color;
                color.a = value.OriginalGaugeAlpha;
                value.Gauge.color = color;
            }
            GameObject gameObject = TouchNoticeOf(owner);
            if (gameObject != null)
            {
                gameObject.SetActive(value: false);
            }
            value.Applied = false;
        }
        if (remove)
        {
            TouchMotionOwners.Remove(owner);
        }
    }
    private static void ApplyTouchMotion(TouchNoteB owner)
    {
        if (owner == null || owner.IsEnd())
        {
            return;
        }
        int noteIndex = owner.GetNoteIndex();
        if (!SvScrollPosByNoteIndex.TryGetValue(noteIndex, out var value) || !HsMultByNoteIndex.TryGetValue(noteIndex, out var value2))
        {
            return;
        }
        float num = AppearMsecOf(owner);
        float num2 = DefaultMsecOf(owner);
        float currentMsec = NotesManager.GetCurrentMsec();
        TouchMotionOwners.TryGetValue(owner, out var value3);
        if (value3 == null && !RingVisualActive(noteIndex, num, num2, 1.225f, currentMsec))
        {
            return;
        }
        if (value3 == null)
        {
            value3 = new TouchMotionLease();
            if (owner is TouchHoldC touchHoldC)
            {
                value3.Gauge = TouchGaugeOf(touchHoldC);
                if (value3.Gauge != null)
                {
                    value3.OriginalGaugeEnabled = value3.Gauge.enabled;
                    value3.OriginalGaugeAlpha = value3.Gauge.color.a;
                }
            }
            TouchMotionOwners.Add(owner, value3);
        }
        if (!float.IsNaN(value3.LastMsec) && currentMsec + 1f < value3.LastMsec)
        {
            value3.Alpha = 0f;
            value3.GaugeVisible = (value3.NoticeVisible = false);
        }
        value3.LastMsec = currentMsec;
        ScrollVisualTiming.TouchPresentation touchPresentation = ScrollVisualTiming.Touch(value, GetNoteScroll(noteIndex, currentMsec), TouchViewSpeed(num2, value2), currentMsec < num, value3.Alpha, value3.GaugeVisible);
        value3.Alpha = touchPresentation.Alpha;
        value3.GaugeVisible = touchPresentation.GaugeVisible;
        SpriteRenderer[] array = TouchPetalsOf(owner);
        UnityEngine.Vector3[] array2 = TouchPositionsOf(owner);
        for (int i = 0; i < array.Length && i < array2.Length; i++)
        {
            if (!(array[i] == null))
            {
                array[i].transform.localPosition = array2[i] * (touchPresentation.Distance / 0.4f);
                Color color = array[i].color;
                color.a = touchPresentation.Alpha;
                array[i].color = color;
            }
        }
        SpriteRenderer spriteRenderer = NoteSpriteOf(owner);
        if (spriteRenderer != null)
        {
            Color color2 = spriteRenderer.color;
            color2.a = touchPresentation.Alpha;
            spriteRenderer.color = color2;
        }
        if (value3.Gauge != null)
        {
            value3.Gauge.enabled = value3.OriginalGaugeEnabled && touchPresentation.GaugeVisible;
            Color color3 = value3.Gauge.color;
            color3.a = touchPresentation.Alpha;
            value3.Gauge.color = color3;
        }
        GameObject gameObject = TouchNoticeOf(owner);
        if (!(owner is TouchHoldC) && gameObject != null)
        {
            if (touchPresentation.Timing > -0.02f)
            {
                value3.NoticeVisible = true;
            }
            gameObject.SetActive(value3.NoticeVisible && !IsFakeNoteOwner(owner) && ShouldShowMineFeedback(owner));
        }
        value3.Applied = true;
    }

    [HarmonyPatch(typeof(TouchNoteB), "Initialize")]
    public static class TouchMotionInitializePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void Prefix(TouchNoteB __instance) {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }
        RestoreTouchMotion(__instance, true);
    }
    }
    [HarmonyPatch(typeof(TouchNoteB), "GetNoteYPosition")]
    public static class TouchReferenceMotionPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void Prefix(TouchNoteB __instance) {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }
        RestoreTouchMotion(__instance, false);
    }
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        public static void Postfix(TouchNoteB __instance) {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }
        ApplyTouchMotion(__instance);
    }
        // Original visual function and NoteCheck execute on the real clock.
        // The postprocess writes only petal positions, alpha and display visibility.
    }


    private static readonly List<TouchReserve> TouchReservePruneScratch = new List<TouchReserve>();

    private static readonly List<KeyValuePair<TouchReserve, ReserveVisual>> ReserveVisualRebuildScratch = new List<KeyValuePair<TouchReserve, ReserveVisual>>();

    private static int _touchReservePruneFrame = -1;
}
