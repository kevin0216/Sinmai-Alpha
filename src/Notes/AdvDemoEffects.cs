using Manager;
using Monitor;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    // Attract demo: AdvDemoMonitor shows its note field only while IsActiveNotesField()
    // is true (it hides _NoteRoot for the ranking sequences and after PlayStop). Chart
    // effects must not draw or play over those screens.
    private static readonly AdvDemoMonitor[] AdvDemoMonitors = new AdvDemoMonitor[2];

    internal static bool ChartEffectsHidden(GameMonitor monitor)
        => GameManager.IsAdvDemo && monitor is AdvDemoMonitor demo && demo != null && !demo.IsActiveNotesField();

    // For hooks that only know the monitor index (GameCtrl has no monitor reference).
    internal static bool ChartEffectsHidden(int monitorIndex)
        => GameManager.IsAdvDemo && monitorIndex >= 0 && monitorIndex < AdvDemoMonitors.Length &&
            ChartEffectsHidden(AdvDemoMonitors[monitorIndex]);

    // Called from every GameMonitor.ViewUpdate; any other monitor clears a stale slot.
    private static void TrackAdvDemoMonitor(GameMonitor monitor)
    {
        var index = monitor != null ? monitor.MonitorIndex : -1;
        if (index < 0 || index >= AdvDemoMonitors.Length) return;
        var demo = GameManager.IsAdvDemo ? monitor as AdvDemoMonitor : null;
        if (!ReferenceEquals(AdvDemoMonitors[index], demo)) AdvDemoMonitors[index] = demo;
    }

    // The same per-monitor releases as the hidden single-player sub monitor, so each
    // Apply* binds again once the field is shown. Subtitles and media are only
    // suspended: resuming must not rebuild every label or reload video and audio.
    private static void SuspendChartPresentation(GameMonitor monitor)
    {
        ReleaseJudgeLinePresentation(monitor);
        JudgeLineExpansionRuntime.Release(monitor);
        if (SubtitleDisplays.TryGetValue(monitor, out var subtitles) && subtitles != null && subtitles.enabled) subtitles.Stop();
        ApplyMediaPresentation(monitor); // takes its stopped-playback path while hidden
        ReleaseComboPresentation(monitor);
        ReleaseNoisePresentation(monitor);
        ReleaseJudgeTextPresentation(monitor);
        foreach (var motion in PresentationMotionCameras.Values) if (motion != null) motion.Unbind(monitor);
        foreach (var camera in PresentationCameras.Values) if (camera != null) camera.Unbind(monitor);
    }
}
