using System;
using System.Collections.Generic;
using System.IO;
using SinmaiAlpha.ChartVisuals;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes.Libs;

// Image semantics match the reference NoteSkinLibrary: centered pivot, same
// world width as the normal model, original aspect ratio, no EX/guide swap.
public sealed class NoteSkinAssets
{
    public string Root;
    private readonly Dictionary<string, Texture2D> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Path, int Model), Sprite> sprites = new();
    private readonly Queue<string> preload = new();
    private bool queued;
    public void Prepare(IEnumerable<string> paths)
    {
        if (queued) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths) if (seen.Add(path)) preload.Enqueue(path);
        queued = true;
    }
    public bool PreloadTick()
    {
        // Decode a bounded number on the Unity thread during native readiness.
        for (var i = 0; i < 4 && preload.Count > 0; i++) Load(preload.Dequeue());
        return preload.Count == 0;
    }
    private Texture2D Load(string path)
    {
        if (textures.TryGetValue(path, out var cached)) return cached;
        Texture2D texture = null;
        try
        {
            var full = NoteSkin.ResolvePath(Root, path);
            if (full == null || !File.Exists(full)) throw new IOException("Image not found next to MA2");
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(full))) throw new IOException("Unreadable png/jpg");
            texture.wrapMode = TextureWrapMode.Clamp;
        }
        catch (Exception e)
        {
            if (texture != null) Object.Destroy(texture);
            texture = null;
            MelonLogger.Warning("[Note Skin] " + path + ": " + e.Message + "; keeping native picture");
        }
        textures[path] = texture;
        return texture;
    }
    public Sprite Create(string path, Sprite model)
    {
        if (path == null)
        {
            return null;
        }
        if (!normalized.TryGetValue(path, out var path2))
        {
            path2 = (NoteSkin.Normalize(path, out var path3) ? path3 : null);
            normalized[path] = path2;
        }
        if (path2 == null)
        {
            return null;
        }
        (string, int) key = (path2, (!(model == null)) ? model.GetInstanceID() : 0);
        if (sprites.TryGetValue(key, out var value))
        {
            return value;
        }
        Texture2D texture2D = Load(path2);
        if (texture2D == null)
        {
            sprites[key] = null;
            return null;
        }
        float num = ((model != null && model.rect.width > 0f) ? (model.rect.width / model.pixelsPerUnit) : 0f);
        float pixelsPerUnit = ((num > 0.0001f) ? ((float)texture2D.width / num) : 100f);
        Sprite sprite = Sprite.Create(texture2D, new Rect(0f, 0f, texture2D.width, texture2D.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        sprite.name = path2;
        sprites[key] = sprite;
        return sprite;
    }
    public void Clear()
    {
        foreach (Sprite value in sprites.Values)
        {
            if (value != null)
            {
                UnityEngine.Object.Destroy(value);
            }
        }
        sprites.Clear();
        foreach (Texture2D value2 in textures.Values)
        {
            if (value2 != null)
            {
                UnityEngine.Object.Destroy(value2);
            }
        }
        textures.Clear();
        normalized.Clear();
        preload.Clear();
        queued = false;
    }


    private readonly Dictionary<string, string> normalized = new Dictionary<string, string>(StringComparer.Ordinal);
}
