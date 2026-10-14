using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using SinmaiAlpha.ChartVisuals;
using DB;
using HarmonyLib;
using MAI2.Util;
using Manager;
using MelonLoader;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class NoiseChart
    {
        public readonly List<NoiseZoneEvent> Events = new();
        public readonly NoiseZoneTimeline Timeline = new();
    }
    private static ConditionalWeakTable<NotesReader, NoiseChart> NoiseCharts = new();
    private static List<(int Bar, int Grid, string Text)> PendingNoiseZones => RuntimeCharts.Current.PendingNoiseZones;
    [HarmonyPatch(typeof(NotesRecord), "addRecord", new[] { typeof(string) })]
    public static class NoiseRecordPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string str)
        {
        if (!(CustomNoteTypes.FeaturesEnabled())) {  return; }

            if (str == null) return;
            var p = str.Split('\t');
            if (p.Length == 4 && p[0] == "NZONE" &&
                int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var bar) &&
                int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out var grid))
                PendingNoiseZones.Add((bar, grid, p[3]));
        }
    }
    [HarmonyPatch]
    public static class NoiseReaderResetPatch
    {
        public static IEnumerable<MethodBase> TargetMethods() => typeof(NotesReader).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.Name == "loadMa2" || m.Name == "loadStr" || m.Name == "loadDLMusicScore");
        [HarmonyPrefix]
        public static void Prefix(NotesReader __instance)
        { NoisePlaybackStopped = false; ResetNoisePresentation(__instance); PendingNoiseZones.Clear(); NoiseCharts.Remove(__instance); }
    }
    [HarmonyPatch(typeof(NotesReader), "loadMa2Main")]
    public static class NoiseReaderBuildPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NotesReader __instance, int ____playerID)
        {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

            var chart = new NoiseChart();
            var option = Singleton<GamePlayManager>.Instance.GetGameScore(____playerID).UserOption;
            var speed = GetNoisePlayerTapSpeed(option.GetNoteSpeed);
            foreach (var record in PendingNoiseZones)
            {
                if (!NoiseZoneSpec.TryDecode(record.Text, out var spec))
                { MelonLogger.Warning("[Noise Zone] Invalid composition record: " + record.Text); continue; }
                var start = new NotesTime(); start.init(record.Bar, record.Grid, __instance);
                chart.Events.Add(new NoiseZoneEvent(spec.Sensor, start.msec / 1000d, spec.Duration,
                    spec.Controls == null ? NoiseWarningLead(speed, spec.Hs) : 240d / (option.GetTouchSpeed.IsValid() ? option.GetTouchSpeed.GetValue() : 850f) * 1.25, spec.Controls));
            }
            chart.Timeline.Configure(chart.Events);
            NoiseCharts.Remove(__instance); NoiseCharts.Add(__instance, chart);
            if (chart.Events.Count > 0) MelonLogger.Msg("[Noise Zone] Loaded " + chart.Events.Count + " independent region events; no score notes added");
        }
    }
    internal static bool IsNoiseAreaBlocked(int monitor, int nativeArea)
    {
        int num = NoiseZoneSpec.NativeSensor(nativeArea);
        if (num < 0 || monitor < 0 || monitor > 1)
        {
            return false;
        }
        NotesManager notesManager = NotesManager.Instance(monitor);
        if (notesManager == null || !notesManager.IsPlaying())
        {
            return false;
        }
        NotesReader reader = notesManager.getReader();
        if (reader == null || !NoiseCharts.TryGetValue(reader, out var value) || value.Events.Count == 0)
        {
            return false;
        }
        if (!NoiseInputDisplayReady(monitor, reader))
        {
            return false;
        }
        return value.Timeline.IsBlocked(num, (double)NotesManager.GetCurrentMsec() / 1000.0);
    }

}
