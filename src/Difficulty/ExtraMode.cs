using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Monitor;
using Monitor.MusicSelect.ChainList;
using SinmaiAlpha.Hosting;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Difficulty;

// Cosmetic only: never changes ScoreKind, music IDs or gameplay feature gates.
public static class ExtraMode
{
    internal const string Marker = "// SINMAI_ALPHA_MODE\t";
    private sealed class Stamp { public long Length, Ticks; public int CheckedAt; public string Mode; }
    private sealed class Lease
    {
        public Sprite Original, Applied;

        public Image Owner;
    }
    private static readonly Dictionary<string, Stamp> Charts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, Lease> Images = new Dictionary<int, Lease>();
    private static readonly string[] Screens = { "MSS", "TST", "TTR", "UPE" };
    [ThreadStatic] private static bool applying;

    public static void OnBeforePatch(HarmonyLib.Harmony harmony)
    {
        // Mode ownership must also work with custom difficulty skins disabled.
        if (!Settings.Current.ExtraDifficulty)
            foreach (var type in new[] { typeof(ExtraDifficulty.CardDataPatch), typeof(ExtraDifficulty.CentralCardPatch),
                typeof(ExtraDifficulty.MenuPatch), typeof(ExtraDifficulty.InformationDataPatch), typeof(ExtraDifficulty.ResultTrackDataPatch),
                typeof(ExtraDifficulty.SummaryDataPatch) })
                harmony.PatchAll(type);
    }

    internal static string ReadMode(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        try
        {
            path = Resolve(path);
            int tickCount = Environment.TickCount;
            Charts.TryGetValue(path, out var value);
            if (value != null && tickCount - value.CheckedAt >= 0 && tickCount - value.CheckedAt < 1000)
            {
                return value.Mode;
            }
            FileInfo fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                Charts[path] = new Stamp
                {
                    Length = -1L,
                    CheckedAt = tickCount
                };
                return null;
            }
            if (value != null && value.Length == fileInfo.Length && value.Ticks == fileInfo.LastWriteTimeUtc.Ticks)
            {
                value.CheckedAt = tickCount;
                return value.Mode;
            }
            string text = null;
            using (StreamReader streamReader = File.OpenText(path))
            {
                for (int i = 0; i < 128; i++)
                {
                    string text2 = streamReader.ReadLine();
                    if (text2 == null)
                    {
                        break;
                    }
                    if (text2.StartsWith("// SINMAI_ALPHA_MODE\t", StringComparison.Ordinal))
                    {
                        string text3 = text2.Substring("// SINMAI_ALPHA_MODE\t".Length).Trim();
                        if (text3.Length > 0 && text3.Length <= 40 && Array.TrueForAll(text3.ToCharArray(), (char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                        {
                            text = text3;
                        }
                        break;
                    }
                }
            }
            Charts[path] = new Stamp
            {
                Length = fileInfo.Length,
                Ticks = fileInfo.LastWriteTimeUtc.Ticks,
                Mode = text,
                CheckedAt = tickCount
            };
            return text;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        {
            return null;
        }
    }

    private static string IconKey(string name)
    {
        // 1.70 uses dedicated RSL badges for both results and KOP track history.
        if (name == "UI_RSL_MBase_Parts_01" || name == "UI_RSL_MBase_Parts_02") return "UI_TTR_Infoicon_";
        if (name == null || !name.StartsWith("UI_", StringComparison.Ordinal) || name.IndexOf("_Infoicon_", StringComparison.Ordinal) < 0) return null;
        foreach (var screen in Screens)
        {
            var prefix = "UI_" + screen + "_Infoicon_";
            if (name == prefix + "StandardMode" || name == prefix + "DeluxeMode") return prefix;
        }
        return null;
    }

    private static Sprite GetSprite(string mode, string key, Sprite native)
    {
        var cacheKey = mode + "/" + key + "/" + native.GetInstanceID();
        if (Sprites.TryGetValue(cacheKey, out var cached)) return cached;
        var path = Path.Combine(AssetFiles.Root, "ExtraMode", mode, key + mode + "Mode.png");
        if (native.name.StartsWith("UI_RSL_MBase_Parts_", StringComparison.Ordinal))
        {
            var resultPath = Path.Combine(AssetFiles.Root, "ExtraMode", mode, "UI_RSL_MBase_Parts_" + mode + ".png");
            if (File.Exists(resultPath)) path = resultPath;
        }
        Sprite sprite = null;
        Texture2D texture = null;
        try
        {
            if (File.Exists(path))
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(File.ReadAllBytes(path)))
                {
                    texture = RestoreCanvas(texture, native);
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(native.pivot.x / native.rect.width, native.pivot.y / native.rect.height),
                        native.pixelsPerUnit, 0, SpriteMeshType.FullRect, native.border);
                    sprite.name = key + mode + "Mode";
                }
                else Object.Destroy(texture);
            }
        }
        catch (Exception error)
        { if (texture != null) Object.Destroy(texture); MelonLogger.Warning("[ExtraMode] " + path + ": " + error.Message); }
        return Sprites[cacheKey] = sprite;
    }

