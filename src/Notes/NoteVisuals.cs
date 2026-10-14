using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Notes.Libs;
using HarmonyLib;
using Manager;
using Monitor;
using Monitor.Game;
using MelonLoader;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class VisualPayload { public VisualNote Note; public string Stream; public MajdataRegularPath SlideGeometry; }
    private sealed class VisualTimeline { public readonly Dictionary<string, List<VisualChange>> Streams = new(); }
    private sealed class VisualPart { public SpriteRenderer Native; public bool Guide, Mine, Hint; }
    private sealed class VisualLease
    {
        public int Monitor;
        public VisualPayload Payload;
        public readonly List<VisualPart> Parts = new();
    }
    private sealed class VisualOverlay
    {
        public SpriteRenderer Native, Render;
        public bool Applied, Enabled;
        public readonly MaterialPropertyBlock Properties = new();
        public void Restore()
        {
            if (!Applied) return;
            if (Native != null) Native.enabled = Enabled;
            if (Render != null) Render.gameObject.SetActive(false);
            Applied = false;
        }
    }
    private static ConditionalWeakTable<NoteData, VisualPayload> VisualNotes = new ConditionalWeakTable<NoteData, VisualPayload>();
    private static ConditionalWeakTable<NotesReader, VisualTimeline> VisualTimelines = new();
    private static readonly Dictionary<Component, VisualLease> VisualOwners = new();
    private static readonly Dictionary<SpriteRenderer, VisualOverlay> VisualOverlays = new();
    private static readonly HashSet<SpriteRenderer> VisualOverlayRenderers = new();
    private static List<(int Bar, int Grid, string Kind, string Text)> PendingVisualCommands => RuntimeCharts.Current.PendingVisualCommands;
    private static string pendingVisualMarker { get => RuntimeCharts.Current.pendingVisualMarker; set => RuntimeCharts.Current.pendingVisualMarker = value; }

    private static void ReadVisualMarker(MA2Record rec)
    {
        pendingVisualMarker = null;
        if (rec?._str == null || rec._str.Count < 5) return;
        var tail = rec._str[rec._str.Count - 1];
        if (!tail.StartsWith("VS|", StringComparison.Ordinal)) return;
        pendingVisualMarker = tail;
        rec._str.RemoveAt(rec._str.Count - 1);
    }
    private static void RestoreVisualMarker(MA2Record rec)
    {
        if (pendingVisualMarker != null) rec._str.Add(pendingVisualMarker);
    }
    private static void ApplyVisualMarker(NoteData note, string stream)
    {
        VisualNotes.Remove(note);
        if (pendingVisualMarker != null)
        {
            try
            {
                var payload = new VisualPayload { Note = VisualNote.Decode(pendingVisualMarker), Stream = stream ?? "" };
                VisualNotes.Add(note, payload);
                if (payload.Note.ReferenceMotion && payload.Note.Family == "slide" && !string.IsNullOrEmpty(payload.Note.ReferenceSlide))
                {
                    try { payload.SlideGeometry = new MajdataRegularPath(payload.Note.ReferenceSlide, alpha053: true, geometryOnly: true); }
                    catch (Exception e) { MelonLogger.Warning("[Note Visual] Keeping native slide motion: " + e.Message); }
                }
            }
            catch (Exception e) { MelonLogger.Warning("[Note Visual] Invalid note metadata: " + e.Message); }
        }
        pendingVisualMarker = null;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(NotesReader), "loadMa2")]
    public static void ResetVisualCommands(NotesReader __instance)
    {
        PendingVisualCommands.Clear();
        ReserveVisualNotes.Remove(__instance);
        VisualTimelines.Remove(__instance);
    }
    [HarmonyPrefix, HarmonyPatch(typeof(NotesRecord), "addRecord", new[] { typeof(string) })]
    public static void ReadVisualCommand(string str)
    {
        if (!(CustomNoteTypes.FeaturesEnabled())) {  return; }

        if (str == null) return;
        var p = str.Split('\t');
        if (p.Length != 4 || !(p[0] == "COLORV" || p[0] == "SIZEV" || p[0] == "ALPHAV")) return;
        if (int.TryParse(p[1], out var bar) && int.TryParse(p[2], out var grid))
            PendingVisualCommands.Add((bar, grid, p[0].ToLowerInvariant(), p[3]));
    }
    [HarmonyPostfix, HarmonyPatch(typeof(NotesReader), "loadMa2Main")]
    public static void BuildVisualCommands(NotesReader __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        var timeline = new VisualTimeline();
        foreach (var cmd in PendingVisualCommands)
        {
            var scope = ""; var text = cmd.Text; var separator = text.IndexOf('~');
            if (separator >= 0)
            {
                scope = text.Substring(0, separator);
                if (!IsStreamType(scope)) continue;
                text = text.Substring(separator + 1);
            }
            var time = new NotesTime(); time.init(cmd.Bar, cmd.Grid, __instance);
            if (!VisualState.TryParse(cmd.Kind, text, time.msec, out var changes))
            { MelonLogger.Warning("[Note Visual] Invalid " + cmd.Kind + ": " + text); continue; }
            if (!timeline.Streams.TryGetValue(scope, out var list)) timeline.Streams[scope] = list = new List<VisualChange>();
            list.AddRange(changes);
        }
        foreach (var key in timeline.Streams.Keys.ToArray())
            timeline.Streams[key] = timeline.Streams[key].OrderBy(c => c.Time).ToList(); // stable authored order
        VisualTimelines.Remove(__instance); VisualTimelines.Add(__instance, timeline);
    }

    private static void RestoreVisualLease(Component owner)
    {
        if (!VisualOwners.TryGetValue(owner, out var lease)) return;
        foreach (var part in lease.Parts)
            if (part.Native != null && VisualOverlays.TryGetValue(part.Native, out var overlay)) overlay.Restore();
    }

    [HarmonyPatch]
    public static class VisualInitializePatch
    {
        public static IEnumerable<MethodBase> TargetMethods() => FakeOwnerMethods().Where(m => m.Name == "Initialize"
            && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(NoteData));
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        public static void Prefix(Component __instance)
        {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

            RestoreVisualLease(__instance);
            VisualOwners.Remove(__instance);
        }



        [HarmonyPostfix]
        [HarmonyPriority(0)]
        public static void Postfix(Component __instance, NoteData __0)
        {
            if (!FeaturesEnabled(__instance) || IsBorrowed(__0) || !VisualNotes.TryGetValue(__0, out var value))
            {
                return;
            }
            NotesReader reader = NotesManager.Instance((__instance is NoteBase noteBase) ? noteBase.MonitorId : ((SlideRoot)__instance).MonitorId).getReader();
            if (Empty(value.Note.Base) && Empty(value.Note.Guide) && (!VisualTimelines.TryGetValue(reader, out var value2) || !value2.Streams.ContainsKey(value.Stream)))
            {
                return;
            }
            VisualLease lease = new VisualLease
            {
                Payload = value,
                Monitor = ((__instance is NoteBase noteBase2) ? noteBase2.MonitorId : ((SlideRoot)__instance).MonitorId)
            };
            HashSet<SpriteRenderer> seen = new HashSet<SpriteRenderer>();
            Traverse traverse = Traverse.Create(__instance);
            if (__instance is SlideFan)
            {
                SpriteRenderer[] value3 = traverse.Field("_spriteLines").GetValue<SpriteRenderer[]>();
                for (int i = 0; i < value3.Length; i++)
                {
                    Add(value3[i]);
                }
                value3 = traverse.Field("_effectSprites").GetValue<SpriteRenderer[]>();
                for (int i = 0; i < value3.Length; i++)
                {
                    Add(value3[i]);
                }
                GameObject[] value4 = traverse.Field("_baseStarObjs").GetValue<GameObject[]>();
                for (int i = 0; i < value4.Length; i++)
                {
                    AddObject(value4[i], guide: true);
                }
            }
            else if (__instance is SlideRoot)
            {
                foreach (SpriteRenderer item in traverse.Field("_spriteRenders").GetValue<List<SpriteRenderer>>())
                {
                    Add(item);
                }
                foreach (BreakSlide item2 in traverse.Field("_breakSpriteRenders").GetValue<List<BreakSlide>>())
                {
                    AddObject(item2?.gameObject);
                }
                AddObject(traverse.Field("_baseStarNote").GetValue<GameObject>(), guide: true);
            }
            else
            {
                SpriteRenderer value5 = traverse.Field("SpriteRenderDebug").GetValue<SpriteRenderer>();
                SpriteRenderer[] value3 = __instance.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
                foreach (SpriteRenderer spriteRenderer in value3)
                {
                    if (spriteRenderer != value5 && spriteRenderer.GetComponentInParent<NoteGuide>() == null)
                    {
                        Add(spriteRenderer);
                    }
                }
                NoteGuide[] componentsInChildren = __instance.GetComponentsInChildren<NoteGuide>(includeInactive: true);
                for (int i = 0; i < componentsInChildren.Length; i++)
                {
                    SpriteRenderer value6 = Traverse.Create(componentsInChildren[i]).Field("_spriteEachRender").GetValue<SpriteRenderer>();
                    if (value6 != null && seen.Add(value6))
                    {
                        lease.Parts.Add(new VisualPart
                        {
                            Native = value6,
                            Hint = true
                        });
                    }
                }
            }
            VisualOwners[__instance] = lease;
            void Add(SpriteRenderer sprite, bool guide = false)
            {
                if (sprite != null && !VisualOverlayRenderers.Contains(sprite) && seen.Add(sprite))
                {
                    lease.Parts.Add(new VisualPart
                    {
                        Native = sprite,
                        Guide = guide,
                        Mine = lease.Payload.Note.Mine
                    });
                }
            }
            void AddObject(GameObject obj, bool guide = false)
            {
                if (obj != null)
                {
                    SpriteRenderer[] componentsInChildren2 = obj.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
                    for (int j = 0; j < componentsInChildren2.Length; j++)
                    {
                        Add(componentsInChildren2[j], guide);
                    }
                }
            }
            static bool Empty(VisualValue visualValue)
            {
                if (!visualValue.Color.HasValue && !visualValue.X.HasValue)
                {
                    return !visualValue.Alpha.HasValue;
                }
                return false;
            }
        }
    }

    private static void ApplyVisualPart(VisualPart part, VisualValue live, VisualValue baseline)
    {
        var native = part.Native;
        if (native == null || !native.enabled || !native.gameObject.activeInHierarchy || native.sprite == null) return;
        var progress = IsTouchProgressMaterial(native);
        var color = progress ? live.Color : live.Color ?? baseline.Color;
        var alpha = progress ? live.Alpha : live.Alpha ?? baseline.Alpha;
        var x = part.Hint ? null : live.X ?? baseline.X; var y = part.Hint ? null : live.Y ?? baseline.Y;
        if (!color.HasValue && !alpha.HasValue && !x.HasValue) return;
        if (!VisualOverlays.TryGetValue(native, out var overlay) || overlay.Render == null)
        {
            var obj = new GameObject("AquaMai Note Visual"); obj.layer = native.gameObject.layer;
            obj.transform.SetParent(native.transform, false);
            overlay = new VisualOverlay { Native = native, Render = obj.AddComponent<SpriteRenderer>() };
            VisualOverlays[native] = overlay; VisualOverlayRenderers.Add(overlay.Render);
        }
        var render = overlay.Render;
        var source = part.Mine && color.HasValue ? GetMineColorSprite(native.sprite) : native.sprite;
        render.sprite = source;
        render.sharedMaterial = native.sharedMaterial;
        native.GetPropertyBlock(overlay.Properties); render.SetPropertyBlock(overlay.Properties);
        if (progress) ApplyTouchProgressLiveVisual(render, live);
        var gpuTint = !part.Hint && !progress && (color.HasValue || alpha.HasValue) &&
            ApplyNoteTintMaterial(render, source, native.color, color, alpha, native.sharedMaterial);
        if (!progress && !gpuTint && color.HasValue)
            render.sprite = GetVisualTintSprite(source, color.Value, native.color);
        var tinted = color.HasValue && render.sprite != native.sprite;
        render.drawMode = native.drawMode; render.size = native.size;
        render.tileMode = native.tileMode; render.adaptiveModeThreshold = native.adaptiveModeThreshold;
        render.flipX = native.flipX; render.flipY = native.flipY;
        render.sortingLayerID = native.sortingLayerID; render.sortingOrder = native.sortingOrder;
        render.maskInteraction = native.maskInteraction;
        var c = native.color;
        render.color = progress || gpuTint ? c : tinted ? new Color(1, 1, 1, c.a * (alpha ?? 1)) : new Color(c.r, c.g, c.b, c.a * (alpha ?? 1));
        render.transform.localScale = new Vector3(x ?? 1, y ?? 1, 1);
        overlay.Enabled = native.enabled; overlay.Applied = true;
        native.enabled = false; render.gameObject.SetActive(true);
        // Only the additional renderer's geometry scales. Native parents, arrow
        // positions, sibling input queues and the physical slide sensors stay native.
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(GameCtrl), "UpdateNotes")]
    public static void BeforeVisualFrame(GameCtrl __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        RestoreReserveVisuals(__instance.MonitorIndex);
        foreach (var pair in VisualOwners)
            if (pair.Value.Monitor == __instance.MonitorIndex) RestoreVisualLease(pair.Key);
    }
    [HarmonyPostfix]
    [HarmonyPriority(0)]
    [HarmonyPatch(typeof(GameCtrl), "UpdateNotes")]
    public static void AfterVisualFrame(GameCtrl __instance)
    {
        if (!FeaturesEnabled(__instance))
        {
            return;
        }
        NotesReader reader = NotesManager.Instance(__instance.MonitorIndex).getReader();
        VisualTimelines.TryGetValue(reader, out var value);
        ApplyReserveVisuals(__instance.MonitorIndex, reader, value);
        List<KeyValuePair<Component, VisualLease>> list = VisualScratch;
        list.Clear();
        foreach (KeyValuePair<Component, VisualLease> visualOwner in VisualOwners)
        {
            list.Add(visualOwner);
        }
        try
        {
            ApplyVisualOwners(list, __instance.MonitorIndex, value);
        }
        finally
        {
            list.Clear();
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameCtrl), "ForceNoteCollect")]
    public static void CollectVisualNotes(GameCtrl __instance)
    {
        RestoreReserveVisuals(__instance.MonitorIndex);
        TouchReserve[] array = (from p in ReserveVisuals
                                where p.Value.Monitor == __instance.MonitorIndex
                                select p.Key).ToArray();
        foreach (TouchReserve key in array)
        {
            ReserveVisuals.Remove(key);
        }
        KeyValuePair<Component, VisualLease>[] array2 = VisualOwners.Where((KeyValuePair<Component, VisualLease> p) => p.Value.Monitor == __instance.MonitorIndex).ToArray();
        for (int num = 0; num < array2.Length; num++)
        {
            KeyValuePair<Component, VisualLease> keyValuePair = array2[num];
            RestoreVisualLease(keyValuePair.Key);
            VisualOwners.Remove(keyValuePair.Key);
        }
        if (VisualOwners.Count == 0)
        {
            // A seek (practice / DebugFeature) used to run the full chart-release ClearVisualTintCache: it destroyed the
            // tint and gray sprites, the source readbacks and the tint materials and unloaded NoteVisuals.ab, so the
            // next coloured note paid the bundle load and the each-guide readback + recolour again mid-play. Those are
            // keyed by content (sprite, colour) and still valid after a seek, and the sprites are only shown through
            // the overlays (destroyed here) and borrowed carriers (removed by CollectBorrowedNotes, priority 810, for
            // this monitor), so they now live until GameProcess.OnRelease. Everything bound to renderers is still reset.
            VisualTintFailures.Clear();
            ResetVisualOverlays();
            ResetNoteTintBindings();
        }
    }


    private static void ResetNoteTintBindings()
    {
        foreach (KeyValuePair<SpriteRenderer, Material> borrowedTintDefault in BorrowedTintDefaults)
        {
            if (borrowedTintDefault.Key != null)
            {
                borrowedTintDefault.Key.sharedMaterial = borrowedTintDefault.Value;
                borrowedTintDefault.Key.SetPropertyBlock(null);
            }
        }
        BorrowedTintDefaults.Clear();
        NoteTintBlocks.Clear();
        TintWarmedReaders.Clear();
    }

    private static readonly VisualValue VisualLiveScratch = new VisualValue();

    private static readonly VisualValue VisualGuideScratch = new VisualValue();

    private static void ApplyVisualOwners(List<KeyValuePair<Component, VisualLease>> array, int monitorIndex, VisualTimeline value)
    {
        for (int i = 0; i < array.Count; i++)
        {
            KeyValuePair<Component, VisualLease> keyValuePair = array[i];
            VisualLease value2 = keyValuePair.Value;
            if (value2.Monitor != monitorIndex)
            {
                continue;
            }
            if (keyValuePair.Key == null || !keyValuePair.Key.gameObject.activeInHierarchy || EndFlagOf(keyValuePair.Key))
            {
                RestoreVisualLease(keyValuePair.Key);
                VisualOwners.Remove(keyValuePair.Key);
                continue;
            }
            VisualPayload payload = value2.Payload;
            VisualValue visualValue = EmptyVisualValue;
            VisualValue visualValue2 = EmptyVisualValue;
            if (value != null && value.Streams.TryGetValue(payload.Stream, out var value3))
            {
                // Resolved into two per-frame scratch values instead of two new ones per lease: ApplyVisualPart and
                // ApplyTouchProgressLiveVisual only read them and keep no reference, and Resolve fills every field.
                visualValue = VisualState.Resolve(value3, NotesManager.GetCurrentMsec(), payload.Note, null, VisualLiveScratch);
                if (payload.Note.Family == "slide")
                {
                    visualValue2 = VisualState.Resolve(value3, NotesManager.GetCurrentMsec(), payload.Note, "slidestar", VisualGuideScratch);
                }
            }
            foreach (VisualPart part in value2.Parts)
            {
                ApplyVisualPart(part, part.Guide ? visualValue2 : visualValue, part.Guide ? payload.Note.Guide : payload.Note.Base);
            }
        }
    }
}
