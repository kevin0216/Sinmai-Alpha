using System;
using System.Numerics;

namespace SinmaiAlpha.Notes.Libs;

internal static class SssJudgePathGeometry
{
    // Match SlideDataBuilder's hit-region centers. C1 and C2 are one logical center.
    internal static Vector2 Center(int area)
    {
        if (area < 0 || area >= 34 || area == 16 || area == 17) return Vector2.Zero;
        var first = area < 8 ? 0 : area < 16 ? 8 : area < 26 ? 18 : 26;
        var radius = first == 0 ? 440 : first == 8 ? 210 : first == 18 ? MaiGeometry.DRadius : MaiGeometry.ERadius;
        var angle = Math.PI * ((first < 16 ? 3.0 / 8 : .5) - (area - first) / 4.0);
        return new Vector2((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
    }

    // Match InputManager.ConvertTouchPanelRotatePush on 1.66 and 1.70.
    internal static int PhysicalArea(int area, int button)
    {
        if (area < 0 || area >= 34 || area == 16 || area == 17) return area;
        var first = area < 8 ? 0 : area < 16 ? 8 : area < 26 ? 18 : 26;
        return first + ((area - first + button) % 8 + 8) % 8;
    }
}
