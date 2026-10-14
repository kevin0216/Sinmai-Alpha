using SinmaiAlpha.Hosting;

namespace SinmaiAlpha.Compatibility;

internal static class SinglePlayer
{
    // config.json "HideSubMonitor": true wins; otherwise follow the host mod's SinglePlayer.HideSubMonitor.
    internal static bool HideSubMonitor => Settings.Current.HideSubMonitor || OptionalDisplay.HideSubMonitor;
}
