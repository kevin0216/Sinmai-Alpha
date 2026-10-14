using SinmaiAlpha.Difficulty;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Hosting;
using HarmonyLib;
using Manager;
using MelonLoader;
using Monitor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private static ConditionalWeakTable<NotesReader, List<SubtitleChange>> SubtitleEvents = new();
    private static readonly Dictionary<GameMonitor, ReferenceSubtitleDisplay> SubtitleDisplays = new();
    private static List<(int Bar, int Grid, string Text)> PendingSubtitles => RuntimeCharts.Current.PendingSubtitles;

    private static void ResetSubtitleCommands(NotesReader reader)
    { PendingSubtitles.Clear(); SubtitleEvents.Remove(reader); }
    private static void ReadSubtitleCommand(string line)
    {
        if (line == null || !line.StartsWith("TEXT\t", StringComparison.Ordinal)) return;
        var p = line.Split('\t');
        if (p.Length == 4 && int.TryParse(p[1], out var bar) && int.TryParse(p[2], out var grid))
            PendingSubtitles.Add((bar, grid, p[3]));
    }
    private static void BuildSubtitleCommands(NotesReader reader)
    {
        var changes = new List<SubtitleChange>();
        foreach (var item in PendingSubtitles)
        {
            var time = new NotesTime(); time.init(item.Bar, item.Grid, reader);
            if (SubtitleCommands.Decode(item.Text, time.msec, out var change)) changes.Add(change);
            else MelonLogger.Warning("[Chart Subtitle] Invalid TEXT payload at " + item.Bar + ":" + item.Grid);
        }
        SubtitleEvents.Remove(reader);
        if (changes.Count != 0)
        {
            SubtitleEvents.Add(reader, changes);
            MelonLogger.Msg("[Chart Subtitle] Loaded " + changes.Count + " events");
        }
    }
    private static void ApplySubtitlePresentation(GameMonitor monitor)
    {
        NotesReader reader = NotesManager.Instance(monitor.MonitorIndex).getReader();
        CanvasGroup value = MainOf(monitor);
        if ((GuiSizes.SinglePlayer && monitor.MonitorIndex != 0) || value == null || !SubtitleEvents.TryGetValue(reader, out var value2))
        {
            if (SubtitleDisplays.TryGetValue(monitor, out var value3))
            {
                value3.Stop();
                UnityEngine.Object.Destroy(value3);
                SubtitleDisplays.Remove(monitor);
            }
        }
        else
        {
            if (!SubtitleDisplays.TryGetValue(monitor, out var value4) || value4 == null)
            {
                value4 = (SubtitleDisplays[monitor] = monitor.gameObject.AddComponent<ReferenceSubtitleDisplay>());
            }
            value4.Bind(monitor, value, value2);
        }
    }
    private static void ReleaseSubtitlePresentation()
    {
        foreach (var display in SubtitleDisplays.Values) if (display != null) { display.Stop(); Object.Destroy(display); }
        SubtitleDisplays.Clear(); PendingSubtitles.Clear(); SubtitleEvents = new();
    }
}

// Use the game's existing TMP font atlas and fallback chain. No bundled font
// data, OS font scan or per-event font import is needed during gameplay.
public sealed class ReferenceSubtitleDisplay : MonoBehaviour
{
    private sealed class Label
    {
        public SubtitleChange Event;
        public TMPro.TextMeshProUGUI Text;


        public bool Rebuilt;

        public float BuiltScale = float.NaN;
    }
    private GameMonitor owner;
    private CanvasGroup main;
    private List<SubtitleChange> events;
    private SubtitleCommands.Timeline timeline;
    private RectTransform root;
    private readonly Dictionary<SubtitleChange, Label> labels = new();
    private readonly List<Label> previous = new();
    private TMPro.TMP_FontAsset font;

