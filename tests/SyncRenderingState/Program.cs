using System;
using System.Reflection;
using System.Runtime.Serialization;
using SinmaiAlpha.ChartVisuals;
using UnityEngine;

// Exercises the actual built DLL's managed cache decisions. No Unity player,
// camera, shader or native rendering is invoked by this executable.
internal static class Program
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Any).SetValue(owner, value);
    private static object Get(object owner, string field) => owner.GetType().GetField(field, Any).GetValue(owner);
    private static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Any).Invoke(owner, args);

    private static void Main()
    {
        var displayType = typeof(NoiseZoneTimeline).Assembly.GetType("SinmaiAlpha.Notes.CustomNoteTypes+NoiseZoneDisplay", true);
        var display = FormatterServices.GetUninitializedObject(displayType);
        var visualType = displayType.GetNestedType("NoiseZoneVisual", BindingFlags.NonPublic);
        var styles = Array.CreateInstance(visualType, 33);
        for (var i = 0; i < 33; i++) styles.SetValue(Activator.CreateInstance(visualType, new object[] { null }), i);
        var visible = new bool[33]; visible[0] = true;
        var timeline = new NoiseZoneTimeline(); timeline.Configure(new[] { new NoiseZoneEvent(0, 0, 2, 1) });
        double time = .5;
        Set(display, "Clock", (Func<double>)(() => time)); Set(display, "timeline", timeline);
        Set(display, "visible", visible); Set(display, "styles", styles);
        var sizes = new Vector2[33]; var colors = new Color32[33];
        bool Changed()
        {
            object[] args = { sizes, colors, false };
            var bits = (ulong)Call(display, "ActiveMaskState", args);
            Check(bits == (visible[0] ? 1UL : 0UL), "active sensor bits");
            return (bool)args[2];
        }
        Check(Changed(), "initial style populates cache");
        Check(!Changed(), "steady zone reuses raster");
        time = .1; Check(Changed(), "growth redraws on seek");
        time = 1.9; Check(Changed(), "shrink redraws");
        time = 1.7; Check(!Changed(), "stable middle after rewind");
        styles.SetValue(Activator.CreateInstance(visualType, new object[] { new VisualValue { Color = 0x123456 } }), 0);
        Check(Changed(), "live color invalidates mask"); Check(!Changed(), "unchanged color reuses mask");
        styles.SetValue(Activator.CreateInstance(visualType, new object[] { new VisualValue { Color = 0x123456, X = 2, Y = -1, Alpha = .5f } }), 0);
        Check(Changed(), "live size and alpha invalidate mask");
        timeline.Configure(new[] { new NoiseZoneEvent(0, 0, .05, 1) }); time = .08;
        Check(!Changed(), "short visual pulse has no growth window");
        visible[0] = false; Changed();

        Set(display, "warningZones", new bool[33]); Set(display, "warnings", new float[33]);
        Set(display, "warningShines", new float[33]); Set(display, "meshZones", new bool[33]);
        Set(display, "meshWarnings", new float[33]); Set(display, "meshShines", new float[33]);
        Set(display, "meshStyles", Array.CreateInstance(visualType, 33));
        Check(!(bool)Call(display, "SameMeshInputs"), "initial empty mesh is cleared");
        Check((bool)Call(display, "SameMeshInputs"), "empty mesh stays cached");
        ((bool[])Get(display, "warningZones"))[0] = true;
        ((float[])Get(display, "warnings"))[0] = .75f;
        Check(!(bool)Call(display, "SameMeshInputs"), "warning onset rebuilds mesh");
        Check((bool)Call(display, "SameMeshInputs"), "unchanged warning reuses mesh");
        ((float[])Get(display, "warningShines"))[0] = .04f;
        Check(!(bool)Call(display, "SameMeshInputs"), "warning animation rebuilds mesh");
        styles.SetValue(Activator.CreateInstance(visualType, new object[] { new VisualValue { Color = 0x123456, X = 2, Y = -1, Alpha = .50001f } }), 0);
        Check(!(bool)Call(display, "SameMeshInputs"), "mesh uses full float alpha, not quantized mask alpha");
        ((bool[])Get(display, "warningZones"))[0] = false;
        Check(!(bool)Call(display, "SameMeshInputs"), "last warning is cleared");
        Console.WriteLine($"PASS: {checks} built-DLL noise cache checks; no GPU rendering performed");
    }
}
