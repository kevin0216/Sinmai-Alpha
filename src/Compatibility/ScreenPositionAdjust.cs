using UnityEngine;
using UnityEngine.UI;

namespace SinmaiAlpha.Compatibility;

internal static class ScreenPositionAdjust
{
    // The host's private camera that renders the game when its ScreenPositionAdjust is on; null otherwise.
    internal static Camera GameRenderCamera => OptionalDisplay.GameRenderCamera;

    internal static RawImage DisplayImage(int monitor, bool sub)
    {
        return OptionalDisplay.DisplayImage(monitor, sub);
    }
}