    // Exported PNGs contain the trimmed pixels, whereas native UI sprites
    // retain the original canvas (e.g. MSS is 116x44 with a 116x32 crop).
    // Recreate that transparent canvas instead of stretching the crop to fill it.
    private static Texture2D RestoreCanvas(Texture2D source, Sprite native)
    {
        var width = Mathf.RoundToInt(native.rect.width);
        var height = Mathf.RoundToInt(native.rect.height);
        if (source.width == width && source.height == height) return source;
        var offset = native.textureRectOffset;
        var crop = native.textureRect;
        var x = Mathf.RoundToInt(offset.x); var y = Mathf.RoundToInt(offset.y);
        var cropWidth = Mathf.Min(width - x, Mathf.RoundToInt(crop.width));
        var cropHeight = Mathf.Min(height - y, Mathf.RoundToInt(crop.height));
        if (native.name.StartsWith("UI_RSL_MBase_Parts_", StringComparison.Ordinal))
        {
            // A dedicated RSL asset is optional. Fit the supplied TTR badge in
            // the smaller result box without squeezing its lettering.
            var scale = Mathf.Min((float)cropWidth / source.width, (float)cropHeight / source.height);
            var fitWidth = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            var fitHeight = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            x += (cropWidth - fitWidth) / 2; y += (cropHeight - fitHeight) / 2;
            cropWidth = fitWidth; cropHeight = fitHeight;
        }
        var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
        output.SetPixels32(new Color32[width * height]);
        if (source.width == cropWidth && source.height == cropHeight)
            output.SetPixels(x, y, cropWidth, cropHeight, source.GetPixels());
        else
        {
            // Higher resolution replacements keep the same native placement.
            var pixels = new Color[cropWidth * cropHeight];
            for (var row = 0; row < cropHeight; row++)
                for (var col = 0; col < cropWidth; col++)
                    pixels[row * cropWidth + col] = source.GetPixelBilinear((col + .5f) / cropWidth, (row + .5f) / cropHeight);
            output.SetPixels(x, y, cropWidth, cropHeight, pixels);
        }
        output.Apply(false, true);
        Object.Destroy(source);
        return output;
    }

    internal static void Apply(Image image)
    {
        if (applying || image == null)
        {
            return;
        }
        int instanceID = image.GetInstanceID();
        Images.TryGetValue(instanceID, out var value);
        Sprite sprite = ((value != null && image.sprite == value.Applied) ? value.Original : image.sprite);
        // IconKey(sprite?.name) and sprite.name from the per-sprite cache (reference null test as before).
        NameKey nameKey = (((object)sprite != null) ? KeyOf(sprite) : null);
        string text = nameKey?.Key;
        if (text == null)
        {
            if (value != null)
            {
                value.Applied = null;
                value.Original = null;
            }
            return;
        }
        string text2 = ReadMode(ExtraDifficulty.ModeChart(image, nameKey.Name));
        Sprite sprite2 = ((text2 == null) ? null : GetSprite(text2, text, sprite));
        if (value == null)
        {
            value = NewLease(image, instanceID);
        }
        value.Original = sprite;
        value.Applied = sprite2;
        Sprite sprite3 = sprite2 ?? sprite;
        if (image.sprite == sprite3)
        {
            return;
        }
        applying = true;
        try
        {
            image.sprite = sprite3;
        }
        finally
        {
            applying = false;
        }
    }

