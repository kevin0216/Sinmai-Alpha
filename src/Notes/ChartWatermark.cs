using System.Collections.Generic;
using HarmonyLib;
using Monitor;
using SinmaiAlpha.Compatibility;
using SinmaiAlpha.Hosting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private static readonly Dictionary<GameMonitor, ChartWatermark> Watermarks = new();

    private static void ApplyWatermark(GameMonitor monitor)
    {
        if (GuiSizes.SinglePlayer && monitor.MonitorIndex != 0 || Watermarks.ContainsKey(monitor)) return;
        var main = Traverse.Create(monitor).Field("Main").GetValue<CanvasGroup>();
        if (main == null) return;
        var display = monitor.gameObject.AddComponent<ChartWatermark>();
        display.Bind(monitor, main);
        Watermarks.Add(monitor, display);
    }

    private static void ReleaseWatermarks()
    {
        foreach (var display in Watermarks.Values)
            if (display != null) { display.Stop(); Object.Destroy(display); }
        Watermarks.Clear();
    }
}

// 独立屏幕 UI，不参与谱面运动、滤镜、亮度和音符判定。
public sealed class ChartWatermark : MonoBehaviour
{
    internal const string Credit = "Sinmai-Alpha Mod by SaltNya";
    private GameMonitor owner;
    private CanvasGroup main;
    private Canvas sourceCanvas;
    private Camera sourceCamera;
    private RawImage adjustedMain, adjustedSub;
    private GameObject overlay;
    private TextMeshProUGUI label;
    private readonly Vector3[] corners = new Vector3[4];

    internal void Bind(GameMonitor monitor, CanvasGroup group)
    {
        owner = monitor; main = group;
        sourceCanvas = group.GetComponentInParent<Canvas>();
        if (sourceCanvas != null) sourceCanvas = sourceCanvas.rootCanvas;
        sourceCamera = sourceCanvas != null && sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : sourceCanvas != null && sourceCanvas.worldCamera != null ? sourceCanvas.worldCamera : Camera.main;
        adjustedMain = ScreenPositionAdjust.DisplayImage(monitor.MonitorIndex, false);
        adjustedSub = ScreenPositionAdjust.DisplayImage(monitor.MonitorIndex, true);
        var font = ReferenceSubtitleDisplay.NativeFont(group);
        if (font == null) return;
        overlay = new GameObject("Sinmai-Alpha Credit", typeof(RectTransform), typeof(Canvas));
        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32765;
        canvas.targetDisplay = sourceCanvas != null ? sourceCanvas.targetDisplay : 0;
        var textObject = new GameObject(Credit, typeof(RectTransform));
        textObject.transform.SetParent(overlay.transform, false);
        label = textObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = Credit; label.fontSize = 18;
        label.color = new Color(.85f, .85f, .85f, .75f);
        label.alignment = TextAlignmentOptions.TopLeft;
        label.enableWordWrapping = false; label.richText = false; label.raycastTarget = false;
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = Vector2.zero;
        label.rectTransform.pivot = new Vector2(0, 1);
        label.enabled = false;
    }

    // 游戏原生纵向画布为 1080x1920，顶端信息区高 450；与用户红框对应。
    internal static Rect Place(Rect frame, bool mainOnly)
    {
        var scale = frame.width / 1080f;
        return new Rect(frame.xMin + 8 * scale,
            frame.yMax - (mainOnly ? 6 * scale : frame.height * 450f / 1920f) - 28 * scale,
            360 * scale, 28 * scale);
    }

    private Rect ScreenBounds(RectTransform rect, Camera camera)
    {
        rect.GetWorldCorners(corners);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            var point = RectTransformUtility.WorldToScreenPoint(camera, corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private Rect ImageBounds(RawImage image)
    {
        var canvas = image.canvas;
        return ScreenBounds(image.rectTransform, canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null);
    }

    private void LateUpdate()
    {
        if (label == null)
        {
            return;
        }
        // label.enabled is written only when it changes: turning the label off and back on every
        // frame made TMP unregister, re-register and fully rebuild its text every frame. Every path
        // still ends in the same state as before (hidden on every early exit, and on an exception;
        // otherwise shown with this frame's position, size and font size), and nothing renders
        // between the old off/on writes.
        bool show = false;
        try
        {
            show = PlaceLabel();
        }
        finally
        {
            if (label.enabled != show)
            {
                label.enabled = show;
            }
        }
    }

    private void OnDisable() { if (label != null) label.enabled = false; }
    internal void Stop()
    {
        enabled = false;
        if (overlay != null) { overlay.SetActive(false); Object.Destroy(overlay); overlay = null; }
    }
    private void OnDestroy() => Stop();


    private bool PlaceLabel()
    {
        if (owner == null || main == null || !owner.gameObject.activeInHierarchy || !main.gameObject.activeInHierarchy || main.alpha <= 0f || !CustomNoteTypes.FeaturesForMonitor(owner.MonitorIndex) || (GuiSizes.SinglePlayer && owner.MonitorIndex != 0))
        {
            return false;
        }
        bool flag = GuiSizes.SinglePlayer && SinglePlayer.HideSubMonitor;
        Rect rect2;
        if (!flag && adjustedSub != null && adjustedSub.isActiveAndEnabled)
        {
            Rect rect = ImageBounds(adjustedSub);
            float num = rect.width / 1080f;
            rect2 = new Rect(rect.xMin + 8f * num, rect.yMin - 28f * num, 360f * num, 28f * num);
        }
        else if (adjustedMain != null && adjustedMain.isActiveAndEnabled)
        {
            rect2 = Place(ImageBounds(adjustedMain), mainOnly: true);
        }
        else
        {
            RectTransform rectTransform = ((!flag && sourceCanvas != null) ? (sourceCanvas.transform as RectTransform) : (main.transform as RectTransform));
            if (rectTransform == null)
            {
                return false;
            }
            rect2 = Place(ScreenBounds(rectTransform, sourceCamera), flag);
        }
        if (!(rect2.width <= 0f) && !(rect2.height <= 0f))
        {
            RectTransform rectTransform2 = label.rectTransform;
            rectTransform2.anchoredPosition = new Vector2(rect2.xMin, rect2.yMax);
            rectTransform2.sizeDelta = rect2.size;
            label.fontSize = 18f * rect2.width / 360f;
            return true;
        }
        return false;
    }
}
