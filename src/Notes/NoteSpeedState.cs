using System.Collections.Generic;
using System.Linq;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Notes.Libs;
using HarmonyLib;
using Manager;
using Monitor;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    public static void ResetStreamedChartSpeedAndVisuals(NotesReader __instance)
    {
        LoadMa2SvResetPrefix(); ResetVisualCommands(__instance);
    }
    [HarmonyPatch]
    public static class StreamedChartResetPatch
    {
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() => typeof(NotesReader)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(m => m.Name == "loadDLMusicScore" || m.Name == "loadStr");
        [HarmonyPrefix]
        public static void Prefix(NotesReader __instance) => ResetStreamedChartSpeedAndVisuals(__instance);
    }
    private static VisualNote SpeedClass(NoteData note)
    {
        if (VisualNotes.TryGetValue(note, out var payload)) return payload.Note;
        return new VisualNote { Family = ResolveSpeedType(note), Each = note.isEach, Break = note.type.isBreak(),
            Mine = GetNoteKind(note) is CustomNoteKind.Mine or CustomNoteKind.MineTouchBreak or CustomNoteKind.MineTouchStar };
    }
    private static IEnumerable<string> SpeedPriority(VisualNote note)
    {
        if (note.Mine) yield return "mine";
        if (note.Break) yield return "break";
        if (note.Each) yield return "each";
        yield return note.Family;
    }
    private static string ResolvePlayableSvType(NoteData note)
    {
        foreach (var type in SpeedPriority(SpeedClass(note)))
            if (SvCurves.ContainsKey(type) || SvClearTimes.ContainsKey(type)) return type;
        return ResolveSpeedType(note);
    }
    private static float? HsAt(string type, float msec)
    {
        var last = float.NegativeInfinity; float? result = null;
        if (HsCurves.TryGetValue(type, out var curve))
            foreach (var entry in curve) { if (entry.Msec > msec) break; last = entry.Msec; result = entry.Mult; }
        if (HsClearTimes.TryGetValue(type, out var resets))
            foreach (var reset in resets) { if (reset > msec) break; if (reset >= last) { last = reset; result = null; } }
        return result;
    }
    private static float ResolvePlayableHs(NoteData note)
    {
        if (StreamTypeByNoteIndex.TryGetValue(note.indexNote, out var stream)) return HsAt(stream, note.time.msec) ?? 1;
        // Slide HS controls appearance only and does not inherit global/head HS.
        if (note.type.isAllSlide()) return HsAt("slide", note.time.msec) ?? 1;
        foreach (var type in SpeedPriority(SpeedClass(note)))
        { var value = HsAt(type, note.time.msec); if (value.HasValue) return value.Value; }
        return HsAt("", note.time.msec) ?? 1;
    }
    [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(SlideRoot), "Initialize")]
    public static void ApplyNativeSlideAppearanceHs(SlideRoot __instance, NoteData note)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        ApplySlideAppearanceHs(__instance, note);
    }
    [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(SlideFan), "Initialize")]
    public static void ApplyNativeFanAppearanceHs(SlideFan __instance, NoteData note)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        ApplySlideAppearanceHs(__instance, note);
    }
    private static void ApplySlideAppearanceHs(SlideRoot owner, NoteData note)
    {
        RegisterSlideAppearance(owner, note);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SlideRoot), "MoveStarLane")]
    public static void ApplyNativeSlideSvMotion(SlideRoot __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        ApplySlideSvMotion(__instance, false);
        ApplySlideStarAppearance(__instance);
    }
    [HarmonyPostfix, HarmonyPatch(typeof(SlideFan), "MoveStarLane")]
    public static void ApplyNativeFanSvMotion(SlideFan __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        ApplySlideSvMotion(__instance, true);
        ApplySlideStarAppearance(__instance);
    }
    private static void ApplySlideSvMotion(SlideRoot owner, bool fan)
    {
        NoteData note = SlideNoteDataOf(owner);
        if (note == null)
        {
            return;
        }
        CustomSlideNoteData touch = note as CustomSlideNoteData;
        VisualNotes.TryGetValue(note, out var value);
        if (touch?.TouchGeometry == null && value?.SlideGeometry == null && !MajdataHasSlideSvFor(note))
        {
            return;
        }
        ApplySlideSvMotionCore(owner, fan, note, touch, value);
    }
}
