using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SinmaiAlpha.Difficulty;
using UnityEngine.UI;

namespace SinmaiAlpha.Compatibility;

// Older MaiMagic builds replace these icons without consulting Alpha's ExtraMode.
// Guard the host's writes per image so mixed STD/DX cards and reused UI retain the
// host icon whenever the chart behind that particular image has no Alpha mode.
internal static class CustomScoreKindCompatibility
{
    private static readonly HashSet<Assembly> Seen = new();
    private static HarmonyLib.Harmony harmony;

    internal static void Initialize(HarmonyLib.Harmony instance)
    {
        harmony = instance;
        AppDomain.CurrentDomain.AssemblyLoad += (_, args) => Inspect(args.LoadedAssembly);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) Inspect(assembly);
    }

    private static void Inspect(Assembly assembly)
    {
        try
        {
            if (assembly.GetName().Name != "MaiMagic.Mods") return;
            lock (Seen) if (!Seen.Add(assembly)) return;
            var utility = assembly.GetType("MaiMagic.Mods.UX.CustomScoreKindIcon+ScoreKindPatchUtil", false);
            if (utility == null) return;

            var count = 0;
            foreach (var method in utility.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.GetMethodBody() == null) continue;
                if (PatchProcessor.GetOriginalInstructions(method).Any(IsOverrideSetter))
                {
                    harmony.Patch(method, transpiler: new HarmonyMethod(typeof(CustomScoreKindCompatibility), nameof(OverrideWrites)));
                    count++;
                }
                if (method.Name == "ReplaceMultipleImageSprites" && method.GetParameters().Length > 0 &&
                    typeof(Image).IsAssignableFrom(method.GetParameters()[0].ParameterType))
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(CustomScoreKindCompatibility), nameof(BeforeReplaceSprites)));
                    count++;
                }
            }
            SafeLog.Msg($"[Sinmai-Alpha] Guarding {count} MaiMagic score-kind icon writers for Alpha chart modes");
        }
        catch (Exception error)
        {
            SafeLog.Warning("[Sinmai-Alpha] Cannot guard MaiMagic score-kind icons: " + error);
        }
    }

    private static bool IsOverrideSetter(CodeInstruction instruction) =>
        (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
        instruction.operand is MethodInfo method && method.DeclaringType == typeof(Image) && method.Name == "set_overrideSprite";

    private static IEnumerable<CodeInstruction> OverrideWrites(IEnumerable<CodeInstruction> instructions)
    {
        var replacement = AccessTools.Method(typeof(ExtraMode), nameof(ExtraMode.SetHostScoreKindOverride));
        foreach (var instruction in instructions)
        {
            if (IsOverrideSetter(instruction))
            {
                // Both calls consume an Image and a Sprite. Keep labels/exception blocks.
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            yield return instruction;
        }
    }

    // MaiMagic's caller restores the native MultiSprites before this replacement.
    // Keep that restoration and let ExtraMode select the chart's own InfoIcon.
    private static bool BeforeReplaceSprites(Image __0) => !ExtraMode.OwnsScoreKindIcon(__0);
}
