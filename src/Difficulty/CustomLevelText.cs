using Manager.MaiStudio;
using SinmaiAlpha.Notes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MAI2.Util;
using Manager;
using MelonLoader;
using SinmaiAlpha.Assets;
using SinmaiAlpha.Hosting;
using UnityEngine;
using UnityEngine.UI;

namespace SinmaiAlpha.Difficulty;

public static partial class ExtraDifficulty
{
    private sealed class LevelLabel { public Texture2D Texture; }
    private static readonly ConditionalWeakTable<SpriteCounter, LevelLabel> LevelLabels = new();
    private static readonly Dictionary<string, ChartFlags> LevelFlags = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LevelTextWarnings = new();

    private static ChartFlags CustomLevelFlags(MusicIdentity identity, int difficulty)
    {
        // Both marker names opened below live in the chart folder the boot scan found without any
        // *ExtraDifficulty.flag (same ids, difficulties and folder transform), so the result would be null.
        if (noThemes)
        {
            return null;
        }
        if (identity == null || identity.Utage || identity.Id <= 0 || identity.Id >= 100000 || difficulty < 0 || difficulty > 4)
        {
            return null;
        }
        MusicData music = Singleton<DataManager>.Instance.GetMusic(identity.Id);
        if (music?.notesData == null || difficulty >= music.notesData.Count)
        {
            return null;
        }
        string text = music.notesData[difficulty]?.file?.path;
        if (string.IsNullOrEmpty(text) || ChartFeatureGate.IsA000(text))
        {
            return null;
        }
        string path = FileSystem.ResolvePath(text);
        string text2 = System.IO.Path.ChangeExtension(path, ".ExtraDifficulty.flag");
        if (!File.Exists(text2) && difficulty == 3)
        {
            text2 = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), "ExtraDifficulty.flag");
        }
        return ReadLevelFlags(text2);
    }
    private static ChartFlags ReadLevelFlags(string marker)
    {
        if (LevelFlags.TryGetValue(marker, out var flags)) return flags;
        try { flags = File.Exists(marker) ? ChartFlags.Parse(File.ReadAllText(marker)) : null; }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        { MelonLogger.Warning("[CustomLevelText] " + e.Message); }
        LevelFlags[marker] = flags; return flags;
    }
    private static void BindLevelText(object owner, int difficulty, bool utage, DifficultyTheme theme)
    {
        // Without a flag file CustomLevelFlags is null for every chart and LevelLabels never got an entry, so the
        // loop below would only do failing Removes (plus an Identity walk, a path resolve and a stat per call).
        if (noThemes)
        {
            return;
        }
        ChartFlags chartFlags = ((!utage) ? CustomLevelFlags(Identity(owner as Component), difficulty) : null);
        Texture2D texture2D = null;
        if (chartFlags != null && chartFlags.CustomLevelText && !string.IsNullOrWhiteSpace(chartFlags.LevelText))
        {
            try
            {
                Color color = NativeLevelColor(difficulty);
                if (theme != null)
                {
                    ColorUtility.TryParseHtmlString(theme.Color, out color);
                }
                texture2D = LevelTextStyle.Get(chartFlags.LevelText, color);
                if (texture2D == null && LevelTextWarnings.Add(chartFlags.LevelText))
                {
                    MelonLogger.Warning("[CustomLevelText] Native Rodin font or rendered glyphs unavailable; keeping native level: " + chartFlags.LevelText);
                }
            }
            catch (Exception ex)
            {
                if (LevelTextWarnings.Add(chartFlags.LevelText))
                {
                    MelonLogger.Warning("[CustomLevelText] Keeping native level: " + ex.Message);
                }
            }
        }
        string[] array = new string[6] { "_digitLevel", "_doubleDigitLevel", "_difficultySingle", "_difficultyDouble", "_singleLevel", "_doubleLevel" };
        foreach (string name in array)
        {
            SpriteCounter spriteCounter = Field<SpriteCounter>(owner, name);
            if (!(spriteCounter == null))
            {
                SetLevelLabel(spriteCounter, texture2D);
            }
        }
    }
    private static Color NativeLevelColor(int difficulty)
    {
        var colors = new[] { "#70D43B", "#F7B708", "#FF828C", "#9F51DC", "#DFCCF1" };
        ColorUtility.TryParseHtmlString(colors[Mathf.Clamp(difficulty, 0, 4)], out var color); return color;
    }
    internal static HashSet<Texture2D> LevelTexturesInUse()
    {
        var textures = new HashSet<Texture2D>();
        foreach (var counter in Resources.FindObjectsOfTypeAll<SpriteCounter>())
            if (LevelLabels.TryGetValue(counter, out var label) && label.Texture != null) textures.Add(label.Texture);
        return textures;
    }
    private static void SetLevelLabel(SpriteCounter counter, Texture2D texture)
    {
        if (texture == null)
        {
            if (LevelLabels.Remove(counter)) counter.SetAllDirty();
            return;
        }
        var label = LevelLabels.GetValue(counter, _ => new LevelLabel());
        if (label.Texture == texture) return;
        label.Texture = texture; counter.SetAllDirty();
    }

    [HarmonyPatch(typeof(SpriteCounter), "mainTexture", MethodType.Getter)]
    public static class CustomLevelTexturePatch
    {
        public static void Postfix(SpriteCounter __instance, ref Texture __result)
        {
            // LevelLabels only gets entries from flag files (BindLevelText), so it is empty while noThemes holds.
            if (noThemes)
            {
                return;
            }
            if (LevelLabels.TryGetValue(__instance, out var value) && value.Texture != null)
            {
                __result = value.Texture;
            }
        }
    }
    [HarmonyPatch(typeof(SpriteCounter), "OnPopulateMesh")]
    public static class CustomLevelMeshPatch
    {
        public static void Postfix(SpriteCounter __instance, VertexHelper vh)
        {
            // LevelLabels is empty under the gate (noThemes): only flag files (BindLevelText) fill it.
            if (noThemes || !LevelLabels.TryGetValue(__instance, out var value) || value.Texture == null || vh.currentVertCount == 0)
            {
                return;
            }
            Vector2 vector = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 vector2 = new Vector2(float.MinValue, float.MinValue);
            UIVertex vertex = default;
            Color color = Color.white;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                vector = Vector2.Min(vector, vertex.position);
                vector2 = Vector2.Max(vector2, vertex.position);
                if (i == 0)
                {
                    color = vertex.color;
                }
            }
            float num = Mathf.Min((vector2.x - vector.x) / (float)value.Texture.width, (vector2.y - vector.y) / (float)value.Texture.height) * LevelTextStyle.DisplayScale;
            if (!(num <= 0f))
            {
                Vector2 vector3 = (vector + vector2) * 0.5f;
                vector3.x += (vector2.x - vector.x) * LevelTextStyle.DisplayOffsetX;
                vector3.y += (vector2.y - vector.y) * LevelTextStyle.DisplayOffsetY;
                Vector2 vector4 = new Vector2(value.Texture.width, value.Texture.height) * (0.5f * num);
                vh.Clear();
                vh.AddVert(new Vector3(vector3.x - vector4.x, vector3.y - vector4.y), color, new Vector2(0f, 0f));
                vh.AddVert(new Vector3(vector3.x - vector4.x, vector3.y + vector4.y), color, new Vector2(0f, 1f));
                vh.AddVert(new Vector3(vector3.x + vector4.x, vector3.y + vector4.y), color, new Vector2(1f, 1f));
                vh.AddVert(new Vector3(vector3.x + vector4.x, vector3.y - vector4.y), color, new Vector2(1f, 0f));
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(2, 3, 0);
            }
        }
    }
    // Unity's legacy OS Font exposes no embedded font bytes to TextCore.
    // Redirect only our SimHei source to its installed file, keeping TMP's
    // normal atlas packing and every other font's loader unchanged.
    [HarmonyPatch]
    public static class CustomLevelHeiFontPatch
    {
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var method in typeof(TMPro.TMP_FontAsset).GetMethods(System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance))
                if (method.DeclaringType == typeof(TMPro.TMP_FontAsset) && method.GetMethodBody() != null &&
                    (method.Name == "CreateFontAsset" || method.Name.StartsWith("TryAddCharacter", StringComparison.Ordinal))) yield return method;
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.operand is System.Reflection.MethodInfo method &&
                    method.DeclaringType == typeof(UnityEngine.TextCore.LowLevel.FontEngine) && method.Name == "LoadFontFace")
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(Font))
                        instruction.operand = AccessTools.Method(typeof(LevelTextStyle), "LoadFontFace",
                            parameters.Length == 1 ? new[] { typeof(Font) } : new[] { typeof(Font), typeof(int) });
                }
                yield return instruction;
            }
        }
    }

}
