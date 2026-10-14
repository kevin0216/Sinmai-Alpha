// Scan-based reference from source commit cd15585, before the MaiMagic sync.
using System;
using System.Collections.Generic;
namespace SinmaiAlpha.ChartVisuals
{
    internal static class ReferenceVisualState
    {
        public static VisualValue Resolve(IList<VisualChange> changes, double time, VisualNote note, string family = null)
        {
            family = family ?? note.Family;
            VisualValue Lookup(string kind, string type)
            {
                for (var i = changes.Count - 1; i >= 0; i--)
                    if (changes[i].Time <= time + .000001 && changes[i].Kind == kind && changes[i].Type == type) return changes[i].Value;
                return null;
            }
            IEnumerable<string> Priority(string kind)
            {
                if (family == "blockarea") { yield return "blockarea"; yield break; }
                if (note.Mine)
                {
                    yield return "mine";
                    if (kind == "color") yield break;
                }
                if (note.Break) yield return "break";
                if (note.Each) yield return "each";
                yield return family;
                if (kind == "color" && family == "star") yield return "tap";
                if (kind != "size" || family != "slide") yield return "";
            }
            var result = new VisualValue();
            foreach (var type in Priority("color")) { var v = Lookup("color", type); if (v?.Color != null) { result.Color = v.Color; break; } }
            foreach (var type in Priority("size")) { var v = Lookup("size", type); if (v?.X != null) { result.X = v.X; result.Y = v.Y; break; } }
            foreach (var type in Priority("alpha")) { var v = Lookup("alpha", type); if (v?.Alpha != null) { result.Alpha = v.Alpha; break; } }
            return result;
        }
    }
}
