using System;
using System.Collections.Generic;
using SinmaiAlpha.Hosting;
using SinmaiAlpha.Notes.Libs;
using HarmonyLib;
using MelonLoader;
using Monitor;
using Monitor.Game;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes;

// A shared arcade camera must not let one player's moved frame cover the other.
// Render the authored frame with the native projection and materials, then copy
// only its original viewport. Layers and camera masks are render-time leases;
// native input objects, queues, clocks and judge results are never replaced.
public sealed partial class ChartFrameMotion
{
    private sealed class IsolationSurface
    {
        public GameMonitor Owner;
        public GameCtrl Controller;
        public Canvas Canvas;
        public MonitorBackgroundTownController BackgroundController;
        public GameObject Background;
        public int Layer;
    }
    private sealed class LayerLease
    {
        public GameObject Owner;
        public int Original, Applied;
    }
    private sealed class IsolatedFrame
    {
        public RenderTexture Texture;
        public PresentationRect Bounds;
        public PresentationRect TextureUv;
    }
    private sealed class ScreenClipLease
    {
        public Renderer Owner;
        public float Min, Max, AppliedMin, AppliedMax;
    }
    private static readonly int ClipMinX = Shader.PropertyToID("_ClipMinX");
    private static readonly int ClipMaxX = Shader.PropertyToID("_ClipMaxX");
    private static readonly HashSet<int> ReservedIsolationLayers = new();
    private readonly List<int> isolationLayers = new();
    private readonly List<IsolationSurface> isolationSurfaces = new();
    private readonly Dictionary<GameObject, LayerLease> isolationLeases = new();
    private readonly List<IsolatedFrame> isolatedFrames = new();
    private readonly List<ScreenClipLease> screenClipLeases = new();
    private readonly List<Renderer> isolationRenderers = new();
    private readonly List<Canvas> isolationCanvases = new();
    private readonly Stack<LayerLease> spareLayerLeases = new();
    private readonly MaterialPropertyBlock screenClipProperties = new();
    private readonly Vector3[] isolationCorners = new Vector3[4];
    private Camera isolationCamera, leasedSourceCamera;
    private int originalCameraMask, appliedCameraMask;
    private bool isolationSurfacesDirty = true, renderingIsolation, isolationWarning;

    private bool ReserveLayers(int count)
    {
        if (isolationLayers.Count >= count) return true;
        var used = new HashSet<int>(ReservedIsolationLayers);
        foreach (var obj in Resources.FindObjectsOfTypeAll<GameObject>()) if (obj != null) used.Add(obj.layer);
        var available = new List<int>();
        for (var layer = 31; layer >= 8 && available.Count + isolationLayers.Count < count; layer--)
            if (!used.Contains(layer) && string.IsNullOrEmpty(LayerMask.LayerToName(layer))) available.Add(layer);
        if (available.Count + isolationLayers.Count < count) return false;
        foreach (var layer in available) { isolationLayers.Add(layer); ReservedIsolationLayers.Add(layer); }
        return true;
    }

    internal static MonitorBackgroundTownController NativeBackgroundFor(GameMonitor owner)
    {
        if (owner == null)
        {
            return null;
        }
        int monitorIndex = owner.MonitorIndex;
        bool flag = monitorIndex >= 0 && monitorIndex < BackgroundOwners.Length;
        CommonMonitor commonMonitor = (flag ? BackgroundOwners[monitorIndex] : null);
        if (commonMonitor == null || !commonMonitor.isActiveAndEnabled || commonMonitor.MonitorIndex != monitorIndex)
        {
            commonMonitor = null;
            CommonMonitor[] array = UnityEngine.Object.FindObjectsOfType<CommonMonitor>();
            foreach (CommonMonitor commonMonitor2 in array)
            {
                if (commonMonitor2 != null && commonMonitor2.MonitorIndex == monitorIndex)
                {
                    commonMonitor = commonMonitor2;
                    break;
                }
            }
            if (flag)
            {
                BackgroundOwners[monitorIndex] = commonMonitor;
            }
            if (commonMonitor == null)
            {
                return null;
            }
        }
        AccessTools.FieldRef<CommonMonitor, MonitorBackgroundTownController> backgroundController = NativeRefs.BackgroundController;
        return (backgroundController != null) ? backgroundController(commonMonitor) : Traverse.Create(commonMonitor).Field("_backgroundController").GetValue<MonitorBackgroundTownController>();
    }
    private void DiscoverSurfaces()
    {
        if (!isolationSurfacesDirty) return;
        isolationSurfaces.Clear();
        foreach (var owner in Object.FindObjectsOfType<GameMonitor>())
        {
            if (owner == null || GuiSizes.SinglePlayer && owner.MonitorIndex != 0) continue;
            var main = Traverse.Create(owner).Field("Main").GetValue<CanvasGroup>();
            var controller = Traverse.Create(owner).Field("GameController").GetValue<GameCtrl>();
            var canvas = main != null ? main.GetComponentInParent<Canvas>() : null;
            if (main == null || controller == null || canvas == null || !main.gameObject.activeInHierarchy) continue;
            // CommonProcess is a separate native monitor hierarchy. Its index
            // owns the background even when two canvases share world position.
            var background = NativeBackgroundFor(owner);
            isolationSurfaces.Add(new IsolationSurface { Owner = owner, Controller = controller, Canvas = canvas, BackgroundController = background });
        }
        isolationSurfacesDirty = false;
    }


