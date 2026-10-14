using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SinmaiAlpha.Hosting;
using SinmaiAlpha.ChartVisuals;
using HarmonyLib;
using Manager;
using MelonLoader;
using Monitor;
using Monitor.Game;
using Process;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private const string TouchProgressShaderName = "Hidden/AquaMai/TouchHoldProgress";
    private sealed class TouchProgressLease
    {
        public TouchHoldC Owner;
        public SpriteRenderer Render;
        public Material Original, Material;
        public Sprite OriginalSprite;
        public void Restore()
        {
            if (Owner != null) Traverse.Create(Owner).Field("GaugeMaterial").SetValue(Original);
            if (Render != null) { Render.sharedMaterial = Original; Render.sprite = OriginalSprite; }
            if (Material != null) Object.Destroy(Material);
        }
    }
    private static readonly Dictionary<TouchHoldC, TouchProgressLease> TouchProgressOwners = new();
    private static AssetBundle TouchProgressBundle;
    private static Shader TouchProgressShader;
    private static bool TouchProgressWarning;

    private static bool LoadTouchProgressShader()
    {
        if (TouchProgressShader != null)
        {
            return true;
        }
        if (TouchProgressLoadFailed)
        {
            return false;
        }
        try
        {
            string path = FileSystem.ResolvePath("Sinmai-Alpha/Alpha/NoteVisuals.ab");
            if (!File.Exists(path))
            {
                TouchProgressLoadFailed = true;
                return false;
            }
            TouchProgressBundle = AssetBundle.LoadFromFile(path);
            NoteTintShaderUnavailable = false;
            if (TouchProgressBundle != null)
            {
                TouchProgressShader = TouchProgressBundle.LoadAsset<Shader>("assets/aquamai/touchholdprogress.shader");
            }
            if (TouchProgressShader != null && TouchProgressShader.isSupported)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            if (!TouchProgressWarning)
            {
                MelonLogger.Warning("[TouchHold Progress] " + ex.Message);
            }
        }
        if (!TouchProgressWarning)
        {
            MelonLogger.Warning("[TouchHold Progress] Material unavailable; retaining native gauge.");
        }
        TouchProgressWarning = true;
        TouchProgressLoadFailed = true;
        if (TouchProgressBundle != null)
        {
            TouchProgressBundle.Unload(unloadAllLoadedObjects: true);
        }
        TouchProgressBundle = null;
        TouchProgressShader = null;
        return false;
    }
    private static void ApplyBreakTouchHoldProgress(Component component)
    {
        if (component is not TouchBreakHoldC owner || !LoadTouchProgressShader() ||
            !MineTextures.TryGetValue("touchhold_progress", out var texture) ||
            !MineTextures.TryGetValue("touchhold_progress_mask", out var mask)) return;
        var fields = Traverse.Create(owner);
        var render = fields.Field("HoldGaugeObject").GetValue<SpriteRenderer>();
        if (render == null || GameNoteImageContainer.TouchHoldGuide == null) return;
        if (!TouchProgressOwners.TryGetValue(owner, out var lease))
        {
            lease = new TouchProgressLease { Owner = owner, Render = render,
                Original = fields.Field("GaugeMaterial").GetValue<Material>(), OriginalSprite = render.sprite,
                Material = new Material(TouchProgressShader) { name = "AquaMai Break TouchHold Progress" } };
            var material = lease.Material;
            material.SetColor("_Color", Color.white);
            material.SetColor("_NoteColor", new Color(1f, 101f / 255f, 56f / 255f, 1f));
            material.SetFloat("_Brightness", 1.12f); material.SetFloat("_SrcHue", 0f);
            material.SetFloat("_DarkDetail", .35f); material.SetFloat("_NoteAlpha", 1f);
            material.SetFloat("_MaskScale", GameNoteImageContainer.TouchHoldGuide.pixelsPerUnit / 502f);
            material.SetTexture("_ProgressMask", mask);
            if (lease.Original != null && lease.Original.HasProperty("_Amount"))
                material.SetFloat("_Amount", lease.Original.GetFloat("_Amount"));
            TouchProgressOwners.Add(owner, lease);
        }
        render.sprite = CreateSpriteFromTexture("touchhold_progress", texture, GameNoteImageContainer.TouchHoldGuide);
        render.sharedMaterial = lease.Material;
        fields.Field("GaugeMaterial").SetValue(lease.Material);
    }
    private static bool IsTouchProgressMaterial(SpriteRenderer render) => render.sharedMaterial != null &&
        render.sharedMaterial.shader != null && render.sharedMaterial.shader.name == TouchProgressShaderName;
    private static void ApplyTouchProgressLiveVisual(SpriteRenderer render, VisualValue live)
    {
        MaterialPropertyBlock materialPropertyBlock = TouchProgressLiveBlock ?? (TouchProgressLiveBlock = new MaterialPropertyBlock());
        render.GetPropertyBlock(materialPropertyBlock);
        int num = live.Color ?? 16737592;
        materialPropertyBlock.SetColor("_NoteColor", new Color((float)((num >> 16) & 0xFF) / 255f, (float)((num >> 8) & 0xFF) / 255f, (float)(num & 0xFF) / 255f, 1f));
        materialPropertyBlock.SetFloat("_NoteAlpha", live.Alpha ?? 1f);
        bool flag = live.Color.HasValue || live.Alpha.HasValue;
        materialPropertyBlock.SetFloat("_Brightness", flag ? 1f : 1.12f);
        materialPropertyBlock.SetFloat("_SrcHue", flag ? (-1) : 0);
        render.SetPropertyBlock(materialPropertyBlock);
    }
    private static void ResetTouchProgress(int monitor = -1, NotesReader reader = null)
    {
        KeyValuePair<TouchHoldC, TouchProgressLease>[] array = TouchProgressOwners.ToArray();
        for (int i = 0; i < array.Length; i++)
        {
            KeyValuePair<TouchHoldC, TouchProgressLease> keyValuePair = array[i];
            if (!(keyValuePair.Key != null) || ((monitor < 0 || keyValuePair.Key.MonitorId == monitor) && (reader == null || NotesManager.Instance(keyValuePair.Key.MonitorId).getReader() == reader)))
            {
                RestoreVisualLease(keyValuePair.Key);
                keyValuePair.Value.Restore();
                TouchProgressOwners.Remove(keyValuePair.Key);
            }
        }
        // A per-monitor reset is a seek (TouchProgressCollectPatch): NoteVisuals.ab and its shaders stay loaded (the
        // same shared assets the next note would load again). Chart load and release unload as before and re-arm a
        // failed load.
        if (monitor >= 0)
        {
            return;
        }
        // A chart load (loadMa2Main) also drops the VisualState stream indexes, rebuilt on first use: attract mode never
        // reaches ReleaseChartPresentation, so they would otherwise keep each demo chart's streams until the cap.
        if (reader != null)
        {
            VisualState.ReleaseIndexes();
        }
        TouchProgressLoadFailed = false;
        if (TouchProgressOwners.Count == 0 && !HasNoteTintMaterials())
        {
            if (TouchProgressBundle != null)
            {
                TouchProgressBundle.Unload(unloadAllLoadedObjects: true);
            }
            TouchProgressBundle = null;
            TouchProgressShader = null;
        }
    }
    [HarmonyPatch(typeof(TouchBreakHoldC), nameof(TouchBreakHoldC.Initialize))]
    public static class TouchProgressInitializePatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First - 20)]
        public static void Prefix(TouchBreakHoldC __instance)
        {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

            if (!TouchProgressOwners.TryGetValue(__instance, out var lease)) return;
            RestoreVisualLease(__instance); lease.Restore(); TouchProgressOwners.Remove(__instance);
        }
    }
    [HarmonyPatch(typeof(GameCtrl), "ForceNoteCollect")]
    public static class TouchProgressCollectPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First - 20)]
        public static void Prefix(GameCtrl __instance) => ResetTouchProgress(__instance.MonitorIndex);
    }
    [HarmonyPatch(typeof(NotesReader), "loadMa2Main")]
    public static class TouchProgressReaderResetPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void Prefix(NotesReader __instance) => ResetTouchProgress(reader: __instance);
    }
    private static void CopyVisualSpriteGeometry(Sprite destination, Sprite source)
    {
        // OverrideGeometry expects pixels relative to rect, whereas vertices are
        // relative to the pivot in world units. Unity applies pivot/PPU itself.
        var vertices = source.vertices;
        for (var i = 0; i < vertices.Length; i++)
            vertices[i] = vertices[i] * source.pixelsPerUnit + source.pivot;
        destination.OverrideGeometry(vertices, source.triangles);
    }


    private static bool TouchProgressLoadFailed;

    private static bool NoteTintShaderUnavailable;

    private static MaterialPropertyBlock TouchProgressLiveBlock;
}
