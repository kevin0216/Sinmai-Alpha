using SinmaiAlpha.Assets;
using HarmonyLib;
using Monitor;
using UnityEngine;
using UnityEngine.UI;

namespace SinmaiAlpha.Difficulty;

public static partial class ExtraDifficulty
{
    private static bool IsDifficultyHeaderImage(Image image)
    {
        foreach (var controller in image.GetComponentsInParent<DifficultyContoroller>(true))
            if (Field<Image>(controller, "_difficutlyNameImage") == image ||
                Field<Image>(controller, "_background01Image") == image) return true;
        return false;
    }

    [HarmonyPatch(typeof(DifficultyContoroller), "ChangeName")]
    public static class DifficultyHeaderNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(DifficultyContoroller __instance, int ____difficulty, Image ____difficutlyNameImage)
        {
            // With no flag file ThemeFor is null, so this is ApplyImage(image, null) with no lease to restore.
            if (noThemes)
            {
                return;
            }
            DifficultyTheme theme = ThemeFor(Identity(__instance), ____difficulty);
            ApplyImage(____difficutlyNameImage, SpriteFor("UI_CMN_TitleTextImage_" + ThemeCode(theme), theme));
        }
    }

    [HarmonyPatch(typeof(DifficultyContoroller), "ChangeBackground")]
    public static class DifficultyHeaderBackgroundPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DifficultyContoroller __instance, int ____difficulty, Image ____background01Image)
        {
            // ThemeFor is null under the gate (noThemes), so no colour would be applied.
            if (noThemes)
            {
                return;
            }
            DifficultyTheme difficultyTheme = ThemeFor(Identity(__instance), ____difficulty);
            if (difficultyTheme != null && !(____background01Image == null) && ColorUtility.TryParseHtmlString(difficultyTheme.Color, out var color))
            {
                color.a = ____background01Image.color.a;
                ____background01Image.color = color;
            }
        }
    }
}
