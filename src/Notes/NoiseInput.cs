using HarmonyLib;
using Manager;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    internal static IEnumerable<MethodInfo> NoiseInputTargets()
    {
        return from m in typeof(InputManager).GetMethods(BindingFlags.Static | BindingFlags.Public)
               where m.ReturnType == typeof(bool) && (m.Name.StartsWith("InGameTouchPanelArea", StringComparison.Ordinal) || m.Name.StartsWith("GetTouchPanelArea", StringComparison.Ordinal) || m.Name == "InGameButtonDown" || m.Name == "InGameButtonPush" || m.Name == "GetButtonDown" || m.Name == "GetButtonPush" || m.Name == "GetButtonLongPush") && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(int) && ((m.GetParameters().Length >= 2 && (m.GetParameters()[1].ParameterType == typeof(InputManager.TouchPanelArea) || m.GetParameters()[1].ParameterType == typeof(InputManager.ButtonSetting) || m.GetParameters()[1].ParameterType == typeof(InputManager.TouchPanelArea[]))) || (m.GetParameters().Length == 1 && m.Name.Contains("_C_")))
               select m;
    }

    private static System.Type NoiseArg1(MethodInfo m)
    {
        ParameterInfo[] parameters = m.GetParameters();
        if (parameters.Length < 2)
        {
            return null;
        }
        return parameters[1].ParameterType;
    }

    private static int NoiseButtonOffset(string name)
    {
        if (!name.Contains("_B_"))
        {
            if (!name.Contains("_D_"))
            {
                if (!name.Contains("_E_"))
                {
                    return 0;
                }
                return 26;
            }
            return 18;
        }
        return 8;
    }

    private static IEnumerable<MethodBase> NoiseButtonTargets(int offset)
    {
        return from m in NoiseInputTargets()
               where NoiseArg1(m) == typeof(InputManager.ButtonSetting) && NoiseButtonOffset(m.Name) == offset
               select m;
    }

    private static bool NoisePrepare(MethodBase original, IEnumerable<MethodBase> targets)
    {
        if (!(original != null))
        {
            return targets.Any();
        }
        return true;
    }

    private static void NoiseButtonPostfix(int monitor, InputManager.ButtonSetting button, int offset, ref bool result)
    {
        int num = (int)button;
        if (result && num >= 0 && num <= 7 && FeaturesForMonitor(monitor) && IsNoiseAreaBlocked(monitor, num + offset))
        {
            result = false;
        }
    }

    [HarmonyPatch]
    public static class NoiseInputAreaPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return from m in NoiseInputTargets()
                   where NoiseArg1(m) == typeof(InputManager.TouchPanelArea)
                   select m;
        }

        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            return NoisePrepare(original, TargetMethods());
        }

        [HarmonyPostfix]
        public static void Postfix(int __0, InputManager.TouchPanelArea __1, ref bool __result)
        {
            if (__result && FeaturesForMonitor(__0) && IsNoiseAreaBlocked(__0, (int)__1))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    public static class NoiseInputButtonPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return NoiseButtonTargets(0);
        }

        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            return NoisePrepare(original, TargetMethods());
        }

        [HarmonyPostfix]
        public static void Postfix(int __0, InputManager.ButtonSetting __1, ref bool __result)
        {
            NoiseButtonPostfix(__0, __1, 0, ref __result);
        }
    }

    [HarmonyPatch]
    public static class NoiseInputButtonBPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return NoiseButtonTargets(8);
        }

        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            return NoisePrepare(original, TargetMethods());
        }

        [HarmonyPostfix]
        public static void Postfix(int __0, InputManager.ButtonSetting __1, ref bool __result)
        {
            NoiseButtonPostfix(__0, __1, 8, ref __result);
        }
    }

    [HarmonyPatch]
    public static class NoiseInputButtonDPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return NoiseButtonTargets(18);
        }

        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            return NoisePrepare(original, TargetMethods());
        }

        [HarmonyPostfix]
        public static void Postfix(int __0, InputManager.ButtonSetting __1, ref bool __result)
        {
            NoiseButtonPostfix(__0, __1, 18, ref __result);
        }
    }

    [HarmonyPatch]
    public static class NoiseInputButtonEPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return NoiseButtonTargets(26);
        }

        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            return NoisePrepare(original, TargetMethods());
        }

        [HarmonyPostfix]
        public static void Postfix(int __0, InputManager.ButtonSetting __1, ref bool __result)
        {
            NoiseButtonPostfix(__0, __1, 26, ref __result);
        }
    }

    [HarmonyPatch]
    public static class NoiseInputAreaListPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return from m in NoiseInputTargets()
                   where NoiseArg1(m) == typeof(InputManager.TouchPanelArea[])
                   select m;
        }

        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            return NoisePrepare(original, TargetMethods());
        }

        [HarmonyPostfix]
        public static void Postfix(int __0, InputManager.TouchPanelArea[] __1, ref bool __result)
        {
            if (!__result || __1 == null || !FeaturesForMonitor(__0))
            {
                return;
            }
            bool flag = false;
            for (int i = 0; i < __1.Length; i++)
            {
                if (IsNoiseAreaBlocked(__0, (int)__1[i]))
                {
                    flag = true;
                    break;
                }
            }
            if (!flag)
            {
                return;
            }
            bool result = false;
            for (int j = 0; j < __1.Length; j++)
            {
                InputManager.TouchPanelArea touchPanelArea = __1[j];
                if (!IsNoiseAreaBlocked(__0, (int)touchPanelArea) && InputManager.GetTouchPanelAreaDown(__0, touchPanelArea))
                {
                    result = true;
                    break;
                }
            }
            __result = result;
        }
    }

    [HarmonyPatch]
    public static class NoiseInputCenterPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return from m in NoiseInputTargets()
                   where m.GetParameters().Length == 1
                   select m;
        }

        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            return NoisePrepare(original, TargetMethods());
        }

        [HarmonyPostfix]
        public static void Postfix(int __0, ref bool __result)
        {
            if (__result && FeaturesForMonitor(__0) && IsNoiseAreaBlocked(__0, 16))
            {
                __result = false;
            }
        }
    }
}
