using SinmaiAlpha.Notes.Libs;
using System;
using System.Collections.Generic;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Hosting;
using HarmonyLib;
using Manager;
using Monitor;
using Monitor.Game;
using UnityEngine;
using UnityEngine.UI;

namespace SinmaiAlpha.Notes;

// Canvas meshes must be moved before Unity batches them. Sprite/mesh poses
// exist only during camera rendering, so native gameplay sees its own poses.
[DefaultExecutionOrder(10000)]
public sealed partial class ChartFrameMotion : MonoBehaviour
{
    private sealed class Pose
    {
        public Transform Target;
        public Vector3 Position, Scale, WorldPosition;
        public Quaternion Rotation, WorldRotation;
        public Vector3 AppliedPosition, AppliedScale;
        public Quaternion AppliedRotation;
        public bool SkipRotation;
        public bool PV;
        public bool PositionApplied, ScaleApplied, RotationApplied;

        public Pose() { }
        public void Capture(Transform target, bool skipRotation, bool pv)
        {
            PositionApplied = ScaleApplied = RotationApplied = false;
            Target = target; SkipRotation = skipRotation; PV = pv;
            Position = target.localPosition; Scale = target.localScale; Rotation = target.localRotation;
            WorldPosition = target.position; WorldRotation = target.rotation;
        }
        public void Restore(bool leased)
        {
            if (Target == null) return;
            // UI animators may have written a newer pose since LateUpdate.
            // Restore only components still owned by the presentation lease.
            if (!leased || PositionApplied && Target.localPosition.Equals(AppliedPosition)) Target.localPosition = Position;
            if (!leased || ScaleApplied && Target.localScale.Equals(AppliedScale)) Target.localScale = Scale;
            if (!leased || RotationApplied && Target.localRotation.Equals(AppliedRotation)) Target.localRotation = Rotation;
        }
        public void Apply(Frame frame, bool transformedAncestor)
        {
            var rotation = SkipRotation ? Quaternion.identity : Quaternion.Euler(0, 0, PV ? frame.PVDegrees : frame.Degrees);
            var relative = WorldPosition - frame.Center;
            var planar = rotation * new Vector3(relative.x * frame.Scale, relative.y * frame.Scale, 0);
            Target.position = new Vector3(frame.Center.x + planar.x + frame.Offset.x,
                frame.Center.y + planar.y + frame.Offset.y, WorldPosition.z);
            AppliedPosition = Target.localPosition; PositionApplied = true;
            Target.rotation = rotation * WorldRotation;
            AppliedRotation = Target.localRotation; RotationApplied = true;
            // Different rotation policies can share an ancestor (the native
            // movie is a child of its black cover). Scale such a child once.
            Target.localScale = transformedAncestor ? Scale : Scale * frame.Scale;
            AppliedScale = Target.localScale; ScaleApplied = true;
        }
    }
    private sealed class Target
    {
        public Transform Transform;
        public bool SkipRotation;
        public bool PV;
        public int Depth;
    }
    private sealed class Frame
    {
        public GameMonitor Owner;
        public NotesReader Reader;
        public RectTransform Main, Bounds;
        public GameCtrl Controller;
        public PresentationCommands.Track Move, Rotate, Zoom;
        public CustomNoteTypes.NoiseRenderState Noise;
        public readonly List<Transform> Covers = new();
        public readonly List<Pose> CanvasPoses = new(), WorldPoses = new();
        public readonly List<Target> Targets = new();
        public readonly Vector3[] MainCorners = new Vector3[4], FrameCorners = new Vector3[4];
        public Vector3 Center, Offset;
        public float Degrees, PVDegrees, Scale = 1;
        public bool Active;
        private readonly Dictionary<Transform, Target> targetIndex = new();
        private readonly Dictionary<Transform, int> descendantCounts = new();
        private readonly HashSet<Transform> appliedAncestors = new();
        private readonly Stack<Target> spareTargets = new();
        private readonly Stack<Pose> sparePoses = new();

