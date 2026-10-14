using System;
using MelonLoader;

namespace SinmaiAlpha.Compatibility;

/// <summary>
/// Logging for code that must never throw: host-mod accessors read inside game frames, the AssemblyLoad and
/// Startup.ApplyPatch hooks that run inside the host's loader, and the mine-sound flush in LateUpdate.
/// </summary>
internal static class SafeLog
{
    internal static void Msg(string message)
    {
        try
        {
            MelonLogger.Msg(message);
        }
        catch (Exception)
        {
        }
    }

    internal static void Warning(string message)
    {
        try
        {
            MelonLogger.Warning(message);
        }
        catch (Exception)
        {
        }
    }
}
