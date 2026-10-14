using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Hosting;
using SinmaiAlpha.Compatibility;
using HarmonyLib;
using Manager;
using MelonLoader;
using Monitor;
using Monitor.Game;
using Process;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using SinmaiAlpha.Notes.Libs;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class PresentationTimeline
    {
        public readonly Dictionary<string, PresentationCommands.Track> Tracks = new();
        public ComboDisplayCommands.Track ComboDisplay;
        public float Value(string kind, float time) => Tracks.TryGetValue(kind, out var track) ? track.Evaluate(time) : 0;
    }
    private sealed class CoverLease
    {
        public GameObject Owner;
        public bool Active;
        public bool ToggleOwner;
        public SpriteRenderer[] Sprites;
        public Color[] Colors;
        public bool[] SpriteEnabled;
        public Graphic[] Graphics;
        public Color[] GraphicColors;
        public bool[] GraphicEnabled;
        public void Apply(float alpha)
        {
            if (Owner == null) return;
            alpha = PresentationViewportMath.CoverAlpha(alpha);
            if (ToggleOwner) Owner.SetActive(true);
            for (var i = 0; i < Sprites.Length; i++) if (Sprites[i] != null)
            { var color = Colors[i]; color.a = alpha; Sprites[i].color = color; if (ToggleOwner) Sprites[i].enabled = true; }
            for (var i = 0; i < Graphics.Length; i++) if (Graphics[i] != null)
            { var color = GraphicColors[i]; color.a = alpha; Graphics[i].color = color; if (ToggleOwner) Graphics[i].enabled = true; }
        }
        public void ApplyPlayerBrightness(PresentationCommands.Track track, float time)
        {
            if (Owner == null) return;
            if (!track.IsPlayerBrightnessActive(time)) { Restore(); return; }
            if (ToggleOwner) Owner.SetActive(true);
            for (var i = 0; i < Sprites.Length; i++) if (Sprites[i] != null)
            { var color = Colors[i]; color.a = track.EvaluatePlayerBrightness(time, Colors[i].a); Sprites[i].color = color; if (ToggleOwner) Sprites[i].enabled = true; }
            for (var i = 0; i < Graphics.Length; i++) if (Graphics[i] != null)
            { var color = GraphicColors[i]; color.a = track.EvaluatePlayerBrightness(time, GraphicColors[i].a); Graphics[i].color = color; if (ToggleOwner) Graphics[i].enabled = true; }
        }
        public void Restore()
        {
            if (Owner == null) return;
            for (var i = 0; i < Sprites.Length; i++) if (Sprites[i] != null) { Sprites[i].color = Colors[i]; Sprites[i].enabled = SpriteEnabled[i]; }
            for (var i = 0; i < Graphics.Length; i++) if (Graphics[i] != null) { Graphics[i].color = GraphicColors[i]; Graphics[i].enabled = GraphicEnabled[i]; }
            if (ToggleOwner) Owner.SetActive(Active);
        }
    }
    private static List<(int Bar, int Grid, string Kind, string Text)> PendingPresentation => RuntimeCharts.Current.PendingPresentation;
    private static ConditionalWeakTable<NotesReader, PresentationTimeline> PresentationTimelines = new();
    private static readonly Dictionary<GameMonitor, ReferenceSideStatistics> PresentationPanels = new();
    private static readonly Dictionary<Camera, ChartShakeCamera> PresentationCameras = new();
    private static readonly Dictionary<Camera, ChartFrameMotion> PresentationMotionCameras = new();
    private static readonly List<Transform> MotionCovers = new();
    private static readonly Dictionary<GameObject, CoverLease> PresentationCovers = new();
    private sealed class SharedTiledCover
    {
        public SpriteRenderer Source;
        public bool Enabled;
        public readonly SpriteRenderer[] Sides = new SpriteRenderer[2];
        public readonly Dictionary<int, float> Alpha = new();
        public readonly Dictionary<int, PresentationCommands.Track> Brightness = new();
        public readonly MaterialPropertyBlock Properties = new();
        public void Apply()
        {
            if (Source == null) return;
            Source.GetPropertyBlock(Properties);
            for (var side = 0; side < 2; side++)
            {
                var renderer = Sides[side];
                renderer.sprite = Source.sprite; renderer.sharedMaterial = Source.sharedMaterial;
                renderer.drawMode = Source.drawMode; renderer.size = new Vector2(Source.size.x / 2, Source.size.y);
                renderer.tileMode = Source.tileMode; renderer.adaptiveModeThreshold = Source.adaptiveModeThreshold;
                renderer.flipX = Source.flipX; renderer.flipY = Source.flipY;
                renderer.maskInteraction = Source.maskInteraction; renderer.spriteSortPoint = Source.spriteSortPoint;
                renderer.sortingLayerID = Source.sortingLayerID; renderer.sortingOrder = Source.sortingOrder;
                renderer.transform.position = Source.transform.TransformPoint(new Vector3((side == 0 ? -1 : 1) * Source.size.x / 4, 0, 0));
                renderer.transform.rotation = Source.transform.rotation; renderer.transform.localScale = Source.transform.localScale;
                renderer.SetPropertyBlock(Properties);
                var color = Source.color;
                var authored = Alpha.TryGetValue(side, out var value);
                var brightnessActive = Brightness.TryGetValue(side, out var brightnessTrack) && brightnessTrack.IsPlayerBrightnessActive(NotesManager.GetCurrentMsec());
                if (Brightness.TryGetValue(side, out var track)) color.a = track.EvaluatePlayerBrightness(NotesManager.GetCurrentMsec(), color.a);
                else if (authored) color.a = PresentationViewportMath.CoverAlpha(value);
                renderer.color = color; renderer.enabled = authored || brightnessActive || Enabled && Source.gameObject.activeInHierarchy;
            }
            Source.enabled = false;
        }
        public void Restore()
        {
            if (Source != null) Source.enabled = Enabled;
            foreach (var side in Sides) if (side != null) Object.Destroy(side.gameObject);
        }
    }
    private static readonly Dictionary<SpriteRenderer, SharedTiledCover> PresentationSharedCovers = new();
    private static readonly HashSet<SpriteRenderer> PresentationCoverCopies = new();

    private static void ApplySharedOuterCover(GameObject owner, int monitor, float? alpha, PresentationCommands.Track brightness = null)
    {
        if (owner == null)
        {
            return;
        }
        // The List overload walks the same hierarchy in the same order as the array overload, and the snapshot
        // is still complete before the loop creates the ChartOuterCover children. Cleared afterwards so it does
        // not keep renderers alive.
        List<SpriteRenderer> sharedCoverBuffer = SharedCoverBuffer;
        sharedCoverBuffer.Clear();
        owner.GetComponentsInChildren(includeInactive: true, sharedCoverBuffer);
        try
        {
            for (int i = 0; i < sharedCoverBuffer.Count; i++)
            {
                SpriteRenderer spriteRenderer = sharedCoverBuffer[i];
                if (PresentationCoverCopies.Contains(spriteRenderer) || spriteRenderer.drawMode != SpriteDrawMode.Tiled || spriteRenderer.sprite == null)
                {
                    continue;
                }
                if (!PresentationSharedCovers.TryGetValue(spriteRenderer, out var value))
                {
                    value = new SharedTiledCover
                    {
                        Source = spriteRenderer,
                        Enabled = spriteRenderer.enabled
                    };
                    for (int j = 0; j < 2; j++)
                    {
                        GameObject gameObject = new GameObject("ChartOuterCover" + j, typeof(SpriteRenderer));
                        gameObject.layer = spriteRenderer.gameObject.layer;
                        gameObject.transform.SetParent(spriteRenderer.transform.parent, worldPositionStays: false);
                        value.Sides[j] = gameObject.GetComponent<SpriteRenderer>();
                        PresentationCoverCopies.Add(value.Sides[j]);
                    }
                    PresentationSharedCovers[spriteRenderer] = value;
                }
                if (brightness != null)
                {
                    value.Brightness[monitor] = brightness;
                }
                if (alpha.HasValue)
                {
                    value.Alpha[monitor] = alpha.Value;
                }
                value.Apply();
            }
        }
        finally
        {
            sharedCoverBuffer.Clear();
        }
    }

    private static MonitorBackgroundTownController BackgroundFor(GameMonitor monitor, CanvasGroup main)
    {
        return ChartFrameMotion.NativeBackgroundFor(monitor);
    }

    public static void ResetPresentationCommands(NotesReader __instance)
    { foreach (var effect in PresentationCameras.Values) if (effect != null) effect.ResetReader(__instance); foreach (var motion in PresentationMotionCameras.Values) if (motion != null) motion.ResetReader(__instance); PendingPresentation.Clear(); PresentationTimelines.Remove(__instance); ResetJudgeLineCommands(__instance); JudgeLineExpansionRuntime.Reset(__instance); ResetSubtitleCommands(__instance); ResetJudgeTextPresentation(__instance); ResetComboPresentation(__instance); ResetMediaPresentation(__instance); ResetBorrowedNotes(__instance); ResetStarHeads(__instance); ResetFireworks(__instance); ResetNoteSkinAssets(__instance); }

    [HarmonyPatch]
    public static class PresentationLoadResetPatch
    {
        public static IEnumerable<MethodBase> TargetMethods() => typeof(NotesReader).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.Name == "loadMa2" || m.Name == "loadStr" || m.Name == "loadDLMusicScore");
        [HarmonyPrefix]
        public static void Prefix(NotesReader __instance) => ResetPresentationCommands(__instance);
    }

    private static bool IsIgnoredCabinetPresentation(string kind) => kind == "showjudgeinfo" || kind == "showcomboinfo" || kind == "outerbrightness";
    public static void ReadPresentationCommand(string str)
    {
        ReadSubtitleCommand(str);
        ReadMediaCommand(str);
        if (str == null) return;
        var p = str.Split('\t');
        if (p.Length == 4 && !IsIgnoredCabinetPresentation(p[0].ToLowerInvariant()) && PresentationCommands.IsKind(p[0].ToLowerInvariant()) &&
            int.TryParse(p[1], out var bar) && int.TryParse(p[2], out var grid))
            PendingPresentation.Add((bar, grid, p[0].ToLowerInvariant(), p[3]));
    }
    public static void BuildPresentationCommands(NotesReader __instance)
    {
        BuildSubtitleCommands(__instance);
        BuildMediaCommands(__instance);
        var changes = new List<PresentationChange>();
        foreach (var item in PendingPresentation)
        {
            var time = new NotesTime(); time.init(item.Bar, item.Grid, __instance);
            if (PresentationCommands.Decode(item.Kind, item.Text, time.msec, out var change)) changes.Add(change);
            else MelonLogger.Warning("[Chart Presentation] Invalid " + item.Kind + ": " + item.Text);
        }
        BuildJudgeLineCommands(__instance, changes);
        JudgeLineExpansionRuntime.Build(__instance, changes);
        var timeline = new PresentationTimeline();
        foreach (var kind in changes.Select(c => c.Kind).Distinct())
            if (kind == "combodisplay") timeline.ComboDisplay = new ComboDisplayCommands.Track(changes);
            else if (!JudgeLineCommands.IsKind(kind) && !JudgeLineExpansionCommands.IsKind(kind)) timeline.Tracks[kind] = new PresentationCommands.Track(changes, kind);
        PresentationTimelines.Remove(__instance); PresentationTimelines.Add(__instance, timeline);
        if (changes.Count != 0) MelonLogger.Msg("[Chart Presentation] Loaded " + changes.Count + " display/effect events");
    }
    private static void ApplyPresentationCover(GameObject owner, float alpha, bool toggleOwner = true, bool applyValue = true)
    {
        if (owner == null) return;
        if (!PresentationCovers.TryGetValue(owner, out var lease))
        {
            var sprites = owner.GetComponentsInChildren<SpriteRenderer>(true);
            var graphics = owner.GetComponentsInChildren<Graphic>(true);
            // Inner cover owns the movie child; changing its active state would
            // hide the background itself. Only its own renderer is a cover.
            if (!toggleOwner) { sprites = owner.GetComponents<SpriteRenderer>(); graphics = owner.GetComponents<Graphic>(); }
            lease = new CoverLease { Owner = owner, Active = owner.activeSelf, ToggleOwner = toggleOwner, Sprites = sprites,
                Colors = sprites.Select(s => s.color).ToArray(), SpriteEnabled = sprites.Select(s => s.enabled).ToArray(),
                Graphics = graphics, GraphicColors = graphics.Select(g => g.color).ToArray(), GraphicEnabled = graphics.Select(g => g.enabled).ToArray() };
            PresentationCovers[owner] = lease;
        }
        if (applyValue) lease.Apply(alpha);
    }
    public static void ApplyChartPresentation(GameMonitor __instance)
    {
        ApplyWatermark(__instance);
        ApplyJudgeLinePresentation(__instance);
        JudgeLineExpansionRuntime.Apply(__instance);
        ApplySubtitlePresentation(__instance);
        ApplyMediaPresentation(__instance);
        ApplyComboPresentation(__instance);
        if (GuiSizes.SinglePlayer && __instance.MonitorIndex != 0)
        {
            ReleaseNoisePresentation(__instance);
            foreach (ChartFrameMotion value8 in PresentationMotionCameras.Values)
            {
                if (value8 != null)
                {
                    value8.Unbind(__instance);
                }
            }
            ReleaseJudgeTextPresentation(__instance);
            {
                foreach (ChartShakeCamera value9 in PresentationCameras.Values)
                {
                    if (value9 != null)
                    {
                        value9.Unbind(__instance);
                    }
                }
                return;
            }
        }
        NotesReader reader = NotesManager.Instance(__instance.MonitorIndex).getReader();
        ApplyJudgeTextPresentation(__instance, reader);
        GameCtrl value = ControllerOf(__instance);
        CanvasGroup value2 = MainOf(__instance);
        if (value2 == null || value == null)
        {
            return;
        }
        NoiseRenderState noiseRenderState = ApplyNoisePresentation(__instance, reader, value);
        if (!PresentationTimelines.TryGetValue(reader, out var value3))
        {
            value3 = EmptyNoisePresentation;
        }
        if (value3.Tracks.Count == 0 && noiseRenderState == null)
        {
            foreach (ChartFrameMotion value10 in PresentationMotionCameras.Values)
            {
                if (value10 != null)
                {
                    value10.Unbind(__instance);
                }
            }
            {
                foreach (ChartShakeCamera value11 in PresentationCameras.Values)
                {
                    if (value11 != null)
                    {
                        value11.Unbind(__instance);
                    }
                }
                return;
            }
        }
        NotesManager.GetCurrentMsec();
        value3.Tracks.TryGetValue("shake", out var value4);
        value3.Tracks.TryGetValue("flash", out var value5);
        value3.Tracks.TryGetValue("tint", out var value6);
        bool flag = value3.Tracks.ContainsKey("move") || value3.Tracks.ContainsKey("rotate") || value3.Tracks.ContainsKey("zoom");
        if (((noiseRenderState != null || value4 != null || value5 != null || value6 != null) | flag) || ChartScreenEffects.HasTracks(value3.Tracks))
        {
            Canvas componentInParent = value2.GetComponentInParent<Canvas>();
            Camera gameRenderCamera = ScreenPositionAdjust.GameRenderCamera;
            Camera camera;
            if (gameRenderCamera != null)
            {
                camera = gameRenderCamera;
            }
            else
            {
                camera = ((componentInParent != null) ? componentInParent.worldCamera : null);
            }
            if (camera == null)
            {
                camera = Camera.main;
            }
            if (camera == null)
            {
                return;
            }
            if (!PresentationCameras.TryGetValue(camera, out var value7))
            {
                value7 = camera.gameObject.AddComponent<ChartShakeCamera>();
                PresentationCameras[camera] = value7;
            }
            Transform transform = (((!GuiSizes.SinglePlayer || !SinglePlayer.HideSubMonitor || !(gameRenderCamera == null)) && componentInParent != null) ? componentInParent.transform : value2.transform);
            foreach (KeyValuePair<Camera, ChartShakeCamera> presentationCamera in PresentationCameras)
            {
                if (presentationCamera.Key != camera && presentationCamera.Value != null)
                {
                    presentationCamera.Value.Unbind(__instance);
                }
            }
            value7.Bind(__instance, transform as RectTransform, value4, value5, value6, value3.Tracks, reader);
            value7.BindNoise(__instance, noiseRenderState, value2.transform as RectTransform);
        }
        else
        {
            foreach (ChartShakeCamera value12 in PresentationCameras.Values)
            {
                if (value12 != null)
                {
                    value12.Unbind(__instance);
                }
            }
        }
        ApplyFrameMotion(__instance, reader, value, value2, value3, noiseRenderState);
    }
    private static void ApplyFrameMotion(GameMonitor owner, NotesReader reader, GameCtrl controller, CanvasGroup main, PresentationTimeline timeline, NoiseRenderState noise)
    {
        timeline.Tracks.TryGetValue("move", out var value);
        timeline.Tracks.TryGetValue("rotate", out var value2);
        timeline.Tracks.TryGetValue("zoom", out var value3);
        if (value == null && value2 == null && value3 == null && noise == null)
        {
            foreach (ChartFrameMotion value5 in PresentationMotionCameras.Values)
            {
                if (value5 != null)
                {
                    value5.Unbind(owner);
                }
            }
            return;
        }
        Canvas componentInParent = main.GetComponentInParent<Canvas>();
        Camera gameRenderCamera = ScreenPositionAdjust.GameRenderCamera;
        Camera camera;
        if (gameRenderCamera != null)
        {
            camera = gameRenderCamera;
        }
        else
        {
            camera = ((componentInParent != null) ? componentInParent.worldCamera : null);
        }
        if (camera == null)
        {
            camera = Camera.main;
        }
        if (camera == null)
        {
            return;
        }
        Transform transform = (((GuiSizes.SinglePlayer && SinglePlayer.HideSubMonitor && gameRenderCamera == null) || componentInParent == null) ? main.transform : componentInParent.transform);
        if (!PresentationMotionCameras.TryGetValue(camera, out var value4))
        {
            value4 = camera.gameObject.AddComponent<ChartFrameMotion>();
            PresentationMotionCameras[camera] = value4;
        }
        foreach (KeyValuePair<Camera, ChartFrameMotion> presentationMotionCamera in PresentationMotionCameras)
        {
            if (presentationMotionCamera.Key != camera && presentationMotionCamera.Value != null)
            {
                presentationMotionCamera.Value.Unbind(owner);
            }
        }
        MotionCovers.Clear();
        if (value != null || value2 != null || value3 != null)
        {
            ApplySharedOuterCover(GameObject.Find("Mask"), owner.MonitorIndex, null);
            foreach (SharedTiledCover value6 in PresentationSharedCovers.Values)
            {
                if (owner.MonitorIndex >= 0 && owner.MonitorIndex < value6.Sides.Length && value6.Sides[owner.MonitorIndex] != null)
                {
                    MotionCovers.Add(value6.Sides[owner.MonitorIndex].transform);
                }
            }
            MonitorBackgroundTownController monitorBackgroundTownController = BackgroundFor(owner, main);
            GameObject gameObject = ((monitorBackgroundTownController != null) ? ChartFrameMotion.NativeBackgroundObject(monitorBackgroundTownController) : null);
            Transform transform2 = ((gameObject != null) ? gameObject.transform.Find("BG/Monitor_Mask") : null);
            if (transform2 != null)
            {
                MotionCovers.Add(transform2);
            }
        }
        value4.Bind(owner, reader, controller, main.transform as RectTransform, transform as RectTransform, value, value2, value3, MotionCovers);
        value4.BindNoise(owner, noise);
    }
    public static void ReleaseChartPresentation()
    {
        ReleaseWatermarks();
        ResetTouchProgress();
        ResetNoisePresentation();
        ReleaseSubtitlePresentation();
        ReleaseJudgeLinePresentation();
        JudgeLineExpansionRuntime.ReleaseAll();
        ReleaseComboPresentation();
        ReleaseMediaPresentation();
        ReleaseBorrowedNotes();
        ResetStarHeads();
        ReleaseNoteSkinAssets();
        ReleaseJudgeTextPresentation();
        foreach (var lease in PresentationCovers.Values) lease.Restore();
        foreach (var lease in PresentationSharedCovers.Values) lease.Restore();
        foreach (var panel in PresentationPanels.Values) if (panel != null) Object.Destroy(panel.gameObject);
        foreach (var camera in PresentationCameras.Values) if (camera != null) { camera.Clear(); Object.Destroy(camera); }
        foreach (var camera in PresentationMotionCameras.Values) if (camera != null) { camera.Clear(); Object.Destroy(camera); }
        PresentationMotionCameras.Clear(); MotionCovers.Clear();
        PresentationCovers.Clear(); PresentationSharedCovers.Clear(); PresentationCoverCopies.Clear(); PresentationPanels.Clear(); PresentationCameras.Clear(); PendingPresentation.Clear();
    }
    [HarmonyPatch(typeof(NotesRecord), "addRecord", new[] { typeof(string) })]
    public static class PresentationRecordPatch
    {
        [HarmonyPrefix] public static void Prefix(string str) {
        if (!(CustomNoteTypes.FeaturesEnabled())) {  return; }
        ReadPresentationCommand(str);
    }
    }
    [HarmonyPatch(typeof(NotesReader), "loadMa2Main")]
    public static class PresentationBuildPatch
    {
        [HarmonyPostfix] public static void Postfix(NotesReader __instance) {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }
        BuildPresentationCommands(__instance);
    }
    }
    [HarmonyPatch(typeof(GameMonitor), "ViewUpdate")]
    public static class PresentationViewPatch
    {
        [HarmonyPrefix] public static void Prefix(GameMonitor __instance)
        {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }
 foreach (var motion in PresentationMotionCameras.Values) if (motion != null) motion.BeforeNativeUpdate(__instance); }
        [HarmonyPostfix] public static void Postfix(GameMonitor __instance) {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }
        ApplyChartPresentation(__instance);
    }
    }
    [HarmonyPatch(typeof(GameCtrl), "UpdateMovie")]
    public static class PresentationMovieBrightnessPatch
    {
        [HarmonyPostfix]
        public static void Postfix(int ___monitorIndex, SpriteRenderer ____movieMaskSprite)
        {
        if (!(CustomNoteTypes.FeaturesForMonitor(___monitorIndex))) {  return; }

            if (____movieMaskSprite == null || GuiSizes.SinglePlayer && ___monitorIndex != 0) return;
            var reader = NotesManager.Instance(___monitorIndex).getReader();
            if (!PresentationTimelines.TryGetValue(reader, out var timeline) ||
                !timeline.Tracks.TryGetValue("innerbrightness", out var inner)) return;
            // UpdateMovie has just computed this frame's native option opacity
            // and opening fade. Compose once here, never from our previous result.
            var color = ____movieMaskSprite.color;
            color.a = PresentationViewportMath.MovieCoverAlpha(color.a, inner.EvaluatePlayerBrightness(NotesManager.GetCurrentMsec(), 0));
            ____movieMaskSprite.color = color;
        }
    }
    [HarmonyPatch(typeof(GameProcess), "OnRelease")]
    public static class PresentationReleasePatch
    {
        [HarmonyPostfix] public static void Postfix() => ReleaseChartPresentation();
    }


    private static readonly List<SpriteRenderer> SharedCoverBuffer = new List<SpriteRenderer>();
}