    private static void Refresh(Component owner)
    {
        if (owner == null)
        {
            return;
        }
        // The same search as GetComponentsInChildren<Image>(true), into a pooled list instead of a new array per
        // card bind / TrackStart / Result. A stack, not one shared list: Apply assigns sprites, which re-enters the
        // setter postfix, and another patch on that path could reach Refresh again.
        List<Image> list = ((RefreshBuffers.Count != 0) ? RefreshBuffers.Pop() : new List<Image>());
        try
        {
            owner.GetComponentsInChildren(includeInactive: true, list);
            for (int i = 0; i < list.Count; i++)
            {
                Apply(list[i]);
            }
        }
        finally
        {
            list.Clear();
            RefreshBuffers.Push(list);
        }
    }

    [HarmonyPatch(typeof(Image), "sprite", MethodType.Setter)]
    public static class SpritePatch
    {
        [HarmonyPostfix] public static void Postfix(Image __instance) => Apply(__instance);
    }

    [HarmonyPatch(typeof(Graphic), "OnDidApplyAnimationProperties")]
    public static class AnimationPatch
    {
        [HarmonyPostfix] public static void Postfix(Graphic __instance) { if (__instance is Image image) Apply(image); }
    }

    [HarmonyPatch]
    public static class RefreshPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MusicChainCardObejct), "SetMusicData");
            yield return AccessTools.Method(typeof(MusicChainCardObejct), "SetScoreKind");
            yield return AccessTools.Method(typeof(MusicChainCardObejct), "SetDifficulty");
            yield return AccessTools.Method(typeof(MusicInfomationController), "SetMusicData");
            yield return AccessTools.Method(typeof(MusicInfomationController), "SetScoreKind");
            yield return AccessTools.Method(typeof(KOP_ResultTrackData), "SetMusicData");
            yield return AccessTools.Method(typeof(TrackStartMonitor), "SetTrackStart");
            yield return AccessTools.Method(typeof(ResultMonitor), "SetGameScoreType");
        }
        [HarmonyPostfix, HarmonyPriority(Priority.Last)] public static void Postfix(Component __instance) => Refresh(__instance);
    }


    private sealed class NameKey
    {
        public string Name;

        public string Key;
    }

    private static readonly List<int> DeadLeases = new List<int>();

    private static int leasePruneAt = 256;

    private static readonly Dictionary<int, NameKey> SpriteKeys = new Dictionary<int, NameKey>();

    private static readonly Dictionary<string, string> Resolved = new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly Stack<List<Image>> RefreshBuffers = new Stack<List<Image>>();

    private static string Resolve(string path)
    {
        if (Resolved.TryGetValue(path, out var value))
        {
            return value;
        }
        value = FileSystem.ResolvePath(path);
        if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/') && char.IsLetter(path[0]) && path.IndexOf('%') < 0)
        {
            if (Resolved.Count >= 4096)
            {
                Resolved.Clear();
            }
            Resolved[path] = value;
        }
        return value;
    }

    private static NameKey KeyOf(Sprite sprite)
    {
        int instanceID = sprite.GetInstanceID();
        if (instanceID != 0 && SpriteKeys.TryGetValue(instanceID, out var value))
        {
            return value;
        }
        string name = sprite.name;
        value = new NameKey
        {
            Name = name,
            Key = IconKey(name)
        };
        if (instanceID != 0)
        {
            if (SpriteKeys.Count >= 8192)
            {
                SpriteKeys.Clear();
            }
            SpriteKeys[instanceID] = value;
        }
        return value;
    }

    private static Lease NewLease(Image image, int instanceID)
    {
        if (Images.TryGetValue(instanceID, out var value))
        {
            return value;
        }
        if (Images.Count >= leasePruneAt)
        {
            foreach (KeyValuePair<int, Lease> item in Images)
            {
                if (item.Value.Owner == null)
                {
                    DeadLeases.Add(item.Key);
                }
            }
            for (int i = 0; i < DeadLeases.Count; i++)
            {
                Images.Remove(DeadLeases[i]);
            }
            DeadLeases.Clear();
            leasePruneAt = Math.Max(256, 2 * Images.Count);
        }
        value = new Lease
        {
            Owner = image
        };
        Images[instanceID] = value;
        return value;
    }
}