        private void RestorePoses(List<Pose> poses, bool leased)
        {
            for (var i = poses.Count - 1; i >= 0; i--)
            { poses[i].Restore(leased); poses[i].Target = null; sparePoses.Push(poses[i]); }
            poses.Clear();
        }
        private void CountAncestors(Transform target, int delta)
        {
            for (var parent = target.parent; parent != null; parent = parent.parent)
            {
                descendantCounts.TryGetValue(parent, out var count);
                if (count + delta == 0) descendantCounts.Remove(parent);
                else descendantCounts[parent] = count + delta;
            }
        }
        public void ClearTargets()
        {
            foreach (var target in Targets) { target.Transform = null; spareTargets.Push(target); }
            Targets.Clear(); targetIndex.Clear(); descendantCounts.Clear(); appliedAncestors.Clear();
        }

        public void RestoreCanvas()
        { RestorePoses(CanvasPoses, true); }
        public void RestoreWorld()
        { RestorePoses(WorldPoses, false); }
        public bool Evaluate()
        {
            if (Owner == null || Main == null || Bounds == null || Controller == null ||
                !Main.gameObject.activeInHierarchy || GuiSizes.SinglePlayer && Owner.MonitorIndex != 0) return Active = false;
            var now = NotesManager.GetCurrentMsec();
            Degrees = Rotate?.Evaluate(now) ?? 0;
            PVDegrees = Rotate?.EvaluatePVRotation(now) ?? 0;
            Scale = Mathf.Clamp(1 + (Zoom?.Evaluate(now) ?? 0), .1f, 8);
            var x = 0f; var y = 0f; Move?.EvaluateVector(now, out x, out y);
            Main.GetWorldCorners(MainCorners); Bounds.GetWorldCorners(FrameCorners);
            Center = (MainCorners[0] + MainCorners[2]) * .5f;
            var offset = (FrameCorners[3] - FrameCorners[0]) * x + (FrameCorners[1] - FrameCorners[0]) * y;
            Offset = new Vector3(offset.x, offset.y, 0);
            return Active = Math.Abs(Degrees) > .0001f || Math.Abs(PVDegrees) > .0001f || Math.Abs(Scale - 1) > .0001f ||
                Math.Abs(Offset.x) > .0001f || Math.Abs(Offset.y) > .0001f;
        }
        public void Add(Transform transform, bool skipRotation, bool pv = false)
        {
            if (transform == null) return;
            if (targetIndex.ContainsKey(transform)) return;
            var depth = 0;
            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                depth++;
                if (targetIndex.TryGetValue(parent, out var ancestor) && ancestor.SkipRotation == skipRotation && ancestor.PV == pv) return;
            }
            // Only an ancestor of an accepted target can replace descendants.
            // Sibling renderers in the native pools take the short path above.
            if (descendantCounts.ContainsKey(transform))
                for (var i = Targets.Count - 1; i >= 0; i--)
                    if (Targets[i].SkipRotation == skipRotation && Targets[i].PV == pv && Targets[i].Transform.IsChildOf(transform))
                    {
                        var removed = Targets[i]; CountAncestors(removed.Transform, -1);
                        targetIndex.Remove(removed.Transform); Targets.RemoveAt(i);
                        removed.Transform = null; spareTargets.Push(removed);
                    }
            var target = spareTargets.Count == 0 ? new Target() : spareTargets.Pop();
            target.Transform = transform; target.SkipRotation = skipRotation; target.PV = pv; target.Depth = depth;
            Targets.Add(target); targetIndex.Add(transform, target); CountAncestors(transform, 1);
        }
        public void Apply(List<Pose> poses)
        {
            Targets.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            // Capture all native poses before moving any parent. Reuse records
            // so a long zoom/rotation does not allocate per pooled renderer.
            foreach (var target in Targets)
            {
                var pose = sparePoses.Count == 0 ? new Pose() : sparePoses.Pop();
                pose.Capture(target.Transform, target.SkipRotation, target.PV); poses.Add(pose);
            }
            appliedAncestors.Clear();
            foreach (var pose in poses)
            {
                var inherited = false;
                for (var parent = pose.Target.parent; parent != null; parent = parent.parent)
                    if (appliedAncestors.Contains(parent)) { inherited = true; break; }
                pose.Apply(this, inherited); appliedAncestors.Add(pose.Target);
            }
            ClearTargets();
        }


