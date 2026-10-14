using MelonLoader;
using SinmaiAlpha.ChartVisuals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DB;
using HarmonyLib;
using MAI2.Util;
using Manager;
using Monitor;
using Monitor.Game;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private static readonly FieldInfo RingNoteObject = AccessTools.Field(typeof(NoteBase), "NoteObj");
    private static readonly FieldInfo RingSprite = AccessTools.Field(typeof(NoteBase), "SpriteRender");
    private static readonly FieldInfo RingExSprite = AccessTools.Field(typeof(NoteBase), "SpriteRenderEx");
    private static readonly FieldInfo RingGuide = AccessTools.Field(typeof(NoteBase), "GuideObj");
    private static readonly FieldInfo RingBreakEffect = AccessTools.Field(typeof(BreakHoldNote), "BreakEffectSprite");
    private static bool _loggedRingVisualError;

    private static void SyncBreakHoldGlow(NoteBase owner, SpriteRenderer body)
    {
        // Runs every frame for each ring/bounce hold body. BreakEffectSprite through NoteRefs.BreakHoldGlow, with the
        // former FieldInfo read as fallback: same field, same reference, same reference-null `is` test.
        if (owner is BreakHoldNote breakHoldNote)
        {
            AccessTools.FieldRef<BreakHoldNote, SpriteRenderer> breakHoldGlow = NoteRefs.BreakHoldGlow;
            if (((breakHoldGlow != null) ? breakHoldGlow(breakHoldNote) : (RingBreakEffect.GetValue(owner) as SpriteRenderer)) is SpriteRenderer spriteRenderer)
            {
                spriteRenderer.size = body.size;
            }
        }
    }

    private static bool RingVisualActive(int index, float appear, float duration, float spawn, float now, float destroy = 4.8f, bool once = false)
    {
        if (!HsMultByNoteIndex.TryGetValue(index, out var value))
        {
            return false;
        }
        if ((Math.Abs(value - 1f) > 1E-05f || Math.Abs(spawn - 1.225f) > 1E-05f || Math.Abs(destroy - 4.8f) > 1E-05f) | once)
        {
            return true;
        }
        if (!NoteScrollTableByNoteIndex.TryGetValue(index, out List<(float, float, float)> value2))
        {
            return false;
        }
        float num = Math.Min(now, appear - duration * 2f);
        float num2 = Math.Max(now, appear);
        float noteScroll = GetNoteScroll(index, num);
        if (Math.Abs(GetNoteScroll(index, num2) - noteScroll - (num2 - num)) > 0.01f)
        {
            return true;
        }
        // The table is sorted by msec (BuildNoteScrollTable sorts with float.CompareTo, NaN first, and its merge
        // only raises the last entry), so once Item1 >= num2 no later entry can pass "Item1 < num2"; the scan stops
        // there. NaN entries (and a NaN num2) never break, and the unchanged test rejects them as before.
        for (int i = 0; i < value2.Count; i++)
        {
            (float, float, float) item = value2[i];
            if (item.Item1 >= num2)
            {
                break;
            }
            if (item.Item1 > num && item.Item1 < num2 && Math.Abs(item.Item2 - noteScroll - (item.Item1 - num)) > 0.01f)
            {
                return true;
            }
        }
        return false;
    }
    private static bool TryRingPresentation(NoteBase owner, bool tail, out ScrollVisualTiming.Presentation presentation)
    {
        presentation = default;
        if (owner == null || owner.IsEnd() || owner is TouchNoteB || owner is TouchHoldC)
        {
            return false;
        }
        int noteIndex = owner.GetNoteIndex();
        if (!SvScrollPosByNoteIndex.TryGetValue(noteIndex, out var value) || !SvTypeByNoteIndex.TryGetValue(noteIndex, out var value2))
        {
            return false;
        }
        float num = AppearMsecOf(owner);
        float currentMsec = NotesManager.GetCurrentMsec();
        if (currentMsec < num && BounceDurationByNoteIndex.TryGetValue(noteIndex, out var value3) && value3 > 0f)
        {
            return false;
        }
        float num2 = DefaultMsecOf(owner);
        RingValue settings = RingSettings(noteIndex, num, value2);
        if (num2 <= 0f || !RingVisualActive(noteIndex, num, num2, settings.Spawn, currentMsec, settings.Destroy, settings.Once))
        {
            return false;
        }
        float num3 = HsMultByNoteIndex[noteIndex];
        if (tail && !SvTailScrollPosByNoteIndex.TryGetValue(noteIndex, out value))
        {
            return false;
        }
        float speed = (ReferenceMotionByNoteIndex.Contains(noteIndex) ? (RingViewSpeed(num2) * 0.001f) : (3.5750003f / num2)) * num3;
        bool everCrossed = settings.Once && RingEverCrossed(owner, tail, value, speed, settings, currentMsec);
        presentation = ScrollVisualTiming.Ring(value, GetNoteScroll(noteIndex, currentMsec), speed, settings.Spawn, settings.Destroy, everCrossed);
        if ((owner is HoldNote || owner is BreakHoldNote) && currentMsec >= (tail ? TailMsecOf(owner) : num) && presentation.Running)
        {
            presentation = new ScrollVisualTiming.Presentation(settings.Destroy, 1f, running: true);
        }
        return true;
    }
    private static float RingNativeY(NoteBase owner, float radius)
    {
        float num = StartPosOf(owner);
        float num2 = EndPosOf(owner);
        return num + (num2 - num) * ((radius - 1.225f) / 3.5750003f);
    }
    private static float RingFirstVisibleTime(NoteData note, float duration)
    {
        var index = note.indexNote;
        if (!NoteScrollTableByNoteIndex.TryGetValue(index, out var table) || table.Count == 0 || duration <= 0) return float.NaN;
        var hs = HsMultByNoteIndex[index];
        var type = SvTypeByNoteIndex[index];
        var settings = RingSettings(index, note.time.msec, type);
        var speed = (ReferenceMotionByNoteIndex.Contains(index) ? RingViewSpeed(duration) * .001f : SpawnRadiusSpan / duration) * hs;
        // Zero speed is already on the judgment ring, independently of SV.
        if (Math.Abs(speed) <= .000000001) return table[0].Msec;
        var target = SvScrollPosByNoteIndex[index];
        if (speed < 0 && ReferenceMotionByNoteIndex.Contains(index) && ResolveSpeedType(note) != "hold")
        {
            // Negative HS approaches from outside the frame. Being past SPAWN
            // does not make a far-away object visible. A conservative 12-radius
            // envelope includes frame corners/native art; the caller retains
            // at least the ordinary activation lead. Hold bodies keep their
            // full-history activation because they can span across the frame.
            return ScrollVisualTiming.FirstViewportEntry(table, target, speed, settings.Spawn, settings.Destroy, 12);
        }
        var threshold = target - (Math.Abs(settings.Destroy - settings.Spawn) + 2.5f) / speed;
        bool Crossed(float scroll) => speed > 0 ? scroll >= threshold : scroll <= threshold;
        for (var i = 0; i < table.Count && table[i].Msec <= note.time.msec; i++)
        {
            var point = table[i];
            if (Crossed(point.Scroll)) return point.Msec;
            if (i + 1 == table.Count) break;
            var next = table[i + 1];
            if (!Crossed(next.Scroll)) continue;
            var delta = next.Scroll - point.Scroll;
            if (Math.Abs(delta) < .00001f) continue;
            return point.Msec + (next.Msec - point.Msec) * (threshold - point.Scroll) / delta;
        }
        return float.NaN;
    }

    // Different HS/SV can register a later note before an earlier note. Restore
    // time order in the native lane, whose IsJudgeNote uses sibling order.
    public static void OrderSignedRingInputQueue(GameCtrl __instance, NoteData note, bool __result)
    {
        if (!__result || note == null || HsMultByNoteIndex.Count == 0 || note.type.isAllSlide() || IsFakeNote(note)) return;
        var active = Traverse.Create(__instance).Field("_activeNoteList").GetValue<List<NoteBase>>();
        var owner = active?.FirstOrDefault(value => value != null && !value.IsEnd() && value.GetNoteIndex() == note.indexNote);
        var parent = owner != null ? owner.transform.parent : null;
        if (parent == null) return;
        var notes = new List<NoteBase>();
        for (var i = 0; i < parent.childCount; i++)
        {
            var candidate = parent.GetChild(i).GetComponent<NoteBase>();
            if (candidate != null && candidate.gameObject.activeSelf && !candidate.IsEnd()) notes.Add(candidate);
        }
        foreach (var candidate in notes.OrderBy(value => (float)FAppearMsec.GetValue(value)).ThenBy(value => value.GetNoteIndex()))
            candidate.transform.SetAsFirstSibling();
    }

    [HarmonyPatch(typeof(GameCtrl), "RegistNote")]
    public static class SignedRingQueuePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        public static void Postfix(GameCtrl __instance, NoteData note, bool __result)
            {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }
        OrderSignedRingInputQueue(__instance, note, __result);
    }
    }

    [HarmonyPatch]
    public static class SignedRingPresentationPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            // Hold/BreakHold and custom subclasses override both Execute and
            // NoteCheck. Postprocess their visuals once after native play logic.
            return typeof(NoteBase).Assembly.GetTypes().Concat(typeof(CustomNoteTypes).Assembly.GetTypes())
                .Where(type => typeof(NoteBase).IsAssignableFrom(type) && type != typeof(NoteBase) &&
                    !typeof(TouchNoteB).IsAssignableFrom(type) && !typeof(TouchHoldC).IsAssignableFrom(type))
                .Select(type => type.GetMethod("Execute", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(method => method != null && method.GetParameters().Length == 0).Distinct();
        }
        [HarmonyPostfix]
        [HarmonyPriority(0)]
        public static void Postfix(NoteBase __instance)
        {
            if (!FeaturesEnabled(__instance))
            {
                return;
            }
            try
            {
                if (TryBounceHoldPresentation(__instance) || !TryRingPresentation(__instance, tail: false, out var presentation))
                {
                    return;
                }
                GameObject gameObject = NoteObjectOf(__instance);
                if (gameObject == null)
                {
                    return;
                }
                float value = Singleton<GamePlayManager>.Instance.GetGameScore(__instance.MonitorId).UserOption.NoteSize.GetValue();
                float num = RingNativeY(__instance, presentation.Radius);
                UnityEngine.Vector3 localPosition = gameObject.transform.localPosition;
                float num2 = 1f;
                bool flag = __instance is HoldNote || __instance is BreakHoldNote;
                if (flag)
                {
                    GameObject value2 = EndPointOf(__instance);
                    ScrollVisualTiming.Presentation presentation2 = (TryRingPresentation(__instance, tail: true, out var presentation3) ? presentation3 : presentation);
                    float num3 = RingNativeY(__instance, presentation2.Radius);
                    bool flag2 = presentation.Running && NotesManager.GetCurrentMsec() < TailMsecOf(__instance);
                    float y = (flag2 ? (Math.Abs(num - num3) + 140f) : 140f);
                    if (flag2)
                    {
                        num2 = ((num >= num3) ? 1 : (-1));
                        localPosition.y = (num + num3) * 0.5f;
                    }
                    else
                    {
                        localPosition.y = num;
                    }
                    SpriteRenderer spriteRenderer = NoteSpriteOf(__instance);
                    spriteRenderer.size = new UnityEngine.Vector2(spriteRenderer.size.x, y);
                    SpriteRenderer spriteRenderer2 = NoteSpriteExOf(__instance);
                    if (spriteRenderer2 != null)
                    {
                        spriteRenderer2.size = spriteRenderer.size;
                    }
                    SpriteRenderer value3 = EffectSpriteOf(__instance);
                    if (value3 != null)
                    {
                        value3.size = spriteRenderer.size;
                    }
                    SyncBreakHoldGlow(__instance, spriteRenderer);
                    if (value2 != null)
                    {
                        value2.SetActive(flag2 && presentation2.Running);
                        UnityEngine.Vector3 localPosition2 = value2.transform.localPosition;
                        localPosition2.y = num3;
                        value2.transform.localPosition = localPosition2;
                        value2.transform.localScale = new UnityEngine.Vector3(presentation.Scale, presentation.Scale, 1f);
                    }
                }
                else
                {
                    localPosition.y = num;
                }
                gameObject.transform.localPosition = localPosition;
                gameObject.transform.localScale = new UnityEngine.Vector3(presentation.Scale * value, presentation.Scale * (flag ? num2 : value), 0f);
                NoteGuide noteGuide = NoteGuideOf(__instance);
                if (noteGuide != null)
                {
                    float num4 = Math.Abs(presentation.Radius / 4.8f);
                    noteGuide.transform.localScale = new UnityEngine.Vector3(num4, num4, 1f);
                    noteGuide.SetAlpha(presentation.Scale);
                }
            }
            catch (Exception ex)
            {
                if (!_loggedRingVisualError)
                {
                    _loggedRingVisualError = true;
                    MelonLogger.Warning("[CustomNoteType] Signed ring presentation: " + ex.Message);
                }
            }
        }
    }
}
