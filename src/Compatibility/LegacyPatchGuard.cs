using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SinmaiAlpha.Compatibility;

/// <summary>
/// Keeps the host mod (MaiMagic, or AquaMai under its original names) from also patching the chart features
/// Sinmai-Alpha ships its own forks of. It prefixes <c>&lt;root&gt;.Core.Startup.ApplyPatch(Type)</c>, which the
/// host calls once per patch class (nested classes included), and skips a host type only when Sinmai-Alpha
/// actually applied the replacement (<see cref="MarkApplied"/>, read when the host calls ApplyPatch).
/// <para>
/// ExtendNotesPool is deliberately not guarded: the host's global pool extension stays the owner, and with both
/// mods active the pools end at native + max(counts), not their sum.
/// </para>
/// </summary>
internal static class LegacyPatchGuard
{
    private static readonly string[] Roots = new string[2] { "MaiMagic", "AquaMai" };

    // Relative to <root>.Mods.Fancy.GamePlay. Replaced by Sinmai-Alpha's ChartFeatures group.
    private static readonly string[] ChartFeatureTypes = new string[3] { "CustomNoteTypes.CustomNoteTypes", "ReviveFinaleVSlide", "TapInHoldFix" };

    // Replaced by Sinmai-Alpha's ExtraDifficulty (MaiMagic has no such type; AquaMai builds may).
    private static readonly string[] ExtraDifficultyTypes = new string[1] { "ExtraDifficulty.ExtraDifficulty" };

        private static HarmonyLib.Harmony harmony;

    private static readonly HashSet<Assembly> Seen = new HashSet<Assembly>();

    private static volatile bool chartFeaturesApplied;

    private static volatile bool extraDifficultyApplied;

    /// <summary>Called by Plugin once all its patch groups applied, with the groups it applied.</summary>
    internal static void MarkApplied(bool chartFeatures, bool extraDifficulty)
    {
        chartFeaturesApplied = chartFeatures;
        extraDifficultyApplied = extraDifficulty;
    }

    internal static bool IsLegacyFeature(Type type)
    {
        if (Match(type, ChartFeatureTypes) == null)
        {
            return Match(type, ExtraDifficultyTypes) != null;
        }
        return true;
    }

    // The matched feature name if type is <root>.Mods.Fancy.GamePlay.<feature> or one of its nested types.
    private static string Match(Type type, string[] features)
    {
        string text = type?.FullName;
        if (text == null)
        {
            return null;
        }
        foreach (string root in Roots)
        {
            string prefix = root + ".Mods.Fancy.GamePlay.";
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            foreach (string feature in features)
            {
                string full = prefix + feature;
                if (text == full || text.StartsWith(full + "+", StringComparison.Ordinal))
                {
                    return feature;
                }
            }
        }
        return null;
    }

    private static bool BeforeLegacyPatch(Type type)
    {
        try
        {
            string feature = (chartFeaturesApplied ? Match(type, ChartFeatureTypes) : null) ?? (extraDifficultyApplied ? Match(type, ExtraDifficultyTypes) : null);
            if (feature == null)
            {
                return true;
            }
            // The host's own "> Applying" line is inside the skipped method, so say what happened here.
            SafeLog.Msg("[Sinmai-Alpha] Skipping host patch " + type.FullName + ": Sinmai-Alpha applies its own " + feature);
            return false;
        }
        catch (Exception ex)
        {
            SafeLog.Warning("[Sinmai-Alpha] Legacy patch guard failed for " + type?.FullName + ", letting it patch: " + ex.Message);
            return true;
        }
    }

    internal static void Initialize(HarmonyLib.Harmony instance)
    {
        harmony = instance;
        AppDomain.CurrentDomain.AssemblyLoad += (object _, AssemblyLoadEventArgs args) =>
        {
            Inspect(args.LoadedAssembly);
        };
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int num = 0; num < assemblies.Length; num++)
        {
            Inspect(assemblies[num]);
        }
    }

    private static void Inspect(Assembly assembly)
    {
        // Runs from AppDomain.AssemblyLoad (any thread), i.e. inside the host's AppDomain.Load: must never throw.
        try
        {
            string name = assembly.GetName().Name;
            foreach (string root in Roots)
            {
                if (name != root + ".Core")
                {
                    continue;
                }
                lock (Seen)
                {
                    if (!Seen.Add(assembly))
                    {
                        return;
                    }
                }
                MethodInfo methodInfo = assembly.GetType(root + ".Core.Startup", throwOnError: false)?.GetMethod("ApplyPatch", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { typeof(Type) }, null);
                if (methodInfo == null)
                {
                    SafeLog.Warning("[Sinmai-Alpha] " + root + ".Core has no Startup.ApplyPatch(Type); its duplicates of Sinmai-Alpha's features cannot be suppressed");
                    return;
                }
                harmony.Patch(methodInfo, new HarmonyMethod(typeof(LegacyPatchGuard), "BeforeLegacyPatch"));
                SafeLog.Msg("[Sinmai-Alpha] Guarding " + root + ".Core.Startup.ApplyPatch against duplicates of Sinmai-Alpha's features");
                return;
            }
        }
        catch (Exception ex)
        {
            SafeLog.Warning("[Sinmai-Alpha] Cannot guard " + assembly?.FullName + " against duplicate features: " + ex);
        }
    }
}
