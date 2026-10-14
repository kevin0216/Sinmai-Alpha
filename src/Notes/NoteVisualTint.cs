using SinmaiAlpha.ChartVisuals;
using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class VisualTexture
    {
        public Color32[] Pixels;
        public int Width, Height;
    }
    private static readonly Dictionary<Sprite, VisualTexture> VisualSourceTextures = new();
    private static readonly Dictionary<VisualTintKey, Sprite> VisualTintSprites = new Dictionary<VisualTintKey, Sprite>();
    private static readonly HashSet<Sprite> VisualTintFailures = new();
    private static readonly Dictionary<Sprite, Sprite> BorrowedGraySprites = new();

    private static Sprite GetBorrowedGraySprite(Sprite source)
    {
        if (BorrowedGraySprites.TryGetValue(source, out var value))
        {
            return value;
        }
        try
        {
            // Session-cached readback and memoised per-pixel gray (see GetVisualSourceTexture / GrayVisualPixels).
            VisualTexture visualTexture = GetVisualSourceTexture(source);
            Color32[] array = VisualPixelBuffer(visualTexture.Pixels.Length);
            GrayVisualPixels(visualTexture.Pixels, array);
            Texture2D texture2D = new Texture2D(visualTexture.Width, visualTexture.Height, TextureFormat.RGBA32, mipChain: false);
            texture2D.name = "AquaMai Borrowed Mine";
            texture2D.wrapMode = TextureWrapMode.Clamp;
            texture2D.SetPixels32(array);
            texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            UnityEngine.Vector2 pivot = new UnityEngine.Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height);
            Sprite sprite = Sprite.Create(texture2D, new Rect(0f, 0f, visualTexture.Width, visualTexture.Height), pivot, source.pixelsPerUnit, 0u, SpriteMeshType.Tight, source.border);
            CopyVisualSpriteGeometry(sprite, source);
            texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            BorrowedGraySprites[source] = sprite;
            return sprite;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("[Borrowed Trajectory] Cannot gray carrier: " + ex.Message);
            return source;
        }
    }

    // Sinmai does not ship NoteColorTint. Bake its RGB operation into a cached
    // sprite instead of depending on an incompatible preview AssetBundle/shader.
    // Opacity, native hold gauge materials and break flashing remain live.
    private static Sprite GetVisualTintSprite(Sprite source, int rgb, Color vertex)
    {
        Color32 color = new Color(vertex.r, vertex.g, vertex.b, 1f);
        VisualTintKey key = new VisualTintKey(source, rgb, color);
        if (VisualTintSprites.TryGetValue(key, out var value))
        {
            return value;
        }
        if (VisualTintFailures.Contains(source))
        {
            return source;
        }
        var bakeStarted = Hosting.GameplayDiagnostics.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        try
        {
            // Session-cached readback and memoised per-pixel recolour (see GetVisualSourceTexture / TintVisualPixels).
            VisualTexture value2 = GetVisualSourceTexture(source);
            Color32[] array = VisualPixelBuffer(value2.Pixels.Length);
            TintVisualPixels(value2.Pixels, array, rgb, color);
            Texture2D texture2D = new Texture2D(value2.Width, value2.Height, TextureFormat.RGBA32, mipChain: false);
            texture2D.name = "AquaMai Note Tint";
            texture2D.filterMode = source.texture.filterMode;
            texture2D.wrapMode = TextureWrapMode.Clamp;
            texture2D.SetPixels32(array);
            texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            UnityEngine.Vector2 pivot = new UnityEngine.Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height);
            Sprite sprite = Sprite.Create(texture2D, new Rect(0f, 0f, value2.Width, value2.Height), pivot, source.pixelsPerUnit, 0u, SpriteMeshType.Tight, source.border);
            CopyVisualSpriteGeometry(sprite, source);
            texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            sprite.name = source.name + " AquaMai " + rgb.ToString("X6");
            VisualTintSprites.Add(key, sprite);
            if (bakeStarted != 0) Hosting.GameplayDiagnostics.TintBake(source.name, rgb, value2.Width, value2.Height, bakeStarted);
            return sprite;
        }
        catch (Exception ex)
        {
            VisualTintFailures.Add(source);
            MelonLogger.Warning("[Note Visual] Cannot recolor " + source.name + ": " + ex.Message);
            return source;
        }
    }
    private static float VisualSmooth(float low, float high, float value)
    {
        var t = Mathf.Clamp01((value - low) / (high - low)); return t * t * (3 - 2 * t);
    }
    private static VisualTexture ReadVisualTexture(Sprite sprite)
    {
        // UV/vertex mapping also handles tight-packed and rotated atlas sprites:
        // textureRect/textureRectOffset throw for tight packing in Unity.
        var vertices = sprite.vertices; var uvs = sprite.uv; var triangles = sprite.triangles;
        var minU = 1f; var minV = 1f; var maxU = 0f; var maxV = 0f;
        foreach (var uv in uvs) { minU = Mathf.Min(minU, uv.x); minV = Mathf.Min(minV, uv.y); maxU = Mathf.Max(maxU, uv.x); maxV = Mathf.Max(maxV, uv.y); }
        var left = Mathf.FloorToInt(minU * sprite.texture.width); var bottom = Mathf.FloorToInt(minV * sprite.texture.height);
        var right = Mathf.CeilToInt(maxU * sprite.texture.width); var top = Mathf.CeilToInt(maxV * sprite.texture.height);
        var rect = new Rect(left, bottom, right - left, top - bottom);
        var width = Mathf.RoundToInt(sprite.rect.width); var height = Mathf.RoundToInt(sprite.rect.height);
        var cropWidth = Mathf.RoundToInt(rect.width); var cropHeight = Mathf.RoundToInt(rect.height);
        var readable = new Texture2D(cropWidth, cropHeight, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        var buffer = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height, 0, RenderTextureFormat.ARGB32);
        Color32[] crop;
        try
        {
            Graphics.Blit(sprite.texture, buffer); RenderTexture.active = buffer;
            // ReadPixels already fills CPU memory; uploading it to the GPU before
            // GetPixels32 only adds a synchronization point and a redundant copy.
            readable.ReadPixels(rect, 0, 0, false); crop = readable.GetPixels32();
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(buffer); Object.Destroy(readable); }
        // Full rectangles need no per-pixel triangle/UV reconstruction. Keep the
        // geometry path for tight/rotated/trimmed atlas sprites.
        if (vertices.Length == 4 && triangles.Length == 6 && width == cropWidth && height == cropHeight)
        {
            var corners = 0;
            for (var i = 0; i < 4; i++)
            {
                var point = vertices[i] * sprite.pixelsPerUnit + sprite.pivot;
                var rightSide = Mathf.Abs(point.x - width) < .01f;
                var topSide = Mathf.Abs(point.y - height) < .01f;
                if ((!rightSide && Mathf.Abs(point.x) >= .01f) || (!topSide && Mathf.Abs(point.y) >= .01f) ||
                    Mathf.Abs(uvs[i].x * sprite.texture.width - left - point.x) >= .01f ||
                    Mathf.Abs(uvs[i].y * sprite.texture.height - bottom - point.y) >= .01f) { corners = 0; break; }
                corners |= 1 << ((rightSide ? 1 : 0) + (topSide ? 2 : 0));
            }
            if (corners == 15)
            {
                var first = (1 << triangles[0]) | (1 << triangles[1]) | (1 << triangles[2]);
                var second = (1 << triangles[3]) | (1 << triangles[4]) | (1 << triangles[5]);
                var shared = first & second;
                var sharedPoints = new List<Vector2>(2);
                for (var i = 0; i < 4; i++) if ((shared & (1 << i)) != 0) sharedPoints.Add(vertices[i] * sprite.pixelsPerUnit);
                if ((first | second) == 15 && sharedPoints.Count == 2 &&
                    Mathf.Abs(Mathf.Abs(sharedPoints[0].x - sharedPoints[1].x) - width) < .01f &&
                    Mathf.Abs(Mathf.Abs(sharedPoints[0].y - sharedPoints[1].y) - height) < .01f)
                    return new VisualTexture { Width = width, Height = height, Pixels = crop };
            }
        }
        var result = new VisualTexture { Width = width, Height = height, Pixels = new Color32[width * height] };
        for (var triangle = 0; triangle < triangles.Length; triangle += 3)
        {
            var ai = triangles[triangle]; var bi = triangles[triangle + 1]; var ci = triangles[triangle + 2];
            var a = vertices[ai] * sprite.pixelsPerUnit + sprite.pivot;
            var b = vertices[bi] * sprite.pixelsPerUnit + sprite.pivot;
            var c = vertices[ci] * sprite.pixelsPerUnit + sprite.pivot;
            var denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(denominator) < .000001f) continue;
            var x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            var x1 = Mathf.Min(width, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            var y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            var y1 = Mathf.Min(height, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++)
            {
                var wa = ((b.y - c.y) * (x + .5f - c.x) + (c.x - b.x) * (y + .5f - c.y)) / denominator;
                var wb = ((c.y - a.y) * (x + .5f - c.x) + (a.x - c.x) * (y + .5f - c.y)) / denominator;
                var wc = 1 - wa - wb;
                if (wa < -.00001f || wb < -.00001f || wc < -.00001f) continue;
                var uv = uvs[ai] * wa + uvs[bi] * wb + uvs[ci] * wc;
                var sx = Mathf.Clamp(Mathf.FloorToInt(uv.x * sprite.texture.width) - left, 0, cropWidth - 1);
                var sy = Mathf.Clamp(Mathf.FloorToInt(uv.y * sprite.texture.height) - bottom, 0, cropHeight - 1);
                result.Pixels[y * width + x] = crop[sy * cropWidth + sx];
            }
        }
        return result;
    }
    private static void ClearVisualTintCache()
    {
        foreach (Sprite value in BorrowedGraySprites.Values)
        {
            if (value != null)
            {
                UnityEngine.Object.Destroy(value.texture);
                UnityEngine.Object.Destroy(value);
            }
        }
        BorrowedGraySprites.Clear();
        foreach (Sprite value2 in VisualTintSprites.Values)
        {
            if (!(value2 == null))
            {
                UnityEngine.Object.Destroy(value2.texture);
                UnityEngine.Object.Destroy(value2);
            }
        }
        VisualTintSprites.Clear();
        PruneVisualSourceTextures();
        VisualTintFailures.Clear();
        ResetVisualOverlays();
        ReleaseNoteTintMaterials();
        EachGuideTintPrewarms.Clear();
        EachGuideTintPrewarmBuilt = 0;
        VisualState.ReleaseIndexes();
    }


    private struct VisualTintKey : IEquatable<VisualTintKey>
    {
        public Sprite Sprite;

        public int Rgb;

        public int Vertex;

        public VisualTintKey(Sprite sprite, int rgb, Color32 vertex)
        {
            Sprite = sprite;
            Rgb = rgb;
            Vertex = vertex.r | (vertex.g << 8) | (vertex.b << 16) | (vertex.a << 24);
        }

        public bool Equals(VisualTintKey other)
        {
            if ((object)Sprite == (object)other.Sprite && Rgb == other.Rgb)
            {
                return Vertex == other.Vertex;
            }
            return false;
        }

        public override bool Equals(object obj)
        {
            if (obj is VisualTintKey other)
            {
                return Equals(other);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return (RuntimeHelpers.GetHashCode(Sprite) * 31 + Rgb) * 31 + Vertex;
        }
    }

    private static VisualTexture GetVisualSourceTexture(Sprite source)
    {
        if (!VisualSourceTextures.TryGetValue(source, out var value))
        {
            value = ReadVisualTexture(source);
            VisualSourceTextures.Add(source, value);
        }
        return value;
    }

    private static void PruneVisualSourceTextures()
    {
        List<Sprite> list = null;
        foreach (Sprite key in VisualSourceTextures.Keys)
        {
            if (key == null)
            {
                (list ?? (list = new List<Sprite>())).Add(key);
            }
        }
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                VisualSourceTextures.Remove(list[i]);
            }
        }
    }

    private static Color32[] VisualPixelScratch;

    private static Color32[] VisualPixelBuffer(int length)
    {
        if (VisualPixelScratch == null || VisualPixelScratch.Length != length)
        {
            VisualPixelScratch = new Color32[length];
        }
        return VisualPixelScratch;
    }

    private const int VisualPixelMemoBits = 16;

    private static int[] VisualPixelMemoKeys;

    private static int[] VisualPixelMemoStamps;

    private static Color32[] VisualPixelMemoValues;

    private static int VisualPixelMemoStamp;

    private static int VisualPixelMemoCount;

    private static void BeginVisualPixelMemo()
    {
        if (VisualPixelMemoKeys == null)
        {
            VisualPixelMemoKeys = new int[1 << VisualPixelMemoBits];
            VisualPixelMemoStamps = new int[1 << VisualPixelMemoBits];
            VisualPixelMemoValues = new Color32[1 << VisualPixelMemoBits];
        }
        if (++VisualPixelMemoStamp == int.MaxValue)
        {
            Array.Clear(VisualPixelMemoStamps, 0, VisualPixelMemoStamps.Length);
            VisualPixelMemoStamp = 1;
        }
        VisualPixelMemoCount = 0;
    }

    private static bool TryVisualPixelMemo(int key, out int slot, out Color32 value)
    {
        slot = (int)((uint)(key * -1640531535) >> (32 - VisualPixelMemoBits));
        while (VisualPixelMemoStamps[slot] == VisualPixelMemoStamp)
        {
            if (VisualPixelMemoKeys[slot] == key)
            {
                value = VisualPixelMemoValues[slot];
                return true;
            }
            slot = (slot + 1) & ((1 << VisualPixelMemoBits) - 1);
        }
        value = default(Color32);
        return false;
    }

    private static void StoreVisualPixelMemo(int slot, int key, Color32 value)
    {
        if (VisualPixelMemoCount < 1 << (VisualPixelMemoBits - 1))
        {
            VisualPixelMemoStamps[slot] = VisualPixelMemoStamp;
            VisualPixelMemoKeys[slot] = key;
            VisualPixelMemoValues[slot] = value;
            VisualPixelMemoCount++;
        }
    }

    private static bool[] VisualTransparentAlpha;

    private static Color32[] VisualTransparentPixels;

    private static void EnsureVisualTransparentTable()
    {
        if (VisualTransparentAlpha != null)
        {
            return;
        }
        bool[] array = new bool[256];
        Color32[] array2 = new Color32[256];
        for (int i = 0; i < 256; i++)
        {
            Color color3 = new Color32(0, 0, 0, (byte)i);
            if (color3.a < 0.01f)
            {
                array[i] = true;
                array2[i] = new Color(0f, 0f, 0f, color3.a);
            }
        }
        VisualTransparentPixels = array2;
        VisualTransparentAlpha = array;
    }

    private static void TintVisualPixels(Color32[] source, Color32[] array, int rgb, Color32 color)
    {
        Color.RGBToHSV(new Color((float)((rgb >> 16) & 0xFF) / 255f, (float)((rgb >> 8) & 0xFF) / 255f, (float)(rgb & 0xFF) / 255f, 1f), out var H, out var S, out var V);
        Color color2 = color;
        EnsureVisualTransparentTable();
        BeginVisualPixelMemo();
        bool[] visualTransparentAlpha = VisualTransparentAlpha;
        Color32[] visualTransparentPixels = VisualTransparentPixels;
        for (int i = 0; i < array.Length; i++)
        {
            Color32 color5 = source[i];
            if (visualTransparentAlpha[color5.a])
            {
                array[i] = visualTransparentPixels[color5.a];
                continue;
            }
            int key = color5.r | (color5.g << 8) | (color5.b << 16) | (color5.a << 24);
            if (TryVisualPixelMemo(key, out var slot, out var value))
            {
                array[i] = value;
                continue;
            }
            Color color3 = color5;
            if (color3.a < 0.01f)
            {
                array[i] = new Color(0f, 0f, 0f, color3.a);
                continue;
            }
            color3.r *= color2.r;
            color3.g *= color2.g;
            color3.b *= color2.b;
            Color.RGBToHSV(color3, out var _, out var S2, out var V2);
            float num = VisualSmooth(0.04f, 0.16f, V2);
            float num2 = VisualSmooth(0.05f, 0.25f, S2);
            float num3 = 0.35f * (1f - V);
            float v = Mathf.Clamp01(V2 * V + num3 * (V2 - 0.5f));
            Color b = Color.HSVToRGB(H, Mathf.Clamp01(S2 * S), v);
            Color a = Color.Lerp(color3, b, num * num2);
            float num4 = color3.r * 0.299f + color3.g * 0.587f + color3.b * 0.114f;
            float num5 = Mathf.Clamp01(num4 * (V * 2f) + num3 * (num4 - 0.5f));
            Color b2 = Color.Lerp(color3, new Color(num5, num5, num5, color3.a), num);
            Color color4 = Color.Lerp(a, b2, 1f - VisualSmooth(0.05f, 0.2f, S));
            color4.a = color3.a;
            array[i] = color4;
            StoreVisualPixelMemo(slot, key, array[i]);
        }
    }

    private static void GrayVisualPixels(Color32[] source, Color32[] array)
    {
        BeginVisualPixelMemo();
        for (int i = 0; i < array.Length; i++)
        {
            Color32 color2 = source[i];
            int key = color2.r | (color2.g << 8) | (color2.b << 16) | (color2.a << 24);
            if (TryVisualPixelMemo(key, out var slot, out var value))
            {
                array[i] = value;
                continue;
            }
            Color color = color2;
            float num = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
            array[i] = new Color(num, num, num, color.a);
            StoreVisualPixelMemo(slot, key, array[i]);
        }
    }

    private static bool IsFullTextureQuad(Sprite sprite, UnityEngine.Vector2[] vertices, UnityEngine.Vector2[] uv, ushort[] triangles, int width, int height, int rectWidth, int rectHeight)
    {
        if (vertices.Length != 4 || uv.Length != 4 || triangles.Length != 6 || width != rectWidth || height != rectHeight || width <= 0 || height <= 0 || width > 2048 || height > 2048)
        {
            return false;
        }
        int num = 0;
        for (int i = 0; i < 4; i++)
        {
            UnityEngine.Vector2 vector = uv[i];
            if ((vector.x != 0f && vector.x != 1f) || (vector.y != 0f && vector.y != 1f))
            {
                return false;
            }
            int num2 = 1 << (((vector.x == 1f) ? 1 : 0) + ((vector.y == 1f) ? 2 : 0));
            if ((num & num2) != 0)
            {
                return false;
            }
            num |= num2;
            UnityEngine.Vector2 vector2 = vertices[i] * sprite.pixelsPerUnit + sprite.pivot;
            if (vector2.x != vector.x * (float)width || vector2.y != vector.y * (float)height)
            {
                return false;
            }
        }
        int num3 = 0;
        int num4 = 0;
        for (int j = 0; j < 3; j++)
        {
            if (triangles[j] > 3 || triangles[j + 3] > 3)
            {
                return false;
            }
            num3 |= 1 << triangles[j];
            num4 |= 1 << triangles[j + 3];
        }
        int num5 = num3 & num4;
        if (VisualCornerCount(num3) != 3 || VisualCornerCount(num4) != 3 || VisualCornerCount(num5) != 2)
        {
            return false;
        }
        int num6 = -1;
        int num7 = -1;
        for (int k = 0; k < 4; k++)
        {
            if ((num5 & (1 << k)) != 0)
            {
                if (num6 < 0)
                {
                    num6 = k;
                }
                else
                {
                    num7 = k;
                }
            }
        }
        if (uv[num6].x != uv[num7].x)
        {
            return uv[num6].y != uv[num7].y;
        }
        return false;
    }

    private static int VisualCornerCount(int mask)
    {
        return (mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1) + ((mask >> 3) & 1);
    }
}
