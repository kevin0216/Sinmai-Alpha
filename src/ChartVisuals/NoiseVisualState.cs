using System;
using System.Collections.Generic;

namespace SinmaiAlpha.ChartVisuals;

// Preserve authored order when two regions have the same start time. Evaluate
// warning controls once per event, instead of once per event per sensor.
internal static class NoiseVisualState
{
    internal static void Evaluate(IReadOnlyList<NoiseZoneEvent> events, double time,
        float[] warnings, NoiseZoneEvent?[] selected)
    {
        Array.Clear(warnings, 0, warnings.Length);
        Array.Clear(selected, 0, selected.Length);
        for (var i = 0; i < events.Count; i++)
        {
            var zone = events[i];
            var progress = zone.Controls != null
                ? zone.Controls.WarningProgress(time - zone.Start, zone.NativeLead)
                : time < zone.WarningStart || time >= zone.Start ? -1
                : (float)((time - zone.WarningStart) / (zone.Start - zone.WarningStart));
            if (progress >= 0) warnings[zone.Sensor] = Math.Max(warnings[zone.Sensor], 1 - .55f * progress);
            if (time >= zone.End || time < zone.Start && progress < 0) continue;
            var previous = selected[zone.Sensor];
            if (!previous.HasValue || zone.Start <= time && previous.Value.Start > time ||
                (zone.Start <= time) == (previous.Value.Start <= time) &&
                (zone.Start <= time ? zone.Start >= previous.Value.Start : zone.Start < previous.Value.Start))
                selected[zone.Sensor] = zone;
        }
    }
}