    internal static TMPro.TMP_FontAsset NativeFont(CanvasGroup group)
    {
        var canvas = group != null ? group.GetComponentInParent<Canvas>() : null;
        if (canvas != null)
            foreach (var text in canvas.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (text.font != null && text.font.name.IndexOf("MaruGothic", StringComparison.OrdinalIgnoreCase) >= 0) return text.font;
        var all = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>();
        foreach (var item in all)
            if (item != null && item.name.IndexOf("MaruGothic", StringComparison.OrdinalIgnoreCase) >= 0) return item;
        foreach (var item in all)
            if (item != null && item.name.IndexOf("SEGA", StringComparison.OrdinalIgnoreCase) >= 0) return item;
        return TMPro.TMP_Settings.defaultFontAsset;
    }
    internal void Bind(GameMonitor monitor, CanvasGroup group, List<SubtitleChange> changes)
    {
        owner = monitor;
        main = group;
        enabled = true;
        if (events == changes)
        {
            return;
        }
        ClearLabels();
        events = changes;
        timeline = new SubtitleCommands.Timeline(changes);
        font = NativeFont(group);
        if (font == null)
        {
            MelonLogger.Warning("[Chart Subtitle] Native game font unavailable");
            return;
        }
        GameObject gameObject = new GameObject("Sinmai-Alpha Native Text", typeof(RectTransform), typeof(Canvas));
        gameObject.layer = group.gameObject.layer;
        root = (RectTransform)gameObject.transform;
        root.SetParent(group.transform, worldPositionStays: false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        RectTransform rectTransform = root;
        Vector2 offsetMin = (root.offsetMax = Vector2.zero);
        rectTransform.offsetMin = offsetMin;
        Canvas component = gameObject.GetComponent<Canvas>();
        component.overrideSorting = true;
        component.sortingOrder = 32760;
        bool flag = false;
        foreach (SubtitleChange change in changes)
        {
            GameObject gameObject2 = new GameObject("Chart text " + change.Index, typeof(RectTransform));
            gameObject2.layer = gameObject.layer;
            gameObject2.transform.SetParent(root, worldPositionStays: false);
            TextMeshProUGUI textMeshProUGUI = gameObject2.AddComponent<TextMeshProUGUI>();
            textMeshProUGUI.font = font;
            textMeshProUGUI.text = change.Text;
            textMeshProUGUI.fontSize = ((change.Size > 0f) ? change.Size : 32f);
            textMeshProUGUI.fontStyle = StyleFor(change);
            textMeshProUGUI.alignment = TextAlignmentOptions.Center;
            textMeshProUGUI.enableWordWrapping = true;
            textMeshProUGUI.richText = false;
            textMeshProUGUI.raycastTarget = false;
            RectTransform rectTransform2 = textMeshProUGUI.rectTransform;
            rectTransform2.anchorMin = Vector2.zero;
            rectTransform2.anchorMax = Vector2.one;
            offsetMin = (rectTransform2.offsetMax = Vector2.zero);
            rectTransform2.offsetMin = offsetMin;
            // Before the first ForceMeshUpdate (same font chain and state it would see), add every glyph these
            // labels will add to a dynamic fallback font in one batch instead of one upload and kerning scan per
            // glyph (see GlyphPreload). The ForceMeshUpdate calls then find them already present.
            if (!flag)
            {
                flag = true;
                GlyphPreload.Run(textMeshProUGUI, changes);
            }
            textMeshProUGUI.ForceMeshUpdate();
            textMeshProUGUI.enabled = false;
            labels[change] = new Label
            {
                Event = change,
                Text = textMeshProUGUI
            };
        }
    }
    internal void Stop()
    {
        enabled = false;
        foreach (Label previou in previous)
        {
            if (previou.Text != null)
            {
                previou.Text.enabled = false;
            }
        }
        previous.Clear();
        current.Clear();
    }
    private void LateUpdate()
    {
        // Labels that stay on screen are no longer switched off and back on every frame (that made TMP clear,
        // re-register, re-parse and rebuild every shown subtitle and its canvas each frame): only labels leaving the
        // screen are switched off, and only newly shown ones on. The text, colour and position writes are unchanged,
        // the enabled set at the end of LateUpdate is the same on every path (an exception mid-loop included) and
        // nothing renders in between. A kept label is rebuilt by TMP itself when its text or colour changes, and by
        // RebuildRescaled when its scale does, so it shows the mesh the old rebuild made.
        current.Clear();
        if (!KeepShownLabels)
        {
            foreach (Label previou in previous)
            {
                if (previou.Text != null)
                {
                    previou.Text.enabled = false;
                }
            }
        }
        try
        {
            ShowLabels();
        }
        finally
        {
            foreach (Label previou2 in previous)
            {
                if (previou2.Text != null && !current.Contains(previou2))
                {
                    previou2.Text.enabled = false;
                }
            }
            previous.Clear();
            previous.AddRange(current);
        }
    }
    private void ClearLabels()
    {
        Stop(); enabled = true; labels.Clear();
        if (root != null) { root.gameObject.SetActive(false); Object.Destroy(root.gameObject); }
        root = null;
    }
    private void OnDestroy()
    {
        ClearLabels();
        events = null;
        timeline = null;
        // ClearLabels sets enabled again; never leave a destroyed display on the static event.
        Unsubscribe();
    }


    private static class GlyphPreload
    {
        private sealed class Plan
        {
            public TMP_FontAsset Font;

            public readonly List<uint> Unicodes = new List<uint>();

            public readonly Dictionary<uint, uint> Glyphs = new Dictionary<uint, uint>();

            public readonly List<int> Searched = new List<int>();

            public readonly Dictionary<TMP_FontAsset, Dictionary<uint, uint>> Indices = new Dictionary<TMP_FontAsset, Dictionary<uint, uint>>();

            public Font HeiSource;

            public TMP_SpriteAsset Sprites;

            public bool Italic;

            public int WeightSlot;

            public bool Stopped;
        }

        private const int StringSource = 3;

        private static AccessTools.FieldRef<TMP_FontAsset, List<uint>> GlyphIndexList;

        private static AccessTools.FieldRef<TMP_FontAsset, List<GlyphRect>> FreeGlyphRects;

        private static AccessTools.FieldRef<TMP_FontAsset, List<GlyphRect>> UsedGlyphRects;

        private static AccessTools.FieldRef<TMP_FontAsset, int> AtlasTextureIndex;

        private static AccessTools.FieldRef<TMP_Text, bool> IsAwake;

        // TMP_FontAsset.GetGlyphIndex: the LoadFontFace + FontEngine.GetGlyphIndex pair TryAddCharacterInternal makes.
        private static Func<TMP_FontAsset, uint, uint> GlyphIndexOf;

        private static FieldInfo InputSource;

        private static FieldInfo HeiSource;

        private static MethodInfo TryAddGlyphsToTexture;

        private static MethodInfo ResetAtlasTexture;

        private static MethodInfo UpdateGlyphAdjustmentRecords;

        private static AccessTools.FieldRef<TMP_FontFeatureTable, List<TMP_GlyphPairAdjustmentRecord>> PairRecords;

        // Resolved once; any failure leaves the preload off (a throwing type initialiser would surface in Bind).
        private static readonly bool Available = Init();

        private static bool Init()
        {
            try
            {
                GlyphIndexList = TryFieldRef<TMP_FontAsset, List<uint>>("m_GlyphIndexList");
                FreeGlyphRects = TryFieldRef<TMP_FontAsset, List<GlyphRect>>("m_FreeGlyphRects");
                UsedGlyphRects = TryFieldRef<TMP_FontAsset, List<GlyphRect>>("m_UsedGlyphRects");
                AtlasTextureIndex = TryFieldRef<TMP_FontAsset, int>("m_AtlasTextureIndex");
                IsAwake = TryFieldRef<TMP_Text, bool>("m_isAwake");
                MethodInfo methodInfo = AccessTools.Method(typeof(TMP_FontAsset), "GetGlyphIndex", new System.Type[1] { typeof(uint) });
                GlyphIndexOf = ((methodInfo != null) ? (Delegate.CreateDelegate(typeof(Func<TMP_FontAsset, uint, uint>), methodInfo) as Func<TMP_FontAsset, uint, uint>) : null);
                InputSource = AccessTools.Field(typeof(TMP_Text), "m_inputSource");
                HeiSource = AccessTools.Field(typeof(LevelTextStyle), "systemHeiSource");
                TryAddGlyphsToTexture = AccessTools.Method(typeof(FontEngine), "TryAddGlyphsToTexture", new System.Type[8]
                {
                    typeof(List<uint>),
                    typeof(int),
                    typeof(GlyphPackingMode),
                    typeof(List<GlyphRect>),
                    typeof(List<GlyphRect>),
                    typeof(GlyphRenderMode),
                    typeof(Texture2D),
                    typeof(Glyph[]).MakeByRefType()
                });
                ResetAtlasTexture = AccessTools.Method(typeof(FontEngine), "ResetAtlasTexture", new System.Type[1] { typeof(Texture2D) });
                UpdateGlyphAdjustmentRecords = AccessTools.Method(typeof(TMP_FontAsset), "UpdateGlyphAdjustmentRecords", new System.Type[2]
                {
                    typeof(uint),
                    typeof(uint)
                });
                PairRecords = TryFieldRef<TMP_FontFeatureTable, List<TMP_GlyphPairAdjustmentRecord>>("m_GlyphPairAdjustmentRecords");
                return PairRecords != null && GlyphIndexOf != null && GlyphIndexList != null && FreeGlyphRects != null && UsedGlyphRects != null && AtlasTextureIndex != null && IsAwake != null && InputSource != null && HeiSource != null && TryAddGlyphsToTexture != null && ResetAtlasTexture != null && UpdateGlyphAdjustmentRecords != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // first: the label of changes[0], created and set up, right before its ForceMeshUpdate.
        internal static void Run(TextMeshProUGUI first, List<SubtitleChange> changes)
        {
            if (!Available || first == null)
            {
                return;
            }
            try
            {
                // OnPreRenderCanvas parses (and so adds glyphs) only for an awake, active label; later labels are
                // created the same way under the same parent. Rich text is off for every label.
                TMP_FontAsset font = first.font;
                if (font == null || !first.isActiveAndEnabled || !IsAwake(first) || first.richText)
                {
                    return;
                }
                Plan plan = new Plan
                {
                    HeiSource = (HeiSource.GetValue(null) as Font),
                    Sprites = first.spriteAsset
                };
                FontWeight fontWeight = first.fontWeight;
                for (int i = 0; i < changes.Count && !plan.Stopped; i++)
                {
                    string text = changes[i].Text;
                    // Non-empty text always went through the text setter (TextInputSources.String: no escape parsing).
                    if (i == 0 && !string.IsNullOrEmpty(text) && Convert.ToInt32(InputSource.GetValue(first)) != StringSource)
                    {
                        return;
                    }
                    WalkLabel(plan, font, text, StyleFor(changes[i]), fontWeight);
                }
                if (plan.Font != null && plan.Unicodes.Count != 0)
                {
                    Commit(plan);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Chart Subtitle] Glyph preload skipped: " + ex.Message);
            }
        }

        // TMP_Text.StringToCharArray (String source, rich text off) + the lookup part of SetArraySizes.
        private static void WalkLabel(Plan plan, TMP_FontAsset font, string text, FontStyles style, FontWeight fontWeight)
        {
            if ((style & (FontStyles.LowerCase | FontStyles.UpperCase | FontStyles.SmallCaps)) != 0)
            {
                plan.Stopped = true;
                return;
            }
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            // StringToCharArray reads past a trailing high surrogate and throws before this label looks anything up.
            if (char.IsHighSurrogate(text[text.Length - 1]))
            {
                plan.Stopped = true;
                return;
            }
            plan.Italic = (style & FontStyles.Italic) == FontStyles.Italic;
            FontWeight fontWeight2 = (((style & FontStyles.Bold) == FontStyles.Bold) ? FontWeight.Bold : fontWeight);
            plan.WeightSlot = ((plan.Italic || fontWeight2 != FontWeight.Regular) ? WeightSlot(fontWeight2) : (-1));
            for (int i = 0; i < text.Length; i++)
            {
                if (plan.Stopped)
                {
                    break;
                }
                uint num = text[i];
                if (char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
                {
                    num = (uint)char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                if (num == 0)
                {
                    break;
                }
                if (!Resolve(plan, font, num))
                {
                    uint unicode = (uint)((TMP_Settings.missingGlyphCharacter == 0) ? 9633 : TMP_Settings.missingGlyphCharacter);
                    if (!ResolveMissing(plan, font, unicode))
                    {
                        FindFrom(plan, 32u, font);
                    }
                }
            }
        }

        // SetArraySizes: primary (no fallbacks), its fallbacks, label sprites, TMP Settings fallbacks, default font,
        // default sprites.
        private static bool Resolve(Plan plan, TMP_FontAsset font, uint unicode)
        {
            if (Find(plan, unicode, font, includeFallbacks: false))
            {
                return true;
            }
            List<TMP_FontAsset> fallbackFontAssetTable = font.fallbackFontAssetTable;
            if (fallbackFontAssetTable != null && fallbackFontAssetTable.Count > 0 && FindIn(plan, unicode, fallbackFontAssetTable))
            {
                return true;
            }
            if (plan.Sprites != null && IsSprite(plan.Sprites, unicode))
            {
                return true;
            }
            List<TMP_FontAsset> fallbackFontAssets = TMP_Settings.fallbackFontAssets;
            if (fallbackFontAssets != null && fallbackFontAssets.Count > 0 && FindIn(plan, unicode, fallbackFontAssets))
            {
                return true;
            }
            TMP_FontAsset defaultFontAsset = TMP_Settings.defaultFontAsset;
            if (defaultFontAsset != null && FindFrom(plan, unicode, defaultFontAsset))
            {
                return true;
            }
            TMP_SpriteAsset defaultSpriteAsset = TMP_Settings.defaultSpriteAsset;
            if (defaultSpriteAsset != null)
            {
                return IsSprite(defaultSpriteAsset, unicode);
            }
            return false;
        }

        // SetArraySizes' missing-glyph replacement (the space after it is looked up by the caller).
        private static bool ResolveMissing(Plan plan, TMP_FontAsset font, uint unicode)
        {
            if (FindFrom(plan, unicode, font))
            {
                return true;
            }
            List<TMP_FontAsset> fallbackFontAssets = TMP_Settings.fallbackFontAssets;
            if (fallbackFontAssets != null && fallbackFontAssets.Count > 0 && FindIn(plan, unicode, fallbackFontAssets))
            {
                return true;
            }
            TMP_FontAsset defaultFontAsset = TMP_Settings.defaultFontAsset;
            if (defaultFontAsset != null)
            {
                return FindFrom(plan, unicode, defaultFontAsset);
            }
            return false;
        }

        private static bool IsSprite(TMP_SpriteAsset sprites, uint unicode)
        {
            TMP_SpriteAsset.SearchForSpriteByUnicode(sprites, unicode, includeFallbacks: true, out var spriteIndex);
            return spriteIndex != -1;
        }

        // TMP_FontAssetUtilities.GetCharacterFromFontAsset with includeFallbacks.
        private static bool FindFrom(Plan plan, uint unicode, TMP_FontAsset fontAsset)
        {
            plan.Searched.Clear();
            return Find(plan, unicode, fontAsset, includeFallbacks: true);
        }

        // TMP_FontAssetUtilities.GetCharacterFromFontAssets with includeFallbacks.
        private static bool FindIn(Plan plan, uint unicode, List<TMP_FontAsset> fontAssets)
        {
            plan.Searched.Clear();
            int count = fontAssets.Count;
            for (int i = 0; i < count; i++)
            {
                if (!(fontAssets[i] == null) && Find(plan, unicode, fontAssets[i], includeFallbacks: true))
                {
                    return true;
                }
            }
            return false;
        }

        // TMP_FontAssetUtilities.GetCharacterFromFontAsset_Internal; true when the character resolves here (or the
        // walk stopped).
        private static bool Find(Plan plan, uint unicode, TMP_FontAsset fontAsset, bool includeFallbacks)
        {
            if (plan.WeightSlot >= 0)
            {
                TMP_FontWeightPair[] fontWeightTable = fontAsset.fontWeightTable;
                if (fontWeightTable == null || fontWeightTable.Length <= plan.WeightSlot)
                {
                    return Stop(plan);
                }
                TMP_FontAsset tMP_FontAsset = (plan.Italic ? fontWeightTable[plan.WeightSlot].italicTypeface : fontWeightTable[plan.WeightSlot].regularTypeface);
                if (tMP_FontAsset != null && (Has(plan, tMP_FontAsset, unicode) || (tMP_FontAsset.atlasPopulationMode == AtlasPopulationMode.Dynamic && Add(plan, tMP_FontAsset, unicode))))
                {
                    return true;
                }
            }
            if (Has(plan, fontAsset, unicode) || (fontAsset.atlasPopulationMode == AtlasPopulationMode.Dynamic && Add(plan, fontAsset, unicode)))
            {
                return true;
            }
            List<TMP_FontAsset> fallbackFontAssetTable = fontAsset.fallbackFontAssetTable;
            if (includeFallbacks && fallbackFontAssetTable != null)
            {
                int count = fallbackFontAssetTable.Count;
                for (int i = 0; i < count; i++)
                {
                    TMP_FontAsset tMP_FontAsset2 = fallbackFontAssetTable[i];
                    if (tMP_FontAsset2 == null)
                    {
                        continue;
                    }
                    int instanceID = tMP_FontAsset2.GetInstanceID();
                    if (!plan.Searched.Contains(instanceID))
                    {
                        plan.Searched.Add(instanceID);
                        if (Find(plan, unicode, tMP_FontAsset2, includeFallbacks: true))
                        {
                            return true;
                        }
                    }
                }
            }
            return plan.Stopped;
        }

        private static bool Has(Plan plan, TMP_FontAsset fontAsset, uint unicode)
        {
            Dictionary<uint, TMP_Character> characterLookupTable = fontAsset.characterLookupTable;
            if (characterLookupTable == null)
            {
                return Stop(plan);
            }
            if (!characterLookupTable.ContainsKey(unicode))
            {
                if (ReferenceEquals(fontAsset, plan.Font))
                {
                    return plan.Glyphs.ContainsKey(unicode);
                }
                return false;
            }
            return true;
        }

        // Whether TryAddCharacterInternal would add the character (it is then queued). A failed atlas pack cannot
        // be foreseen; such a glyph is not added by Commit and TMP retries it later. That only matches the per-glyph
        // order while the atlas has room: on a full atlas the missing-glyph fallback happens after the batch rather
        // than at the glyph's place (needs thousands of dynamic glyphs in one session).
        private static bool Add(Plan plan, TMP_FontAsset fontAsset, uint unicode)
        {
            Font sourceFontFile = fontAsset.sourceFontFile;
            GlyphRenderMode atlasRenderMode = fontAsset.atlasRenderMode;
            if (sourceFontFile == null || ReferenceEquals(sourceFontFile, plan.HeiSource) || atlasRenderMode == GlyphRenderMode.SDF8 || atlasRenderMode == GlyphRenderMode.SDF16 || atlasRenderMode == GlyphRenderMode.SDF32)
            {
                return Stop(plan);
            }
            Texture2D[] atlasTextures = fontAsset.atlasTextures;
            int num = AtlasTextureIndex(fontAsset);
            if (atlasTextures == null || num < 0 || num >= atlasTextures.Length || atlasTextures[num] == null || ((atlasTextures[num].width == 0 || atlasTextures[num].height == 0) && !atlasTextures[num].isReadable))
            {
                return Stop(plan);
            }
            if (!plan.Indices.TryGetValue(fontAsset, out var value))
            {
                value = new Dictionary<uint, uint>();
                plan.Indices.Add(fontAsset, value);
            }
            if (!value.TryGetValue(unicode, out var value2))
            {
                value2 = GlyphIndexOf(fontAsset, unicode);
                value.Add(unicode, value2);
            }
            if (value2 == 0)
            {
                return false;
            }
            if (plan.Font == null)
            {
                plan.Font = fontAsset;
            }
            else if (!ReferenceEquals(plan.Font, fontAsset))
            {
                return Stop(plan);
            }
            plan.Unicodes.Add(unicode);
            plan.Glyphs.Add(unicode, value2);
            return true;
        }

        private static bool Stop(Plan plan)
        {
            plan.Stopped = true;
            return true;
        }

        private static int WeightSlot(FontWeight fontWeight)
        {
            return fontWeight switch
            {
                FontWeight.Thin => 1,
                FontWeight.ExtraLight => 2,
                FontWeight.Light => 3,
                FontWeight.Regular => 4,
                FontWeight.Medium => 5,
                FontWeight.SemiBold => 6,
                FontWeight.Bold => 7,
                FontWeight.Heavy => 8,
                FontWeight.Black => 9,
                _ => 4,
            };
        }

        // TryAddCharacterInternal for every queued character, in order, with one native batch for the new glyphs.
        private static void Commit(Plan plan)
        {
            TMP_FontAsset font = plan.Font;
            List<uint> list = GlyphIndexList(font);
            Dictionary<uint, Glyph> glyphLookupTable = font.glyphLookupTable;
            Dictionary<uint, TMP_Character> characterLookupTable = font.characterLookupTable;
            List<uint> list2 = new List<uint>();
            HashSet<uint> hashSet = new HashSet<uint>();
            foreach (uint unicode in plan.Unicodes)
            {
                uint num = plan.Glyphs[unicode];
                if (!glyphLookupTable.ContainsKey(num) && hashSet.Add(num))
                {
                    list2.Add(num);
                }
            }
            if (FontEngine.LoadFontFace(font.sourceFontFile, font.faceInfo.pointSize) != FontEngineError.Success)
            {
                return;
            }
            if (list2.Count != 0)
            {
                Texture2D texture2D = font.atlasTextures[AtlasTextureIndex(font)];
                if (texture2D.width == 0 || texture2D.height == 0)
                {
                    texture2D.Resize(font.atlasWidth, font.atlasHeight);
                    ResetAtlasTexture.Invoke(null, new object[1] { texture2D });
                }
                object[] array = new object[8]
                {
                    list2,
                    font.atlasPadding,
                    GlyphPackingMode.BestShortSideFit,
                    FreeGlyphRects(font),
                    UsedGlyphRects(font),
                    font.atlasRenderMode,
                    texture2D,
                    null
                };
                TryAddGlyphsToTexture.Invoke(null, array);
                // Null-terminated; entries past the terminator are left over from earlier calls.
                Glyph[] array2 = array[7] as Glyph[];
                for (int i = 0; array2 != null && i < array2.Length && array2[i] != null; i++)
                {
                    Glyph glyph = array2[i];
                    font.glyphTable.Add(glyph);
                    glyphLookupTable.Add(glyph.index, glyph);
                    list.Add(glyph.index);
                }
            }
            // Index of the one-at-a-time add (each successful one rescans kerning) that brought each new glyph.
            Dictionary<uint, int> dictionary = new Dictionary<uint, int>();
            int num2 = 0;
            uint num3 = 0u;
            uint num4 = 0u;
            foreach (uint unicode2 in plan.Unicodes)
            {
                if (glyphLookupTable.TryGetValue(plan.Glyphs[unicode2], out var value))
                {
                    TMP_Character tMP_Character = new TMP_Character(unicode2, value);
                    font.characterTable.Add(tMP_Character);
                    characterLookupTable.Add(unicode2, tMP_Character);
                    if (hashSet.Contains(value.index) && !dictionary.ContainsKey(value.index))
                    {
                        dictionary.Add(value.index, num2);
                    }
                    num2++;
                    num3 = unicode2;
                    num4 = value.index;
                }
            }
            // Every one-at-a-time add rescans all pairs of m_GlyphIndexList; the union of those scans is the last one.
            if (num2 == 0 || !TMP_Settings.getFontFeaturesAtRuntime)
            {
                return;
            }
            TMP_FontFeatureTable fontFeatureTable = font.fontFeatureTable;
            int num5 = ((fontFeatureTable != null && PairRecords(fontFeatureTable) != null) ? PairRecords(fontFeatureTable).Count : 0);
            UpdateGlyphAdjustmentRecords.Invoke(font, new object[2] { num3, num4 });
            if (font.fontFeatureTable != fontFeatureTable)
            {
                num5 = 0;
            }
            List<TMP_GlyphPairAdjustmentRecord> list3 = PairRecords(font.fontFeatureTable);
            if (list3 == null || list3.Count - num5 < 2)
            {
                return;
            }
            // The one-at-a-time scans append a pair at the first add whose list holds both glyphs (zero padding always
            // does), in (first, second) list order with the padding last; give this scan's records that order too.
            Dictionary<uint, int> position = new Dictionary<uint, int>();
            for (int j = 0; j < list.Count; j++)
            {
                if (!position.ContainsKey(list[j]))
                {
                    position.Add(list[j], j);
                }
            }
            list3.Sort(num5, list3.Count - num5, Comparer<TMP_GlyphPairAdjustmentRecord>.Create(delegate (TMP_GlyphPairAdjustmentRecord a, TMP_GlyphPairAdjustmentRecord b)
            {
                int num6 = PairAddIndex(a, dictionary).CompareTo(PairAddIndex(b, dictionary));
                if (num6 != 0)
                {
                    return num6;
                }
                num6 = Position(a.firstAdjustmentRecord.glyphIndex, position).CompareTo(Position(b.firstAdjustmentRecord.glyphIndex, position));
                return (num6 != 0) ? num6 : Position(a.secondAdjustmentRecord.glyphIndex, position).CompareTo(Position(b.secondAdjustmentRecord.glyphIndex, position));
            }));
        }

        private static int PairAddIndex(TMP_GlyphPairAdjustmentRecord record, Dictionary<uint, int> added)
        {
            added.TryGetValue(record.firstAdjustmentRecord.glyphIndex, out var value);
            added.TryGetValue(record.secondAdjustmentRecord.glyphIndex, out var value2);
            return Math.Max(value, value2);
        }

        // Glyph 0 is the array's zero padding, after every real glyph.
        private static int Position(uint glyphIndex, Dictionary<uint, int> position)
        {
            if (glyphIndex == 0 || !position.TryGetValue(glyphIndex, out var value))
            {
                return int.MaxValue;
            }
            return value;
        }
    }

    private static readonly AccessTools.FieldRef<TextMeshProUGUI, float> PreviousLossyScaleY = TryFieldRef<TextMeshProUGUI, float>("m_previousLossyScaleY");

    private static readonly AccessTools.FieldRef<TMP_Text, bool> InputParsingRequired = TryFieldRef<TMP_Text, bool>("m_isInputParsingRequired");

    private static readonly bool KeepShownLabels = PreviousLossyScaleY != null && InputParsingRequired != null;

    private static bool rebuildWarned;

    private readonly List<Label> current = new List<Label>();

    private bool subscribed;

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

    private static FontStyles StyleFor(SubtitleChange change)
    {
        if (!string.IsNullOrEmpty(change.Font) && !change.Font.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            return FontStyles.Normal;
        }
        return FontStyles.Bold;
    }

    private void OnEnable()
    {
        if (KeepShownLabels && !subscribed)
        {
            subscribed = true;
            Canvas.willRenderCanvases += RebuildRescaled;
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (subscribed)
        {
            subscribed = false;
            Canvas.willRenderCanvases -= RebuildRescaled;
        }
    }

    private void ShowLabels()
    {
        if (owner == null || main == null || root == null || !main.gameObject.activeInHierarchy || main.alpha <= 0f || timeline == null || (GuiSizes.SinglePlayer && owner.MonitorIndex != 0))
        {
            return;
        }
        float currentMsec = NotesManager.GetCurrentMsec();
        foreach (SubtitleChange item in timeline.At(currentMsec))
        {
            if (labels.TryGetValue(item, out var value) && !(value.Text == null))
            {
                float num = Mathf.Max(0f, (currentMsec - (float)item.Time) * 0.001f);
                bool flag = item.Style.Equals("Typewriter", StringComparison.OrdinalIgnoreCase);
                string text = (flag ? SubtitleCommands.TypewriterText(item.Text, num, Mathf.Max(0f, item.Transition)) : item.Text);
                // Switching a label on, new text and a new colour (TMP_Text.color's own test) each make TMP rebuild it.
                bool rebuilt = !value.Text.enabled;
                if (value.Text.text != text)
                {
                    value.Text.text = text;
                    rebuilt = true;
                }
                Color color = new Color(1f, 1f, 1f, (!flag && item.Transition > 0f) ? Mathf.Clamp01(num / item.Transition) : 1f);
                if (!(value.Text.color == color))
                {
                    rebuilt = true;
                }
                value.Text.color = color;
                value.Text.rectTransform.anchoredPosition = new Vector2(item.X * root.rect.width, (-item.Y) * root.rect.height);
                value.Text.enabled = true;
                value.Rebuilt = rebuilt;
                current.Add(value);
            }
        }
    }

    private void RebuildRescaled()
    {
        try
        {
            foreach (Label previou in previous)
            {
                TextMeshProUGUI text = previou.Text;
                if (text == null || !text.isActiveAndEnabled)
                {
                    continue;
                }
                float y = text.rectTransform.lossyScale.y;
                if (!previou.Rebuilt && (previou.BuiltScale != y || PreviousLossyScaleY(text) != y))
                {
                    InputParsingRequired(text) = true;
                    text.ForceMeshUpdate();
                }
                previou.Rebuilt = false;
                previou.BuiltScale = y;
            }
        }
        catch (Exception ex)
        {
            if (!rebuildWarned)
            {
                rebuildWarned = true;
                MelonLogger.Warning("[Chart Subtitle] Rescaled label rebuild failed: " + ex.Message);
            }
        }
    }
}