        public bool CanMove
        {
            get
            {
                if ((Move != null || Rotate != null || Zoom != null) && Owner != null && Main != null && Bounds != null && Controller != null && Main.gameObject.activeInHierarchy)
                {
                    if (GuiSizes.SinglePlayer)
                    {
                        return Owner.MonitorIndex == 0;
                    }
                    return true;
                }
                return false;
            }
        }
    }

    private readonly Dictionary<GameMonitor, Frame> frames = new();
    private readonly List<GameMonitor> stale = new();

    private readonly List<Graphic> visibleGraphics = new();
    // Pool objects which are inactive or renderer-disabled cannot contribute
    // pixels. They must retain their native poses, not be transformed/restored.
    internal static void CollectVisibleRenderers(GameObject root, List<Renderer> result)
    {
        result.Clear();
        if (root == null || !root.activeInHierarchy) return;
        root.GetComponentsInChildren(false, result);
        for (var i = result.Count - 1; i >= 0; i--)
            if (result[i] == null || !result[i].enabled) result.RemoveAt(i);
    }
    private void CollectVisibleGraphics(Canvas canvas)
    {
        visibleGraphics.Clear();
        if (canvas != null) canvas.GetComponentsInChildren(false, visibleGraphics);
    }
    internal void Bind(GameMonitor owner, NotesReader reader, GameCtrl controller, RectTransform main, RectTransform bounds, PresentationCommands.Track move, PresentationCommands.Track rotate, PresentationCommands.Track zoom, List<Transform> covers)
    {
        if (owner == null)
        {
            return;
        }
        if (!frames.TryGetValue(owner, out var value))
        {
            // The active-frame paths (poses, leases, crop, composite) used to be compiled on the first MOVE/ROTATE/ZOOM
            // frame of the session, mid-song (about 5 ms of JIT on the game's Mono). Compile them here, at chart load
            // (see JitWarmup). The type initialisers this can run early (this class's PropertyToID pair and collections,
            // the Sort lambda cache; NativeRefs already ran in WarmFieldRefs) are pure and would give the same result at
            // their first use.
            JitWarmup.Compile(typeof(ChartFrameMotion), typeof(FrameViewportCrop), typeof(PresentationRect));
            Dictionary<GameMonitor, Frame> dictionary = frames;
            Frame frame = new Frame
            {
                Owner = owner
            };
            value = frame;
            dictionary[owner] = frame;
        }
        if (value.Reader != reader || value.Controller != controller || value.Main != main || value.Bounds != bounds)
        {
            RestoreIsolation(releaseTextures: true);
            value.RestoreCanvas();
            value.RestoreWorld();
            isolationSurfacesDirty = true;
        }
        value.Reader = reader;
        value.Controller = controller;
        value.Main = main;
        value.Bounds = bounds;
        value.Move = move;
        value.Rotate = rotate;
        value.Zoom = zoom;
        value.Covers.Clear();
        if (covers != null)
        {
            value.Covers.AddRange(covers);
        }
        if (isolationSurfacesDirty || isolationLayers.Count == 0)
        {
            DiscoverSurfaces();
            if (isolationSurfaces.Count != 0)
            {
                ReserveLayers(isolationSurfaces.Count);
            }
        }
    }
    internal void BeforeNativeUpdate(GameMonitor owner)
    {
        // Also catches interrupted rendering (disabled/destroyed cameras).
        RestoreIsolation(true);
        foreach (var frame in frames.Values) frame.RestoreWorld();
        if (frames.TryGetValue(owner, out var own)) own.RestoreCanvas();
    }
    internal void BindNoise(GameMonitor owner, CustomNoteTypes.NoiseRenderState noise)
    { if (frames.TryGetValue(owner, out var frame)) frame.Noise = noise; }
    internal void Unbind(GameMonitor owner)
    { if (!frames.TryGetValue(owner, out var frame)) return; frame.Noise?.CaptureField(null); RestoreIsolation(true); frame.RestoreWorld(); frame.RestoreCanvas(); frames.Remove(owner); isolationSurfacesDirty = true; if (frames.Count == 0) ReleaseIsolation(); }
    internal void ResetReader(NotesReader reader)
    {
        stale.Clear(); foreach (var pair in frames) if (pair.Value.Reader == reader) stale.Add(pair.Key);
        foreach (var owner in stale) Unbind(owner); stale.Clear();
    }
    internal void Clear()
    { RestoreIsolation(true); ReleaseIsolation(); foreach (var frame in frames.Values) { frame.Noise?.CaptureField(null); frame.RestoreWorld(); frame.RestoreCanvas(); } frames.Clear(); }
    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
    private void LateUpdate()
    {
        try
        {
            foreach (var frame in frames.Values)
            {
                frame.RestoreCanvas();
                if (!frame.Evaluate()) continue;
                var canvas = frame.Main.GetComponentInParent<Canvas>();
                CollectVisibleGraphics(canvas);
                if (canvas != null)
                    foreach (var graphic in visibleGraphics)
                        if (graphic != null && graphic.enabled && !graphic.transform.IsChildOf(frame.Controller.transform) &&
                            !frame.Controller.transform.IsChildOf(graphic.transform)) frame.Add(graphic.transform, true);
                foreach (var cover in frame.Covers) if (cover != null && cover.GetComponent<Graphic>() != null) frame.Add(cover, true);
                frame.Apply(frame.CanvasPoses);
            }
        }
        catch { foreach (var frame in frames.Values) { frame.RestoreCanvas(); frame.ClearTargets(); } throw; }
    }
    private void OnPreCull()
    {
        ForgetWalks();
        RestoreIsolation(true);
        foreach (var frame in frames.Values) frame.RestoreWorld();
        try
        {
            foreach (var frame in frames.Values)
            {
                if (!frame.Evaluate()) continue;
                var cover = Traverse.Create(frame.Controller).Field("_movieMaskSprite").GetValue<SpriteRenderer>();
                var movie = Traverse.Create(frame.Controller).Field("_movieSprite").GetValue<SpriteRenderer>();
                var movieRoot = movie != null && movie.transform.parent != null && movie.transform.parent.name == "ChartMediaFrame" ? movie.transform.parent : null;
                foreach (var renderer in WalkController(frame.Controller.gameObject))
                    if (renderer != null) frame.Add(renderer.transform, cover != null && renderer.transform == cover.transform,
                        (renderer == movie || movieRoot != null && renderer.transform.IsChildOf(movieRoot)) && renderer != cover);
                foreach (var target in frame.Covers) if (target != null && target.GetComponent<Graphic>() == null) frame.Add(target, true);
                frame.Apply(frame.WorldPoses);
            }
            var renderCamera = GetComponent<Camera>();
            // Snapshot after MOVE/ROTATE/ZOOM, before OnPostRender restores
            // native poses. The later noise pass uses this exact field matrix.
            foreach (var frame in frames.Values)
                if (frame.Main != null && frame.Main.gameObject.activeInHierarchy && (!GuiSizes.SinglePlayer || frame.Owner.MonitorIndex == 0))
                    frame.Noise?.CaptureField(renderCamera);
            if (!PrepareIsolation())
                foreach (var frame in frames.Values) { frame.RestoreWorld(); frame.RestoreCanvas(); }
        }
        catch { RestoreIsolation(true); foreach (var frame in frames.Values) { frame.RestoreWorld(); frame.RestoreCanvas(); frame.ClearTargets(); } throw; }
        finally { ForgetWalks(); }
    }
    private void OnPostRender()
    { RestoreIsolation(false); foreach (var frame in frames.Values) frame.RestoreWorld(); }


    internal static class NativeRefs
    {
        internal static readonly AccessTools.FieldRef<CommonMonitor, MonitorBackgroundTownController> BackgroundController = TryFieldRef<CommonMonitor, MonitorBackgroundTownController>("_backgroundController");

        internal static readonly AccessTools.FieldRef<MonitorBackgroundTownController, GameObject> BgObject = TryFieldRef<MonitorBackgroundTownController, GameObject>("_bgObject");

        internal static readonly AccessTools.FieldRef<GameCtrl, SpriteRenderer> MovieMaskSprite = TryFieldRef<GameCtrl, SpriteRenderer>("_movieMaskSprite");

        private static AccessTools.FieldRef<T, F> TryFieldRef<T, F>(string name) where T : class
        {
            try
            {
                return AccessTools.FieldRefAccess<T, F>(name);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private static readonly CommonMonitor[] BackgroundOwners = new CommonMonitor[2];

    private readonly List<Renderer> screenClipRenderers = new List<Renderer>();

    private readonly List<int> pooledIsolationTextures = new List<int>();

    private readonly List<RenderTexture> pooledIsolationScratch = new List<RenderTexture>();

    private bool isolationPrimed;

    private readonly List<GameObject> walkedRoots = new List<GameObject>();

    private readonly List<List<Renderer>> walkedRenderers = new List<List<Renderer>>();

    internal bool KeepsIsolationTexturesPooled
    {
        get
        {
            if (isolatedFrames.Count == 0)
            {
                return pooledIsolationTextures.Count != 0;
            }
            return false;
        }
    }

    internal static GameObject NativeBackgroundObject(MonitorBackgroundTownController controller)
    {
        if ((object)controller == null)
        {
            return null;
        }
        AccessTools.FieldRef<MonitorBackgroundTownController, GameObject> bgObject = NativeRefs.BgObject;
        return (bgObject != null) ? bgObject(controller) : Traverse.Create(controller).Field("_bgObject").GetValue<GameObject>();
    }

    private void CollectScreenClipRenderers(GameObject background)
    {
        screenClipRenderers.Clear();
        if (background == null)
        {
            return;
        }
        Renderer[] componentsInChildren = background.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (Renderer renderer in componentsInChildren)
        {
            if (!(renderer == null) && !(renderer.GetComponent("ScreenClipSetter") == null))
            {
                screenClipRenderers.Add(renderer);
            }
        }
    }

    private void LeaseScreenClips(PresentationRect viewport)
    {
        foreach (Renderer renderer in screenClipRenderers)
        {
            bool flag = false;
            foreach (ScreenClipLease screenClipLease2 in screenClipLeases)
            {
                if (screenClipLease2.Owner == renderer)
                {
                    flag = true;
                    break;
                }
            }
            if (!flag)
            {
                renderer.GetPropertyBlock(screenClipProperties);
                float num = screenClipProperties.GetFloat(ClipMinX);
                float num2 = screenClipProperties.GetFloat(ClipMaxX);
                if (!float.IsNaN(num) && !float.IsNaN(num2) && !float.IsInfinity(num) && !float.IsInfinity(num2) && !(num2 <= num))
                {
                    ScreenClipLease screenClipLease = new ScreenClipLease
                    {
                        Owner = renderer,
                        Min = num,
                        Max = num2,
                        AppliedMin = (num - viewport.X) / viewport.Width,
                        AppliedMax = (num2 - viewport.X) / viewport.Width
                    };
                    screenClipLeases.Add(screenClipLease);
                    screenClipProperties.SetFloat(ClipMinX, screenClipLease.AppliedMin);
                    screenClipProperties.SetFloat(ClipMaxX, screenClipLease.AppliedMax);
                    renderer.SetPropertyBlock(screenClipProperties);
                }
            }
        }
    }

    private bool TryIsolationCrop(PresentationRect bounds, int width, int height, out FrameViewportCrop crop)
    {
        foreach (Renderer screenClipRenderer in screenClipRenderers)
        {
            screenClipRenderer.GetPropertyBlock(screenClipProperties);
            float num = screenClipProperties.GetFloat(ClipMinX);
            float num2 = screenClipProperties.GetFloat(ClipMaxX);
            if (!float.IsNaN(num) && !float.IsNaN(num2) && !float.IsInfinity(num) && !float.IsInfinity(num2) && !(num2 <= num) && (!ClipPixelAligned(num, width) || !ClipPixelAligned(num2, width)))
            {
                return FrameViewportCrop.TryCreateFull(bounds, width, height, out crop);
            }
        }
        return FrameViewportCrop.TryCreate(bounds, width, height, out crop);
    }

    private List<Renderer> WalkController(GameObject root)
    {
        int count = walkedRoots.Count;
        if (count == walkedRenderers.Count)
        {
            walkedRenderers.Add(new List<Renderer>());
        }
        List<Renderer> list = walkedRenderers[count];
        CollectVisibleRenderers(root, list);
        walkedRoots.Add(root);
        return list;
    }

    private void ForgetWalks()
    {
        walkedRoots.Clear();
        foreach (List<Renderer> walkedRenderer in walkedRenderers)
        {
            walkedRenderer.Clear();
        }
    }

    private void KeepIsolationTexturesPooled()
    {
        if (pooledIsolationTextures.Count == 0)
        {
            return;
        }
        pooledIsolationScratch.Clear();
        try
        {
            for (int i = 0; i + 2 < pooledIsolationTextures.Count; i += 3)
            {
                pooledIsolationScratch.Add(RenderTexture.GetTemporary(pooledIsolationTextures[i], pooledIsolationTextures[i + 1], 24, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Default, pooledIsolationTextures[i + 2]));
            }
        }
        finally
        {
            foreach (RenderTexture item in pooledIsolationScratch)
            {
                if (item != null)
                {
                    RenderTexture.ReleaseTemporary(item);
                }
            }
            pooledIsolationScratch.Clear();
        }
    }

    private void PrimeIsolationTextures()
    {
        if (GameManager.IsAdvDemo || isolationPrimed || pooledIsolationTextures.Count != 0 || isolationSurfacesDirty || renderingIsolation)
        {
            return;
        }
        try
        {
            // AdvDemo has no chart-release cleanup; impossible or not-yet-ready surfaces need no prime.
            if (isolationLayers.Count < isolationSurfaces.Count)
            {
                return;
            }
            bool canMove = false;
            foreach (Frame frame in frames.Values)
            {
                canMove |= frame.CanMove;
            }
            if (!canMove)
            {
                return;
            }
            foreach (IsolationSurface surface in isolationSurfaces)
            {
                if (surface.Canvas == null || surface.Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    return;
                }
            }
            Camera component = GetComponent<Camera>();
            if (component == null)
            {
                return;
            }
            foreach (IsolationSurface isolationSurface in isolationSurfaces)
            {
                if (!frames.TryGetValue(isolationSurface.Owner, out var value) || !value.CanMove)
                {
                    continue;
                }
                PresentationRect bounds = IsolationBounds(value.Bounds, component);
                if (!bounds.HasArea)
                {
                    continue;
                }
                int num = ((component.targetTexture != null) ? component.targetTexture.width : component.pixelWidth);
                int num2 = ((component.targetTexture != null) ? component.targetTexture.height : component.pixelHeight);
                if (num <= 0 || num2 <= 0)
                {
                    break;
                }
                CollectScreenClipRenderers((isolationSurface.BackgroundController != null) ? NativeBackgroundObject(isolationSurface.BackgroundController) : null);
                if (TryIsolationCrop(bounds, num, num2, out var crop))
                {
                    int item = ((component.targetTexture != null) ? component.targetTexture.antiAliasing : ((!component.allowMSAA) ? 1 : Math.Max(1, QualitySettings.antiAliasing)));
                    pooledIsolationTextures.Add(crop.Width);
                    pooledIsolationTextures.Add(crop.Height);
                    pooledIsolationTextures.Add(item);
                    isolationPrimed = true;
                }
            }
            // The isolation camera, created as PrepareIsolation creates it. It stays disabled and HideAndDontSave, nothing
            // enumerates cameras (game, MaiMagic, Alpha), and every active frame reconfigures it (CopyFrom and the rest)
            // before its Render; only its creation moves out of the onset frame. Created inside a render (OnPreCull), it
            // never joins the camera list, here as at the onset: UnityPlayer 2018.4 defers camera adds and removes made
            // while rendering (RenderManager::AddRemoveCamerasDelayed), so the add and the disable cancel out.
            if (pooledIsolationTextures.Count != 0 && isolationCamera == null)
            {
                GameObject gameObject = new GameObject("ChartFrameIsolationCamera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                isolationCamera = gameObject.AddComponent<Camera>();
                isolationCamera.enabled = false;
            }
        }
        catch (Exception)
        {
            pooledIsolationTextures.Clear();
            isolationPrimed = true;
        }
        finally
        {
            screenClipRenderers.Clear();
        }
    }
}
