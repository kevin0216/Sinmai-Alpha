using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Notes.Libs;

internal static class Program
{
    private static int checks;
    private static readonly Random Random = new(160170);
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    private static void Equal<T>(T expected, T actual, string message) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), message + $": {expected} != {actual}");

    private static void Main()
    {
        VisualLookup();
        NoiseSpeed();
        NoiseTimeline();
        NoiseSelection();
        OwnerScopes();
        JudgeGeometry();
        AllocationChecks();
        Console.WriteLine($"PASS: {checks:N0} checks (seed 160170)");
    }

    private static void SameVisual(VisualValue expected, VisualValue actual)
    {
        Equal(expected.Color, actual.Color, "visual color");
        Equal(expected.X, actual.X, "visual X");
        Equal(expected.Y, actual.Y, "visual Y");
        Equal(expected.Alpha, actual.Alpha, "visual alpha");
    }

    private static void VisualLookup()
    {
        var types = new[] { "", "mine", "break", "each", "tap", "star", "slide", "touch", "blockarea", null };
        var kinds = new[] { "color", "size", "alpha" };
        var scratch = new VisualValue();
        for (var trial = 0; trial < 45; trial++)
        {
            var changes = new List<VisualChange>();
            for (var i = 0; i < 180; i++)
            {
                var kind = kinds[Random.Next(kinds.Length)];
                changes.Add(new VisualChange {
                    Time = trial % 3 == 0 ? Random.Next(-10, 40) : i / 5d,
                    Type = types[Random.Next(types.Length)], Kind = kind,
                    Value = Random.Next(5) == 0 ? new VisualValue() : new VisualValue {
                        Color = kind == "color" ? Random.Next(0x1000000) : null,
                        X = kind == "size" ? (float)Random.NextDouble() : null,
                        Y = kind == "size" ? (float)Random.NextDouble() : null,
                        Alpha = kind == "alpha" ? (float)Random.NextDouble() : null
                    }
                });
            }
            // Out-of-order streams exercise the original reverse-scan fallback.
            for (var sample = 0; sample < 100; sample++)
            {
                var time = sample < 80 ? Random.NextDouble() * 50 - 10 : sample - 80d;
                if (sample == 97) time = double.NaN;
                if (sample == 98) time = double.PositiveInfinity;
                if (sample == 99) time = double.NegativeInfinity;
                foreach (var family in types)
                    for (var flags = 0; flags < 8; flags++)
                    {
                        var note = new VisualNote { Family = family, Mine = (flags & 1) != 0,
                            Break = (flags & 2) != 0, Each = (flags & 4) != 0 };
                        SameVisual(ReferenceVisualState.Resolve(changes, time, note),
                            VisualState.Resolve(changes, time, note, null, scratch));
                    }
            }
            changes.Add(new VisualChange { Time = 45, Type = "tap", Kind = "color", Value = new VisualValue { Color = 123 } });
            var tap = new VisualNote { Family = "tap" };
            SameVisual(ReferenceVisualState.Resolve(changes, 45, tap), VisualState.Resolve(changes, 45, tap));
            // Same-count replacement is explicitly invalidated at chart reload.
            changes[changes.Count - 1].Value = new VisualValue { Color = 456 };
            VisualState.ReleaseIndexes();
            SameVisual(ReferenceVisualState.Resolve(changes, 45, tap), VisualState.Resolve(changes, 45, tap));
        }
        Check(VisualState.TryParse("colorv", "noise=#123456", 0, out var parsed), "noise alias");
        Equal("blockarea", parsed[0].Type, "noise alias target");
        Console.WriteLine("PASS: indexed visuals, NULL inheritance, duplicates, seek, unsorted fallback, blockarea isolation");
    }

    private static NoiseControls Controls(int count)
    {
        var source = new NoiseControls();
        var values = new[] { "1", "-2", "0", "3:2", "NULL", "noise=4", "blockarea=NULL", "2,noise=-1", "noise=NULL,3" };
        for (var i = 0; i < count; i++)
            source.Entries.Add(new NoiseControls.Entry { Time = Random.Next(-20, 5) / 2d,
                Kind = Random.Next(2) == 0 ? "sv" : "hs", Value = values[Random.Next(values.Length)] });
        Check(NoiseControls.Decode(source.Encode(), out var decoded), "control round trip");
        return decoded;
    }

    // Independent scan oracle: the implementation before compiled speed tracks.
    private static float ScanSpeed(NoiseControls controls, string kind, double time)
    {
        float global = 1; float? typed = null;
        foreach (var entry in controls.Entries)
        {
            if (entry.Time > time + 1e-7) break;
            if (entry.Kind != kind) continue;
            foreach (var part in entry.Value.Split(','))
            {
                var eq = part.IndexOf('=');
                var raw = (eq < 0 ? part : part.Substring(eq + 1)).Trim();
                NoiseZoneSpec.TryMultiplier(raw, out var number);
                if (eq < 0) global = number;
                else typed = raw.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : number;
            }
        }
        return typed ?? global;
    }
    private static double ScanDistance(NoiseControls controls, double from, double to)
    {
        if (from > to) return -ScanDistance(controls, to, from);
        var cursor = from; double sum = 0;
        foreach (var entry in controls.Entries)
        {
            if (entry.Kind != "sv" || entry.Time <= from || entry.Time >= to) continue;
            sum += (entry.Time - cursor) * ScanSpeed(controls, "sv", cursor); cursor = entry.Time;
        }
        return sum + (to - cursor) * ScanSpeed(controls, "sv", cursor);
    }
    private static float ScanProgress(NoiseControls controls, double relative, double lead)
    {
        if (relative >= 0) return -1;
        var hs = ScanSpeed(controls, "hs", 0);
        if (Math.Abs(hs) <= 1e-6f) return 1;
        var duration = lead / Math.Abs(hs);
        var distance = Math.Abs(ScanDistance(controls, relative, 0));
        return distance >= duration ? -1 : (float)(1 - distance / duration);
    }
    private static void NoiseSpeed()
    {
        for (var trial = 0; trial < 80; trial++)
        {
            var controls = Controls(trial);
            var times = controls.Entries.SelectMany(e => new[] { e.Time, e.Time - 1e-7, e.Time - 2e-7, e.Time + 1e-7 })
                .Concat(new[] { -100d, 100d, double.NaN, double.NegativeInfinity, double.PositiveInfinity });
            foreach (var time in times)
                foreach (var kind in new[] { "sv", "hs" })
                    Equal(ScanSpeed(controls, kind, time), controls.Speed(kind, time), "speed boundary");
            for (var sample = 0; sample < 40; sample++)
            {
                var from = Random.NextDouble() * 14 - 12; var to = Random.NextDouble() * 14 - 12;
                Equal(ScanDistance(controls, from, to), controls.ScrollDistance(from, to), "signed scroll distance");
                Equal(ScanProgress(controls, from, 2), controls.WarningProgress(from, 2), "warning progress");
            }
            controls.Entries.Add(new NoiseControls.Entry { Time = 20, Kind = "sv", Value = "noise=-3" });
            Equal(ScanSpeed(controls, "sv", 25), controls.Speed("sv", 25), "appended control fallback");
        }
        var styleSource = new NoiseControls();
        styleSource.Entries.Add(new NoiseControls.Entry { Time = -1, Kind = "color", Value = "noise=#123456" });
        styleSource.Entries.Add(new NoiseControls.Entry { Time = 1, Kind = "colorv", Value = "noise=#ABCDEF" });
        Check(NoiseControls.Decode(styleSource.Encode(), out var style), "style decode");
        var into = new VisualValue();
        Equal((int?)0xabcdef, style.Style(2, into).Color, "live style");
        Equal((int?)0x123456, style.Style(0, into).Color, "style rewind resets scratch");
        Console.WriteLine("PASS: compiled SV/HS, typed resets, tolerance boundaries, reverse scroll and warning progress");
    }

    private static void NoiseTimeline()
    {
        for (var trial = 0; trial < 16; trial++)
        {
            var events = Enumerable.Range(0, 160).Select(_ => new NoiseZoneEvent(Random.Next(33),
                Random.Next(-8, 40) / 2d, Random.Next(15) / 4d, Random.Next(10) / 5d)).ToList();
            var timeline = new NoiseZoneTimeline(); timeline.Configure(events);
            var reference = new ReferenceNoiseZoneTimeline(); reference.Configure(events);
            foreach (var time in events.SelectMany(e => new[] { e.Start, e.End, e.WarningStart, e.End - 1e-7 }).Reverse())
            {
                var oldWarnings = new float[33]; var oldActive = new bool[33];
                var warnings = new float[33]; var active = new bool[33]; var shines = new float[33];
                reference.Evaluate(time, oldWarnings, oldActive);
                timeline.EvaluateFrame(time, warnings, active, shines);
                for (var sensor = 0; sensor < 33; sensor++)
                {
                    Equal(reference.IsBlocked(sensor, time), timeline.IsBlocked(sensor, time), "blocked interval");
                    Equal(oldWarnings[sensor], warnings[sensor], "timeline warning");
                    Equal(oldActive[sensor], active[sensor], "timeline active");
                    Equal(reference.WarningShine(sensor, time), shines[sensor], "timeline shine");
                    var oldFound = reference.TryGetVisualSpan(sensor, time, out var oldStart, out var oldEnd);
                    Equal(oldFound, timeline.TryGetVisualSpan(sensor, time, out var start, out var end), "visual span");
                    if (oldFound) { Equal(oldStart, start, "span start"); Equal(oldEnd, end, "span end"); }
                }
            }
            timeline.Configure(Array.Empty<NoiseZoneEvent>());
            Check(!timeline.IsBlocked(0, 0), "empty reconfigure");
        }
        Console.WriteLine("PASS: noise interval index, overlap, adjacent spans, short pulses and backwards seeks");
    }

    private static float Progress(NoiseZoneEvent zone, double time) => zone.Controls != null
        ? ScanProgress(zone.Controls, time - zone.Start, zone.NativeLead)
        : time < zone.WarningStart || time >= zone.Start ? -1
        : (float)((time - zone.WarningStart) / (zone.Start - zone.WarningStart));

    private static void NoiseSelection()
    {
        var controls = Controls(30);
        var zones = Enumerable.Range(0, 180).Select(i => new NoiseZoneEvent(Random.Next(33),
            Random.Next(30) / 2d, Random.Next(12) / 5d, 2, i % 3 == 0 ? controls : null)).ToList();
        var warnings = new float[33]; var selected = new NoiseZoneEvent?[33];
        foreach (var time in zones.SelectMany(e => new[] { e.Start, e.End, e.WarningStart, e.Start + .12 }).Reverse())
        {
            NoiseVisualState.Evaluate(zones, time, warnings, selected);
            for (var sensor = 0; sensor < 33; sensor++)
            {
                NoiseZoneEvent? expected = null; float warning = 0;
                foreach (var zone in zones)
                {
                    if (zone.Sensor != sensor) continue;
                    var progress = Progress(zone, time);
                    if (progress >= 0) warning = Math.Max(warning, 1 - .55f * progress);
                    if (time >= zone.End || time < zone.Start && progress < 0) continue;
                    if (!expected.HasValue || zone.Start <= time && expected.Value.Start > time ||
                        (zone.Start <= time) == (expected.Value.Start <= time) &&
                        (zone.Start <= time ? zone.Start >= expected.Value.Start : zone.Start < expected.Value.Start)) expected = zone;
                }
                Equal(warning, warnings[sensor], "single-pass warning");
                Equal(expected, selected[sensor], "single-pass selected style event");
            }
        }
        Console.WriteLine("PASS: single-pass style selection vs original sensor scans, including SV/HS and equal-time ties");
    }

    private static void OwnerScopes()
    {
        var scopes = new OwnerCallScope<string>();
        var first = new object(); var second = new object();
        var outer = scopes.Enter(first, "mine");
        var other = scopes.Enter(second, "touch");
        Equal("mine", scopes.For(first), "two-player outer route");
        Equal("touch", scopes.For(second), "two-player inner route");
        var stock = scopes.Enter(first, null);
        Check(scopes.For(first) == null, "stock re-entry masks custom route");
        try { throw new InvalidOperationException(); }
        catch (InvalidOperationException) { }
        finally { scopes.Exit(stock); }
        Equal("mine", scopes.For(first), "exception finalizer restores outer route");
        scopes.Exit(other); scopes.Exit(outer); scopes.Exit(outer);
        Check(scopes.For(first) == null, "finalizer is idempotent");
        var stale = scopes.Enter(first, "old chart"); scopes.Clear();
        var current = scopes.Enter(first, "new chart"); scopes.Exit(stale);
        Equal("new chart", scopes.For(first), "stale finalizer after reset");
        Exception error = null;
        var thread = new Thread(() => { try {
            Check(scopes.For(first) == null, "thread isolation");
            var state = scopes.Enter(first, "worker"); scopes.Exit(state);
        } catch (Exception e) { error = e; } });
        thread.Start(); thread.Join();
        if (error != null) throw error;
        Equal("new chart", scopes.For(first), "main-thread scope intact"); scopes.Exit(current);
        using var entered = new ManualResetEventSlim(); using var reset = new ManualResetEventSlim();
        thread = new Thread(() => { try {
            var state = scopes.Enter(first, "stale worker"); entered.Set(); reset.Wait();
            Check(scopes.For(first) == null, "reset invalidates other thread's old chart");
            var next = scopes.Enter(first, "new worker"); scopes.Exit(state);
            Equal("new worker", scopes.For(first), "old worker finalizer cannot pop new scope");
            scopes.Exit(next); Check(scopes.For(first) == null, "old chart does not return after worker exits");
        } catch (Exception e) { error = e; } });
        thread.Start(); entered.Wait(); scopes.Clear(); reset.Set(); thread.Join();
        if (error != null) throw error;
        Console.WriteLine("PASS: owner-scoped note routing, nested stock notes, exceptions, reset and threads");
    }

    private static void JudgeGeometry()
    {
        Equal(Vector2.Zero, SssJudgePathGeometry.Center(16), "C1 center");
        Equal(Vector2.Zero, SssJudgePathGeometry.Center(17), "C2 center");
        // Native first-region centers: A/B at 67.5 degrees, D/E at 90 degrees.
        Check(Vector2.Distance(SssJudgePathGeometry.Center(0), new Vector2(168.3807f, 406.5070f)) < .001f, "A1 center");
        Check(Vector2.Distance(SssJudgePathGeometry.Center(18), new Vector2(0, 430.5f)) < .001f, "D1 center");
        for (var area = 0; area < 34; area++)
            for (var button = 0; button < 8; button++)
            {
                var physical = SssJudgePathGeometry.PhysicalArea(area, button);
                Equal(area, SssJudgePathGeometry.PhysicalArea(physical, 8 - button), "rotation round trip");
                var point = SssJudgePathGeometry.Center(area);
                var rotated = Vector2.Transform(point, Matrix3x2.CreateRotation((float)(-button * Math.PI / 4)));
                Check(Vector2.Distance(rotated, SssJudgePathGeometry.Center(physical)) < .001f, "rotation geometry");
            }
    }

    private static void AllocationChecks()
    {
        var controls = Controls(200);
        var changes = new List<VisualChange> { new() { Time = 0, Kind = "color", Type = "tap", Value = new VisualValue { Color = 123 } } };
        var note = new VisualNote { Family = "tap" }; var into = new VisualValue();
        for (var i = 0; i < 20000; i++) { controls.Speed("sv", i % 20); VisualState.Resolve(changes, i, note, null, into); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20000; i++) { controls.Speed("sv", i % 20); VisualState.Resolve(changes, i, note, null, into); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated, "warmed speed/visual lookup allocations");
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 2000; i++) ScanSpeed(controls, "sv", i % 20);
        var scan = watch.Elapsed.TotalMilliseconds; watch.Restart();
        for (var i = 0; i < 2000; i++) controls.Speed("sv", i % 20);
        Console.WriteLine($"CPU sample (.NET, not Unity): 2,000 speed reads scan={scan:F2}ms compiled={watch.Elapsed.TotalMilliseconds:F2}ms; warmed lookup bytes={allocated}");
    }
}