// Image-space UV offset leaves logical sensors, native input queues and timing untouched.
public sealed class ChartShakeCamera : MonoBehaviour
{
    private sealed class Frame
    {
        public RectTransform Bounds;
        public PresentationCommands.Track Track;
        public PresentationCommands.Track Flash, Tint;
        public PresentationChange TintSource;
        public Color TintColor;
        public NotesReader Reader;
        public IDictionary<string, PresentationCommands.Track> AllTracks;
        public ChartScreenEffects ScreenEffects;
        public CustomNoteTypes.NoiseRenderState Noise;
        public RectTransform Playfield;
        public readonly Vector3[] Corners = new Vector3[4];
    }
    private readonly Dictionary<GameMonitor, Frame> frames = new();
    private Material colorMaterial;
    private bool colorMaterialUnavailable;
    internal void Bind(GameMonitor owner, RectTransform bounds, PresentationCommands.Track track)
        => Bind(owner, bounds, track, null, null);
    internal void Bind(GameMonitor owner, RectTransform bounds, PresentationCommands.Track track,
        PresentationCommands.Track flash, PresentationCommands.Track tint)
        => Bind(owner, bounds, track, flash, tint, null, null);
    internal void Bind(GameMonitor owner, RectTransform bounds, PresentationCommands.Track track,
        PresentationCommands.Track flash, PresentationCommands.Track tint, IDictionary<string, PresentationCommands.Track> tracks, NotesReader reader)
    {
        if (!frames.TryGetValue(owner, out var frame)) frames[owner] = frame = new Frame();
        if (frame.AllTracks != tracks || frame.Reader != reader)
        {
            frame.ScreenEffects?.Dispose();
            frame.ScreenEffects = ChartScreenEffects.HasTracks(tracks) ? new ChartScreenEffects(tracks) : null;
            frame.AllTracks = tracks; frame.Reader = reader;
        }
        if (frame.Tint != tint) frame.TintSource = null;
        frame.Bounds = bounds; frame.Track = track; frame.Flash = flash; frame.Tint = tint;
    }
    internal void Unbind(GameMonitor owner) { if (frames.TryGetValue(owner, out var frame)) { frame.Noise?.CaptureField(null); frame.ScreenEffects?.Dispose(); } frames.Remove(owner); }
    internal void BindNoise(GameMonitor owner, CustomNoteTypes.NoiseRenderState noise, RectTransform playfield)
    { if (frames.TryGetValue(owner, out var frame)) { frame.Noise = noise; frame.Playfield = playfield; } }
    internal void ResetReader(NotesReader reader) { foreach (var owner in frames.Where(pair => pair.Value.Reader == reader).Select(pair => pair.Key).ToArray()) Unbind(owner); }
    internal void Clear() { foreach (var frame in frames.Values) { frame.Noise?.CaptureField(null); frame.ScreenEffects?.Dispose(); } frames.Clear(); ReleaseColorMaterial(); }
    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
    private void ReleaseColorMaterial()
    {
        if (colorMaterial != null) Object.Destroy(colorMaterial);
        colorMaterial = null; colorMaterialUnavailable = false;
    }
    private bool EnsureColorMaterial()
    {
        if (colorMaterial != null) return true;
        if (colorMaterialUnavailable) return false;
        var shader = Shader.Find("UI/Default");
        if (shader != null)
        {
            colorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (colorMaterial.HasProperty("_ColorMask"))
            {
                // Native UI shader uses the supplied vertex color directly.
                // Write RGB only: the reference post effect preserves alpha.
                colorMaterial.SetInt("_ColorMask", (int)(UnityEngine.Rendering.ColorWriteMask.Red |
                    UnityEngine.Rendering.ColorWriteMask.Green | UnityEngine.Rendering.ColorWriteMask.Blue));
                colorMaterial.SetColor("_Color", Color.white);
                colorMaterial.SetVector("_TextureSampleAdd", Vector4.zero);
                colorMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                colorMaterial.DisableKeyword("UNITY_UI_CLIP_RECT");
                colorMaterial.DisableKeyword("UNITY_UI_ALPHACLIP");
                return true;
            }
            Object.Destroy(colorMaterial); colorMaterial = null;
        }
        colorMaterialUnavailable = true;
        MelonLogger.Warning("[Chart Presentation] Native UI/Default with RGB color mask unavailable; color effects disabled");
        return false;
    }
    private void DrawColor(PresentationRect output, RenderTexture texture, Color color)
    {
        if (color.a <= 0 || !EnsureColorMaterial()) return;
        Graphics.DrawTexture(Pixels(output, texture.width, texture.height), Texture2D.whiteTexture,
            new Rect(0, 0, 1, 1), 0, 0, 0, 0, color, colorMaterial);
    }
    private PresentationRect Bounds(Frame frame, Camera camera)
        => Bounds(frame.Bounds, frame.Corners, camera);
    private PresentationRect Bounds(RectTransform bounds, Vector3[] corners, Camera camera)
    {
        bounds.GetWorldCorners(corners);
        var minX = float.PositiveInfinity; var minY = minX;
        var maxX = float.NegativeInfinity; var maxY = maxX;
        foreach (var corner in corners)
        {
            var point = camera.WorldToViewportPoint(corner);
            minX = Mathf.Min(minX, point.x); minY = Mathf.Min(minY, point.y);
            maxX = Mathf.Max(maxX, point.x); maxY = Mathf.Max(maxY, point.y);
        }
        return new PresentationRect(minX, minY, maxX - minX, maxY - minY);
    }
    private static Rect Pixels(PresentationRect rect, int width, int height)
        => new(rect.X * width, (1 - rect.Top) * height, rect.Width * width, rect.Height * height);
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        RenderTexture active = RenderTexture.active;
        bool flag = false;
        RenderTexture renderTexture = null;
        RenderTexture renderTexture2 = null;
        RenderTexture renderTexture4 = null;
        RenderTexture renderTexture5 = null;
        RenderTexture dest = destination;
        try
        {
            if (destination == null && AnyNoiseReady())
            {
                bool draws = true;
                try
                {
                    draws = DrawsAny(GetComponent<Camera>(), NotesManager.GetCurrentMsec());
                }
                catch (Exception)
                {
                    // A failed prediction keeps the original rt2 path, including its base blit.
                }
                if (draws)
                {
                    renderTexture2 = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear);
                    destination = renderTexture2;
                }
                else
                {
                    // Nothing draws: source -> rt2 -> backbuffer would be two texel copies of source, so blit it
                    // straight to the backbuffer below. Hold (unused) a temporary with rt2's descriptor for the frame,
                    // as a drawing frame does, so the pool does not destroy rt2 after 15 idle frames and the next
                    // effect or zone frame does not re-create it. Released with rt2's.
                    renderTexture5 = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear);
                }
            }
            ChartFrameMotion component = GetComponent<ChartFrameMotion>();
            if (component != null && component.HasIsolatedFrames)
            {
                renderTexture = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
                component.CompositeIsolation(source, renderTexture);
                source = renderTexture;
            }
            else if (component != null && component.KeepsIsolationTexturesPooled)
            {
                // Between motion segments: hold (unused) a temporary with the composite's descriptor for this frame,
                // as a motion frame does, so the pool does not destroy it after 15 idle frames and the next onset
                // does not re-create it (see ChartFrameMotion.KeepIsolationTexturesPooled). Released below.
                renderTexture4 = RenderTexture.GetTemporary(source.width, source.height, 0, source.format);
            }
            Graphics.Blit(source, destination);
            Camera component2 = GetComponent<Camera>();
            if (component2 == null)
            {
                return;
            }
            float currentMsec = NotesManager.GetCurrentMsec();
            foreach (KeyValuePair<GameMonitor, Frame> frame2 in frames)
            {
                Frame value = frame2.Value;
                if (frame2.Key == null || value.Bounds == null || !value.Bounds.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (value.ScreenEffects != null)
                {
                    PresentationRect presentationRect = Bounds(value, component2);
                    PresentationRect visible = presentationRect.Intersect(new PresentationRect(0f, 0f, 1f, 1f));
                    PresentationRect playfield = ((value.Playfield != null) ? Bounds(value.Playfield, value.Corners, component2) : presentationRect);
                    if (value.ScreenEffects.TryDraw(source, destination, presentationRect, visible, playfield, currentMsec))
                    {
                        continue;
                    }
                }
                PresentationChange source2 = null;
                PresentationChange source3 = null;
                float num = value.Track?.Evaluate(currentMsec, out source2) ?? 0f;
                bool flag2 = source2 != null && Math.Abs(num) > 0.0001f;
                float num2 = Math.Max(-1f, Math.Min(1f, value.Flash?.Evaluate(currentMsec) ?? 0f));
                float num3 = Math.Max(0f, Math.Min(1f, value.Tint?.Evaluate(currentMsec, out source3) ?? 0f));
                if (source3 == null || string.IsNullOrEmpty(source3.ColorHex))
                {
                    num3 = 0f;
                }
                if (!flag2 && Math.Abs(num2) <= 0.001f && num3 <= 0.001f)
                {
                    continue;
                }
                PresentationRect frame = Bounds(value, component2);
                if (!frame.HasArea)
                {
                    continue;
                }
                PresentationRect presentationRect2 = frame.Intersect(new PresentationRect(0f, 0f, 1f, 1f));
                if (!presentationRect2.HasArea)
                {
                    continue;
                }
                float num4 = 0f;
                float y = 0f;
                if (flag2)
                {
                    float num5 = currentMsec * 0.001f * ((source2.Frequency > 0f) ? source2.Frequency : 18f);
                    num4 = (Mathf.PerlinNoise(num5, 0.37f) - 0.5f) * 2f * num * 0.05f;
                    y = (source2.HasDirection ? (Mathf.Sin(source2.Direction) * num4) : ((Mathf.PerlinNoise(0.71f, num5) - 0.5f) * 2f * num * 0.05f));
                    if (source2.HasDirection)
                    {
                        num4 *= Mathf.Cos(source2.Direction);
                    }
                }
                if (!flag)
                {
                    Graphics.SetRenderTarget(destination);
                    GL.PushMatrix();
                    flag = true;
                    GL.LoadPixelMatrix(0f, source.width, source.height, 0f);
                }
                PresentationRect destination2 = presentationRect2;
                if (flag2)
                {
                    Graphics.DrawTexture(Pixels(presentationRect2, source.width, source.height), Texture2D.blackTexture);
                    if (!PresentationViewportMath.Shift(frame, num4, y, out destination2, out var source4))
                    {
                        continue;
                    }
                    Graphics.DrawTexture(Pixels(destination2, source.width, source.height), source, new Rect(source4.X, source4.Y, source4.Width, source4.Height), 0, 0, 0, 0);
                }
                if (num2 != 0f)
                {
                    DrawColor(destination2, source, new Color((num2 > 0f) ? 1 : 0, (num2 > 0f) ? 1 : 0, (num2 > 0f) ? 1 : 0, Math.Abs(num2)));
                }
                if (num3 > 0f)
                {
                    if (value.TintSource != source3)
                    {
                        int num6 = int.Parse(source3.ColorHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        value.TintColor = new Color((float)((num6 >> 16) & 0xFF) / 255f, (float)((num6 >> 8) & 0xFF) / 255f, (float)(num6 & 0xFF) / 255f);
                        value.TintSource = source3;
                    }
                    Color tintColor = value.TintColor;
                    tintColor.a = num3;
                    DrawColor(destination2, source, tintColor);
                }
            }
            if (flag)
            {
                GL.PopMatrix();
                flag = false;
            }
            RenderTexture renderTexture3 = null;
            bool flag3 = false;
            try
            {
                foreach (KeyValuePair<GameMonitor, Frame> frame3 in frames)
                {
                    Frame value2 = frame3.Value;
                    if (frame3.Key == null || value2.Noise == null || value2.Playfield == null || value2.Bounds == null || !value2.Bounds.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    // TryDraw would return false at its first line without side effects (no zone visible, field
                    // not captured, other camera), so skip the full-screen copy into renderTexture3 that only
                    // feeds it. The Bounds calls skipped with it only fill this frame's scratch corners.
                    if (!value2.Noise.CanDraw(component2))
                    {
                        flag3 |= value2.Noise.Ready;
                        // Once per noise state, from the song-entry Init frame on: create the noise targets with
                        // the size and format TryDraw would use (renderTexture3 has source's), instead of at the
                        // first visible zone mid-song. Same Bounds/area tests as below.
                        if (value2.Noise.NeedsTargets)
                        {
                            PresentationRect presentationRect3 = Bounds(value2, component2);
                            if (presentationRect3.HasArea && presentationRect3.Intersect(new PresentationRect(0f, 0f, 1f, 1f)).HasArea)
                            {
                                value2.Noise.PrepareTargets(source, presentationRect3);
                            }
                        }
                        continue;
                    }
                    PresentationRect bounds = Bounds(value2, component2);
                    PresentationRect visible2 = bounds.Intersect(new PresentationRect(0f, 0f, 1f, 1f));
                    // destination is never null here (CanDraw implies rt2 was taken); the test only keeps a
                    // mispredicted frame from reading the backbuffer.
                    if (bounds.HasArea && visible2.HasArea && destination != null)
                    {
                        // TryDraw reads its source only in its first draw (into its own input), before it binds
                        // the destination, so rt2 can be both: the same texels as the full-screen copy into
                        // renderTexture3 that fed it. A destination Unity passed in is still copied first.
                        RenderTexture renderTexture6 = destination;
                        if (destination != renderTexture2)
                        {
                            if (renderTexture3 == null)
                            {
                                renderTexture3 = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear);
                            }
                            Graphics.Blit(destination, renderTexture3);
                            renderTexture6 = renderTexture3;
                        }
                        PresentationRect playfield2 = Bounds(value2.Playfield, value2.Corners, component2);
                        value2.Noise.TryDraw(renderTexture6, destination, component2, bounds, visible2, playfield2);
                    }
                }
                // Upstream took renderTexture3 on every frame of a noise chart; skipping it between zones let the
                // pool destroy that second full-screen temporary (UnityPlayer RenderBufferManager::GarbageCollect(15)
                // runs every frame and frees a temporary unused for more than 15 frames), so each zone onset
                // re-created it. Taking and returning one with the same descriptor while renderTexture2 is still
                // held keeps it pooled, as upstream did, without the copy. Nothing is drawn into it. With rt2 (or its
                // stand-in renderTexture5) the zones read rt2 itself, so renderTexture3 is not needed at all.
                if (renderTexture3 == null && flag3 && renderTexture2 == null && renderTexture5 == null)
                {
                    RenderTexture.ReleaseTemporary(RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear));
                }
            }
            finally
            {
                if (renderTexture3 != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture3);
                }
            }
        }
        finally
        {
            if (flag)
            {
                GL.PopMatrix();
            }
            try
            {
                if (renderTexture2 != null)
                {
                    Graphics.Blit(renderTexture2, dest);
                }
            }
            finally
            {
                if (renderTexture2 != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture2);
                }
                if (renderTexture5 != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture5);
                }
                RenderTexture.active = active;
                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
                if (renderTexture4 != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture4);
                }
            }
        }
    }


    private bool AnyNoiseReady()
    {
        foreach (Frame value in frames.Values)
        {
            if (value.Noise != null && value.Noise.Ready)
            {
                return true;
            }
        }
        return false;
    }

    private bool DrawsAny(Camera camera, float currentMsec)
    {
        if (camera == null)
        {
            return false;
        }
        foreach (KeyValuePair<GameMonitor, Frame> frame in frames)
        {
            Frame value = frame.Value;
            if (frame.Key == null || value.Bounds == null || !value.Bounds.gameObject.activeInHierarchy)
            {
                continue;
            }
            if (value.ScreenEffects != null && value.ScreenEffects.WillDraw(currentMsec))
            {
                return true;
            }
            PresentationChange source = null;
            PresentationChange source2 = null;
            float num = value.Track?.Evaluate(currentMsec, out source) ?? 0f;
            bool flag = source != null && Math.Abs(num) > 0.0001f;
            float num2 = Math.Max(-1f, Math.Min(1f, value.Flash?.Evaluate(currentMsec) ?? 0f));
            float num3 = Math.Max(0f, Math.Min(1f, value.Tint?.Evaluate(currentMsec, out source2) ?? 0f));
            if (source2 == null || string.IsNullOrEmpty(source2.ColorHex))
            {
                num3 = 0f;
            }
            if (!(!flag && Math.Abs(num2) <= 0.001f && num3 <= 0.001f))
            {
                return true;
            }
            if (value.Noise != null && value.Playfield != null && value.Noise.CanDraw(camera))
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class ReferenceSideStatistics : MonoBehaviour
{
    private Text judge, combo;
    private int monitor;
    internal void Configure(int monitorIndex, CanvasGroup parent)
    {
        monitor = monitorIndex;
        var font = parent.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.font != null)?.font;
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        Text Make(string name, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(transform, false);
            var text = go.GetComponent<Text>(); text.font = font; text.fontSize = 22; text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft; text.raycastTarget = false;
            var rect = text.rectTransform; rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(220, 160);
            return text;
        }
        var root = (RectTransform)transform; root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        judge = Make("JudgeInfo", new Vector2(.02f, .9f)); combo = Make("ComboInfo", new Vector2(.76f, .9f));
    }
    internal void Apply(float judgeAlpha, float comboAlpha)
    {
        judge.canvasRenderer.SetAlpha(judgeAlpha); combo.canvasRenderer.SetAlpha(comboAlpha);
        if (judgeAlpha <= 0 && comboAlpha <= 0) return;
        var score = MAI2.Util.Singleton<GamePlayManager>.Instance.GetGameScore(monitor);
        if (judgeAlpha > 0) judge.text = "CRITICAL " + score.GetCriticalNum() + "\nPERFECT " + score.GetPerfectNum() +
            "\nGREAT " + score.GetGreatNum() + "\nGOOD " + score.GetGoodNum() + "\nMISS " + score.GetMissNum();
        if (comboAlpha > 0) combo.text = "COMBO " + score.Combo + "\n" + score.GetAchivement().ToString("F4") + "%";
    }
}
