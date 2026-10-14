using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Hosting;
using Manager;
using Monitor;
using Monitor.Game;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class JudgeLineTimeline
    {
        public PresentationChange[] Changes;
    }
    private sealed class NativeJudgeLineLease
    {
        public NotesReader Reader;
        public JudgeLineTimeline Timeline;
        public SpriteRenderer Renderer;
        public Color Original;
        public JudgeLineCommands.Track Track;
        public void Restore() { if (Renderer != null) Renderer.color = Original; }
        public void Apply(double milliseconds)
        {
            if (Renderer == null) return;
            var tint = Track.Evaluate(milliseconds);
            Renderer.color = new Color(tint.R, tint.G, tint.B, tint.A);
        }
    }
    private static ConditionalWeakTable<NotesReader, JudgeLineTimeline> JudgeLineTimelines = new();
    private static readonly Dictionary<GameMonitor, NativeJudgeLineLease> JudgeLineLeases = new();

    // Called with the decoded presentation events once per native reader.
    private static void BuildJudgeLineCommands(NotesReader reader, IEnumerable<PresentationChange> changes)
    {
        ResetJudgeLineCommands(reader);
        var events = changes.Where(c => JudgeLineCommands.IsKind(c.Kind)).ToArray();
        if (events.Length != 0) JudgeLineTimelines.Add(reader, new JudgeLineTimeline { Changes = events });
    }
    private static void ApplyJudgeLinePresentation(GameMonitor monitor)
    {
        if (monitor == null)
        {
            return;
        }
        if (GuiSizes.SinglePlayer && monitor.MonitorIndex != 0)
        {
            ReleaseJudgeLinePresentation(monitor);
            return;
        }
        NotesReader reader = NotesManager.Instance(monitor.MonitorIndex).getReader();
        if (!JudgeLineTimelines.TryGetValue(reader, out var value))
        {
            ReleaseJudgeLinePresentation(monitor);
            return;
        }
        GameCtrl value2 = ControllerOf(monitor);
        GameObject gameObject = ((value2 != null) ? GuideEndPointOf(value2) : null);
        SpriteRenderer spriteRenderer = ((gameObject != null) ? gameObject.GetComponent<SpriteRenderer>() : null);
        if (spriteRenderer == null)
        {
            ReleaseJudgeLinePresentation(monitor);
            return;
        }
        if (!JudgeLineLeases.TryGetValue(monitor, out var value3) || value3.Reader != reader || value3.Timeline != value || value3.Renderer != spriteRenderer)
        {
            ReleaseJudgeLinePresentation(monitor);
            Color color = spriteRenderer.color;
            value3 = new NativeJudgeLineLease
            {
                Reader = reader,
                Timeline = value,
                Renderer = spriteRenderer,
                Original = color,
                Track = new JudgeLineCommands.Track(value.Changes, new JudgeLineCommands.Tint(color.r, color.g, color.b, color.a))
            };
            JudgeLineLeases.Add(monitor, value3);
        }
        value3.Apply(NotesManager.GetCurrentMsec());
    }
    private static void ReleaseJudgeLinePresentation(GameMonitor monitor)
    {
        if (!JudgeLineLeases.TryGetValue(monitor, out var lease)) return;
        lease.Restore(); JudgeLineLeases.Remove(monitor);
    }
    private static void ResetJudgeLineCommands(NotesReader reader)
    {
        foreach (var monitor in JudgeLineLeases.Where(p => p.Value.Reader == reader).Select(p => p.Key).ToArray())
            ReleaseJudgeLinePresentation(monitor);
        JudgeLineTimelines.Remove(reader);
    }
    private static void ReleaseJudgeLinePresentation()
    {
        foreach (var lease in JudgeLineLeases.Values) lease.Restore();
        JudgeLineLeases.Clear();
    }
}