    private static bool ClipPixelAligned(float edge, int width)
    {
        var pixel = (double)edge * width;
        return Math.Abs(pixel - Math.Round(pixel)) <= .0001;
    }
    private void RestoreScreenClips()
    {
        foreach (var lease in screenClipLeases)
        {
            if (lease.Owner == null) continue;
            lease.Owner.GetPropertyBlock(screenClipProperties);
            var restoreMin = screenClipProperties.GetFloat(ClipMinX) == lease.AppliedMin;
            var restoreMax = screenClipProperties.GetFloat(ClipMaxX) == lease.AppliedMax;
            if (restoreMin) screenClipProperties.SetFloat(ClipMinX, lease.Min);
            if (restoreMax) screenClipProperties.SetFloat(ClipMaxX, lease.Max);
            if (restoreMin || restoreMax) lease.Owner.SetPropertyBlock(screenClipProperties);
        }
        screenClipLeases.Clear();
    }
    private void LeaseLayer(GameObject owner, int layer, int cameraMask)
    {
        if (owner == null) return;
        if (isolationLeases.TryGetValue(owner, out var existing))
        {
            if (existing.Applied != layer) throw new InvalidOperationException("Monitor render objects share an isolation root");
            return;
        }
        // Do not make an originally camera-hidden object visible.
        if ((cameraMask & (1 << owner.layer)) == 0) return;
        var lease = spareLayerLeases.Count == 0 ? new LayerLease() : spareLayerLeases.Pop();
        lease.Owner = owner; lease.Original = owner.layer; lease.Applied = layer;
        isolationLeases.Add(owner, lease); owner.layer = layer;
    }
    private void LeaseRenderers(GameObject owner, int layer, int mask)
    {
        List<Renderer> list = null;
        for (int i = 0; i < walkedRoots.Count; i++)
        {
            if ((object)walkedRoots[i] == owner)
            {
                list = walkedRenderers[i];
                break;
            }
        }
        if (list == null)
        {
            CollectVisibleRenderers(owner, isolationRenderers);
            list = isolationRenderers;
        }
        foreach (Renderer isolationRenderer in list)
        {
            LeaseLayer(isolationRenderer.gameObject, layer, mask);
        }
    }
    private PresentationRect IsolationBounds(RectTransform bounds, Camera camera)
    {
        bounds.GetWorldCorners(isolationCorners);
        var x = float.PositiveInfinity; var y = x; var right = float.NegativeInfinity; var top = right;
        foreach (var corner in isolationCorners)
        {
            var point = camera.WorldToViewportPoint(corner);
            x = Mathf.Min(x, point.x); y = Mathf.Min(y, point.y); right = Mathf.Max(right, point.x); top = Mathf.Max(top, point.y);
        }
        return new PresentationRect(x, y, right - x, top - y).Intersect(new PresentationRect(0, 0, 1, 1));
    }
    private bool PrepareIsolation()
    {
        bool flag = false;
        foreach (Frame value3 in frames.Values)
        {
            if (value3.Active)
            {
                flag = true;
                break;
            }
        }
        if (!flag)
        {
            PrimeIsolationTextures();
            KeepIsolationTexturesPooled();
            return true;
        }
        Camera component = GetComponent<Camera>();
        if (component == null || renderingIsolation)
        {
            return FailIsolation("Native render camera unavailable");
        }
        DiscoverSurfaces();
        // HasLayerCollision (FindObjectsOfType<Renderer>() and <Canvas>() on the first active frame at least 60 frames
        // after the last scan, so at every motion onset of a pulsed chart) is gone. Unity sorts every Renderer in memory
        // by instance ID before it drops the inactive ones, about 38k with ExtendNotesPool 128, so each scan was a hitch
        // of several ms on the onset frame. It could only return true for a non-leased renderer or canvas on a reserved
        // layer, and none can exist:
        // - ReserveLayers hands out only unnamed layers that no GameObject in memory (assets included) uses;
        // - every game and Sinmai-Alpha asset (1.66 and 1.70 resources/sharedassets/level0, the StreamingAssets prefab
        //   bundles, Alpha's bundles) uses named layers only (0, 5, 8, 12, 13, 14), so nothing instantiated later
        //   carries a reserved layer, and new GameObjects start on layer 0;
        // - the only GameObject.layer writes (Cecil over the 1.66/1.70 Managed folders, MaiMagic, Alpha) are
        //   LeaseLayer/RestoreIsolation and copies of another object's current layer, and no copy runs between
        //   LeaseLayer (here, in OnPreCull) and RestoreIsolation (OnPostRender of this camera, or the next
        //   OnPreCull/BeforeNativeUpdate): only render callbacks run there (CustomAlphaMask.OnRenderObject, and
        //   Camera.onPreCull's TMP_UpdateManager for the isolation camera, whose one layer write is a TMP_SubMesh
        //   copying its 3D TextMeshPro's layer; no 3D TextMeshPro exists under a leased root), none of them on a
        //   leased object.
        // So the scan returned false on every call, and dropping it changes no output.
        if (isolationSurfaces.Count == 0 || !ReserveLayers(isolationSurfaces.Count))
        {
            return FailIsolation("No unused render layers available");
        }
        int num = 0;
        int num2 = 0;
        originalCameraMask = component.cullingMask;
        for (int i = 0; i < isolationSurfaces.Count; i++)
        {
            IsolationSurface isolationSurface = isolationSurfaces[i];
            if (isolationSurface.Canvas == null || isolationSurface.Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return FailIsolation("Monitor canvas cannot be isolated by a world camera");
            }
            isolationSurface.Layer = isolationLayers[i];
            int num3 = 1 << isolationSurface.Layer;
            num |= num3;
            isolationSurface.Background = ((isolationSurface.BackgroundController != null) ? NativeBackgroundObject(isolationSurface.BackgroundController) : null);
            if (!frames.TryGetValue(isolationSurface.Owner, out var value) || !value.Active)
            {
                num2 |= num3;
            }
            isolationCanvases.Clear();
            isolationSurface.Canvas.GetComponentsInChildren(includeInactive: false, isolationCanvases);
            foreach (Canvas isolationCanvase in isolationCanvases)
            {
                if (isolationCanvase.enabled)
                {
                    LeaseLayer(isolationCanvase.gameObject, isolationSurface.Layer, originalCameraMask);
                }
            }
            LeaseRenderers(isolationSurface.Controller.gameObject, isolationSurface.Layer, originalCameraMask);
            LeaseRenderers(isolationSurface.Background, isolationSurface.Layer, originalCameraMask);
            if (value == null)
            {
                continue;
            }
            foreach (Transform cover in value.Covers)
            {
                if (cover != null)
                {
                    LeaseRenderers(cover.gameObject, isolationSurface.Layer, originalCameraMask);
                }
            }
        }
        foreach (KeyValuePair<GameMonitor, Frame> frame in frames)
        {
            if (!frame.Value.Active)
            {
                continue;
            }
            bool flag2 = false;
            foreach (IsolationSurface isolationSurface2 in isolationSurfaces)
            {
                if (isolationSurface2.Owner == frame.Key)
                {
                    flag2 = true;
                    break;
                }
            }
            if (!flag2)
            {
                return FailIsolation("Native monitor ownership changed");
            }
        }
        if (isolationCamera == null)
        {
            GameObject gameObject = new GameObject("ChartFrameIsolationCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            isolationCamera = gameObject.AddComponent<Camera>();
            isolationCamera.enabled = false;
        }
        RenderTexture active = RenderTexture.active;
        renderingIsolation = true;
        pooledIsolationTextures.Clear();
        try
        {
            foreach (IsolationSurface isolationSurface3 in isolationSurfaces)
            {
                if (!frames.TryGetValue(isolationSurface3.Owner, out var value2) || !value2.Active)
                {
                    continue;
                }
                PresentationRect bounds = IsolationBounds(value2.Bounds, component);
                if (!bounds.HasArea)
                {
                    continue;
                }
                int num4 = ((component.targetTexture != null) ? component.targetTexture.width : component.pixelWidth);
                int num5 = ((component.targetTexture != null) ? component.targetTexture.height : component.pixelHeight);
                if (num4 <= 0 || num5 <= 0)
                {
                    return FailIsolation("Native camera has no viewport");
                }
                CollectScreenClipRenderers(isolationSurface3.Background);
                if (TryIsolationCrop(bounds, num4, num5, out var crop))
                {
                    int antiAliasing = ((component.targetTexture != null) ? component.targetTexture.antiAliasing : ((!component.allowMSAA) ? 1 : Math.Max(1, QualitySettings.antiAliasing)));
                    RenderTexture temporary = RenderTexture.GetTemporary(crop.Width, crop.Height, 24, RenderTextureFormat.RGB111110Float, RenderTextureReadWrite.Default, antiAliasing);
                    pooledIsolationTextures.Add(crop.Width);
                    pooledIsolationTextures.Add(crop.Height);
                    pooledIsolationTextures.Add(antiAliasing);
                    isolatedFrames.Add(new IsolatedFrame
                    {
                        Bounds = bounds,
                        Texture = temporary,
                        TextureUv = crop.TextureUv
                    });
                    isolationCamera.CopyFrom(component);
                    isolationCamera.enabled = false;
                    isolationCamera.transform.position = component.transform.position;
                    isolationCamera.transform.rotation = component.transform.rotation;
                    isolationCamera.targetTexture = temporary;
                    isolationCamera.rect = new Rect(0f, 0f, 1f, 1f);
                    isolationCamera.projectionMatrix = crop.Projection(component.projectionMatrix);
                    isolationCamera.ResetCullingMatrix();
                    isolationCamera.cullingMask = (originalCameraMask & ~num) | (1 << isolationSurface3.Layer);
                    isolationCamera.clearFlags = CameraClearFlags.Color;
                    isolationCamera.backgroundColor = Color.black;
                    try
                    {
                        LeaseScreenClips(crop.ProjectionViewport);
                        isolationCamera.Render();
                    }
                    finally
                    {
                        RestoreScreenClips();
                    }
                }
            }
            leasedSourceCamera = component;
            appliedCameraMask = (originalCameraMask & ~num) | num2;
            component.cullingMask = appliedCameraMask;
            return true;
        }
        finally
        {
            isolationCamera.targetTexture = null;
            renderingIsolation = false;
            RenderTexture.active = active;
        }
    }
    private bool FailIsolation(string reason)
    {
        RestoreIsolation(true);
        if (!isolationWarning) { isolationWarning = true; MelonLogger.Warning("[Chart Frame] " + reason + "; native frame retained"); }
        return false;
    }
    private void RestoreIsolation(bool releaseTextures)
    {
        RestoreScreenClips();
        if (leasedSourceCamera != null && leasedSourceCamera.cullingMask == appliedCameraMask) leasedSourceCamera.cullingMask = originalCameraMask;
        leasedSourceCamera = null;
        foreach (var lease in isolationLeases.Values)
        {
            if (lease.Owner != null && lease.Owner.layer == lease.Applied) lease.Owner.layer = lease.Original;
            lease.Owner = null; spareLayerLeases.Push(lease);
        }
        isolationLeases.Clear();
        if (!releaseTextures) return;
        foreach (var frame in isolatedFrames) if (frame.Texture != null) RenderTexture.ReleaseTemporary(frame.Texture);
        isolatedFrames.Clear();
    }
    private void ReleaseIsolation()
    {
        foreach (int isolationLayer in isolationLayers)
        {
            ReservedIsolationLayers.Remove(isolationLayer);
        }
        isolationLayers.Clear();
        pooledIsolationTextures.Clear();
        isolationPrimed = false;
        isolationSurfaces.Clear();
        isolationSurfacesDirty = true;
        if (isolationCamera != null)
        {
            UnityEngine.Object.Destroy(isolationCamera.gameObject);
        }
        isolationCamera = null;
        isolationWarning = false;
    }
    internal bool HasIsolatedFrames => isolatedFrames.Count != 0;
    internal void CompositeIsolation(RenderTexture source, RenderTexture destination)
    {
        var previous = RenderTexture.active; var pushed = false;
        try
        {
            Graphics.Blit(source, destination);
            Graphics.SetRenderTarget(destination); GL.PushMatrix(); pushed = true;
            GL.LoadPixelMatrix(0, source.width, source.height, 0);
            foreach (var frame in isolatedFrames)
            {
                var rect = frame.Bounds;
                Graphics.DrawTexture(new Rect(rect.X * source.width, (1 - rect.Top) * source.height, rect.Width * source.width, rect.Height * source.height),
                    frame.Texture, new Rect(frame.TextureUv.X, frame.TextureUv.Y, frame.TextureUv.Width, frame.TextureUv.Height), 0, 0, 0, 0);
            }
        }
        finally { if (pushed) GL.PopMatrix(); RenderTexture.active = previous; }
    }
}
