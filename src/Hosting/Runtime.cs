using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SinmaiAlpha.Plugin), "Sinmai-Alpha", "0.1.6", "SaltNya")]
[assembly: MelonGame("sega-interactive", "Sinmai")]
[assembly: MelonPriority(-10000)]
[assembly: HarmonyDontPatchAll]

namespace SinmaiAlpha
{
    public sealed class Plugin : MelonMod
    {
        public override void OnInitializeMelon()
        {
            Hosting.Settings.Load();
            Compatibility.LegacyPatchGuard.Initialize(HarmonyInstance);
            var settings = Hosting.Settings.Current;
            Notes.CustomNoteTypes.ShowMineHitFeedback = settings.ShowMineHitFeedback;
            Notes.CustomNoteTypes.MineVolume = Mathf.Clamp01(settings.MineVolume);
            Notes.ExtendNotesPool.count = Math.Max(0, Math.Min(512, settings.ExtraPoolCount));
            var roots = new List<Type> { typeof(Difficulty.AlphaRights), typeof(Difficulty.ExtraMode) };
            if (settings.ChartFeatures) roots.AddRange(new[] { typeof(Notes.CustomNoteTypes), typeof(Notes.ExtendNotesPool), typeof(Notes.ReviveFinaleVSlide), typeof(Notes.TapInHoldFix) });
            if (settings.ExtraDifficulty) roots.Add(typeof(Difficulty.ExtraDifficulty));
            if (Hosting.GameplayDiagnostics.Enabled) { Hosting.GameplayDiagnostics.Start(); roots.Add(typeof(Hosting.GameplayDiagnostics)); }
            var count = 0;
            try
            {
                foreach (var root in roots) Apply(root, ref count);
                Compatibility.LegacyPatchGuard.MarkApplied(settings.ChartFeatures, settings.ExtraDifficulty);
                MelonLogger.Msg(Notes.ChartWatermark.Credit);
                MelonLogger.Msg("[Sinmai-Alpha] Ready: " + count + " patch groups; assets=" + Hosting.AssetFiles.Root);
            }
            catch (Exception error)
            {
                HarmonyInstance.UnpatchSelf();
                MelonLogger.Error("[Sinmai-Alpha] Initialization failed: " + error);
                throw;
            }
        }
        public override void OnDeinitializeMelon() => Hosting.GameplayDiagnostics.Stop();
        private void Apply(Type type, ref int count)
        {
            Lifecycle(type, "OnBeforePatch");
            HarmonyInstance.PatchAll(type);
            Lifecycle(type, "OnAfterPatch"); count++;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
                if (nested.GetCustomAttributes(false).Length != 0) Apply(nested, ref count);
        }
        private void Lifecycle(Type type, string name)
        {
            var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null) method.Invoke(null, method.GetParameters().Select(p => p.ParameterType == typeof(HarmonyLib.Harmony) ? (object)HarmonyInstance : throw new InvalidOperationException("Unsupported lifecycle argument")).ToArray());
        }
    }
}

namespace SinmaiAlpha.Hosting
{
    public static class FileSystem
    {
        public static string ResolvePath(string path)
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(Environment.CurrentDirectory, expanded));
        }
    }
    public static class AssetFiles
    {
        public static string Root => FileSystem.ResolvePath("Sinmai-Alpha");
        public static Stream Open(string relative)
        {
            var path = Path.GetFullPath(Path.Combine(Root, relative));
            if (!path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Asset path leaves Sinmai-Alpha");
            return File.Exists(path) ? File.OpenRead(path) : null;
        }
    }
    [DataContract]
    public sealed class Settings
    {
        [DataMember] public bool ChartFeatures = true;
        [DataMember] public bool ExtraDifficulty = true;
        [DataMember] public bool ShowMineHitFeedback = true;
        [DataMember] public float MineVolume = .7f;
        [DataMember] public int ExtraPoolCount = 128;
        // null follows an installed display adapter; true/false is an explicit
        // hint for cabinets whose single-screen mod exposes no public setting.
        [DataMember] public bool? SinglePlayer = null;
        [DataMember] public bool HideSubMonitor = false;
        [DataMember] public bool ShowSssJudgePath = false;
        [DataMember] public bool GameplayDiagnostics = false;
        public static Settings Current { get; private set; } = new Settings();
        [OnDeserializing] private void Defaults(StreamingContext context)
        { ChartFeatures = ExtraDifficulty = ShowMineHitFeedback = true; MineVolume = .7f; ExtraPoolCount = 128; ShowSssJudgePath = GameplayDiagnostics = false; }
        public static void Load()
        {
            var path = Path.Combine(AssetFiles.Root, "config.json");
            if (!File.Exists(path)) return;
            try { using var stream = File.OpenRead(path); Current = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream); }
            catch (Exception error) { MelonLogger.Warning("[Sinmai-Alpha] Invalid config, using defaults: " + error.Message); }
        }
    }
    public static class GuiSizes
    {
        public static bool SinglePlayer => Settings.Current.SinglePlayer ?? Compatibility.OptionalDisplay.SinglePlayer;
    }
}

