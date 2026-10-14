using Path = System.IO.Path;
using Game;
using MAI2System;
using Manager.MaiStudio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SinmaiAlpha.Hosting;
using SinmaiAlpha.Assets;
using Theme = SinmaiAlpha.Assets.DifficultyTheme;
using HarmonyLib;
using MAI2.Util;
using Manager;
using MelonLoader;
using Monitor;
using Monitor.MusicSelect.ChainList;
using Monitor.MusicSelect.UI;
using Process;
using UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Difficulty;


public static partial class ExtraDifficulty
{

    public static readonly string strongTextureDir = "Sinmai-Alpha/ExtraDifficulty";

    public static readonly bool taggedMasterSkin = true;
    public const string MarkerFileName = "ExtraDifficulty.flag";

    private const int Master = 3;
    private static List<Theme> themes;
    private static List<Theme> Themes=>themes??(themes=DifficultyThemeCatalog.Read(FileSystem.ResolvePath(strongTextureDir),JsonUtility.FromJson<Theme>,m=>MelonLogger.Warning("[ExtraDifficulty] "+m)));
    private sealed class MusicIdentity { public int Id = -1; public int Difficulty = -1; public bool Utage; }
    private sealed class ImageLease : IOwned
    {
        public Sprite Original, Applied;

        public Image Owner;

        public UnityEngine.Object OwnerObject => Owner;
    }
    private sealed class SlotLease : IOwned
    {
        public Sprite Original, Applied, VisibleOriginal; public int Index = -1;

        public MultipleImage Owner;

        public UnityEngine.Object OwnerObject => Owner;
    }
    private static readonly Dictionary<string, Theme> Markers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> Sprites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, Tagged<MusicChainCardObejct>> Cards = new Dictionary<int, Tagged<MusicChainCardObejct>>();
    private static readonly Dictionary<int, Tagged<MusicInfomationController>> Information = new Dictionary<int, Tagged<MusicInfomationController>>();
    private static readonly Dictionary<int, Tagged<KOP_ResultTrackData>> ResultTracks = new Dictionary<int, Tagged<KOP_ResultTrackData>>();
    private static readonly Dictionary<int, Tagged<SingleResultCardController>> SummaryCards = new();
    private static readonly List<SingleResultCardController> SummaryBuffer = new();
    private static readonly Dictionary<int, ImageLease> Images = new Dictionary<int, ImageLease>();
    private static readonly Dictionary<int, SlotLease> Slots = new Dictionary<int, SlotLease>();
    [ThreadStatic] private static Theme levelTheme;
    [ThreadStatic] private static MusicIdentity menuIdentity;
    private static readonly Dictionary<Theme, Sprite[]> NumberSheets = new();
    private static readonly HashSet<Theme> NumberSheetsChecked = new();
    private static bool loaded;
    private static bool useLegacyUpperFrame;

    public static void OnBeforePatch()
    {
        Markers.Clear();
        ThemeMemo.Clear();
        themes = null;
        LevelFlags.Clear();
        LevelTextWarnings.Clear();
        try
        {
            FieldInfo field = typeof(ConstParameter).GetField("NowGameVersion", BindingFlags.Static | BindingFlags.Public);
            useLegacyUpperFrame = field != null && Convert.ToUInt32(field.GetRawConstantValue()) < 27000;
        }
        catch (Exception ex)
        {
            useLegacyUpperFrame = false;
            MelonLogger.Warning("[ExtraDifficulty] Cannot detect upper-frame layout, using current textures: " + ex.Message);
        }
    }
    private static bool HasMarker(string chartPath) => MarkerTheme(chartPath) != Theme.None;

    private static bool IsTagged(MusicIdentity identity, int difficulty) => ThemeFor(identity, difficulty) != Theme.None;
    private static DifficultyTheme ThemeFor(MusicIdentity identity, int difficulty)
    {
        if (!Settings.Current.ExtraDifficulty || !taggedMasterSkin || identity == null || identity.Utage || difficulty < 0 || difficulty > 4 || identity.Id <= 0 || identity.Id >= 100000)
        {
            return null;
        }
        MusicData music = Singleton<DataManager>.Instance.GetMusic(identity.Id);
        if (music?.notesData == null || music.notesData.Count <= difficulty)
        {
            return null;
        }
        // The chart path of a loaded MusicData never changes, so (id, difficulty) always maps to the same Markers
        // entry; the memo skips the two path canonicalisations MarkerTheme does before its own lookup.
        int num = identity.Id * 8 + difficulty;
        if (!ThemeMemo.TryGetValue(num, out var difficultyTheme))
        {
            difficultyTheme = MarkerTheme(music.notesData[difficulty]?.file?.path, difficulty, num);
        }
        if (difficultyTheme == null)
        {
            return difficultyTheme;
        }
        LoadTextures();
        return difficultyTheme;
    }
    private static T Field<T>(object owner, string name) => Traverse.Create(owner).Field(name).GetValue<T>();
    private static Sprite SpriteFor(string name,Theme theme)
    {
        if(theme==null||name==null)return null;
        // Central resolution also covers the generic Image/MultipleImage scans
        // and the result animation callback, not only SetDifficulty.
        if (useLegacyUpperFrame && name == "UI_UPE_MBase_" + ThemeCode(theme))
        {
            var legacyName = name + "_Old";
            if (Sprites.TryGetValue(theme.Id + "/" + legacyName, out var legacy) ||
                Sprites.TryGetValue("/" + legacyName, out legacy)) return legacy;
        }
        if(Sprites.TryGetValue(theme.Id+"/"+name,out var sprite))return sprite;
        return Sprites.TryGetValue("/"+name,out sprite)?sprite:null;
    }

    private static void LoadTextures()
    {
        if (loaded) return;
        loaded = true;
        var folder = FileSystem.ResolvePath(strongTextureDir);
        if (!Directory.Exists(folder)) { MelonLogger.Warning("[ExtraDifficulty] Texture directory missing: " + folder); return; }
        // Load legacy root first; the new theme folders override matching files.
        LoadFolder(folder, "");
        foreach (var theme in Themes)
            LoadFolder(Path.Combine(folder, theme.Id),theme.Id);
    }
    private static void LoadFolder(string folder,string themeId)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (!Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file).StartsWith("_dump_")) continue;
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(File.ReadAllBytes(file))) { Object.Destroy(texture); continue; }
                PrepareTextureEdges(texture);
                var name = Path.GetFileNameWithoutExtension(file);
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                sprite.name = name;
                var key=themeId+"/"+name;
                if (Sprites.TryGetValue(key, out var previous)) { Object.Destroy(previous.texture); Object.Destroy(previous); }
                Sprites[key] = sprite;
            }
            catch (Exception error) { if (texture != null) Object.Destroy(texture); MelonLogger.Warning("[ExtraDifficulty] " + Path.GetFileName(file) + ": " + error.Message); }
        }
    }
    private static string ThemeCode(Theme theme) => theme?.TextureCode;
    private static string ThemeNumber(Theme theme) => theme?.TextureNumber;
    private static Sprite[] NumberSheet(Theme theme)
    {
        if (theme == Theme.None) return null;
        if (NumberSheets.TryGetValue(theme, out var numberSheet)) return numberSheet;
        if (!NumberSheetsChecked.Add(theme)) return null;
        LoadTextures();
        var name = "UI_NUM_MLevel_" + ThemeNumber(theme);
        var source = SpriteFor(name,theme);
        if (source == null || source.texture == null) return null;
        var texture = source.texture;
        if (texture.width != 192 || texture.height != 240)
        { MelonLogger.Warning("[ExtraDifficulty] " + name + " must be 192x240; native numbers retained"); return null; }
        numberSheet = new Sprite[15];
        for (var i = 0; i < numberSheet.Length; i++)
        {
            numberSheet[i] = Sprite.Create(texture, new Rect(i % 4 * 48, 240 - (i / 4 + 1) * 60, 48, 60),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            numberSheet[i].name = name + "_" + i;
        }
        NumberSheets[theme] = numberSheet;
        return numberSheet;
    }
    private static Sprite[] NativeSheet(int difficulty, bool utage = false)
    {
        if (utage) return CommonPrefab.GetUtageMusicLevelSprites();
        var table = Field<MusicDifficultySheet>(typeof(CommonPrefab), "_musicLevelSheetTable");
        if (table?.MusicLevelSprites == null || difficulty < 0 || difficulty >= table.MusicLevelSprites.Count) return null;
        return table.MusicLevelSprites[difficulty].Sheet;
    }
    private static string ThemeName(string name, Theme theme)
    {
        var code = ThemeCode(theme);
        if (code == null || string.IsNullOrEmpty(name) || name.Contains("TitleTextImage")) return null;
        // Restrict mappings to difficulty artwork. Unrelated *_Text and Lv
        // images must not be replaced with a song-card label.
        foreach (var native in new[] { "MST_Re", "MST", "EXP", "ADV", "BSC", "BAS" })
        {
            if (name.Contains("_" + native + "_")) return name.Replace("_" + native + "_", "_" + code + "_");
            if (name.EndsWith("_" + native, StringComparison.Ordinal)) return name.Substring(0, name.Length - native.Length) + code;
        }
        // UI_MSS_Btn_01_* is shared by difficulty and option controls.
        // Only DifficultyButtonPatch may replace those button backgrounds.
        return null;
    }
    private static string DifficultyButtonName(string name, Theme theme)
    {
        var number = ThemeNumber(theme);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(number)) return null;
        for (var i = 1; i <= 5; i++)
        {
            var prefix = "UI_MSS_Btn_01_" + i.ToString("D2") + "_";
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return "UI_MSS_Btn_01_" + number + "_" + name.Substring(prefix.Length);
        }
        return null;
    }
    private static bool IsDifficultyButtonBase(Image image)
    {
        // Include inactive controls populated before the select screen opens.
        foreach (var button in image.GetComponentsInParent<DifficultyButtonObject>(true))
            if (Field<Image>(button, "_baseImage") == image) return true;
        return false;
    }
    [HarmonyPatch(typeof(DifficultyButtonObject), "ChangeDifficulty")]
    public static class DifficultyButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DifficultyButtonObject __instance, Sprite __1, int __3)
        {
            // ThemeFor is null under the gate (noThemes): this is ApplyImage(image, null) with no lease to restore.
            if (noThemes)
            {
                return;
            }
            DifficultyTheme theme = ThemeFor(Identity(__instance), __3);
            ApplyImage(Field<Image>(__instance, "_baseImage"), SpriteFor(DifficultyButtonName(__1?.name, theme), theme));
        }
    }
    private static void ApplyImage(Image image, Sprite replacement)
    {
        if (!(image == null))
        {
            int instanceID = image.GetInstanceID();
            Images.TryGetValue(instanceID, out var value);
            if (value != null && value.Applied != null && image.sprite == value.Applied)
            {
                image.sprite = value.Original;
            }
            if (replacement == null)
            {
                if (value != null)
                {
                    Images.Remove(instanceID);
                }
                return;
            }
            if (value == null)
            {
                value = new ImageLease
                {
                    Owner = image
                };
                Images[instanceID] = value;
            }
            value.Original = image.sprite;
            value.Applied = replacement;
            image.sprite = replacement;
        }
    }
    private static void ApplySlot(MultipleImage image, DifficultyTheme theme, string explicitName = null, bool forceVisible = false, int slot = 3)
    {
        if (image == null || image.MultiSprites == null)
        {
            return;
        }
        List<Sprite> multiSprites = image.MultiSprites;
        int instanceID = image.GetInstanceID();
        Slots.TryGetValue(instanceID, out var value);
        if (value != null)
        {
            Sprite applied = value.Applied;
            if (applied != null && value.Index >= 0 && value.Index < multiSprites.Count && multiSprites[value.Index] == applied)
            {
                multiSprites[value.Index] = value.Original;
            }
            if (applied != null && image.sprite == applied)
            {
                image.sprite = value.VisibleOriginal;
            }
            value.Applied = null;
        }
        Sprite sprite = ((slot >= 0 && slot < multiSprites.Count && theme != null) ? SpriteFor(explicitName ?? ThemeName(multiSprites[slot]?.name, theme), theme) : null);
        if (sprite == null)
        {
            if (value != null)
            {
                Slots.Remove(instanceID);
            }
            return;
        }
        if (value == null)
        {
            value = new SlotLease
            {
                Owner = image
            };
            Slots[instanceID] = value;
        }
        value.Index = slot;
        value.Original = multiSprites[slot];
        value.Applied = sprite;
        multiSprites[slot] = sprite;
        value.VisibleOriginal = value.Original;
        if ((image.sprite == value.Original) | forceVisible)
        {
            value.VisibleOriginal = image.sprite;
            image.sprite = sprite;
        }
    }
    private static MusicIdentity Selected(IMusicSelectProcess process)
    {
        var data = process?.GetCombineMusic(0);
        return new MusicIdentity { Id = data == null ? -1 : data.GetID(process.ScoreType) };
    }
    private static MusicIdentity MonitorIdentity(MonitorBase monitor)
    {
        if (monitor == null) return new MusicIdentity();
        if (monitor is MusicSelectMonitor selection) return Selected(Field<IMusicSelectProcess>(selection, "_musicSelect"));
        var index = monitor.MonitorIndex;
        if ((monitor is GameMonitor || monitor is ResultMonitor) && GameManager.MusicTrackNumber > 0)
        {
            var score = Singleton<GamePlayManager>.Instance.GetGameScore(index);
            if (score != null && score.SessionInfo.musicId > 0)
            {
                var session = score.SessionInfo;
                return new MusicIdentity { Id = session.musicId, Difficulty = session.difficulty, Utage = session.musicId >= 100000 };
            }
        }
        var music = GameManager.SelectMusicID; var difficulty = GameManager.SelectDifficultyID;
        return new MusicIdentity { Id = music != null && index >= 0 && index < music.Length ? music[index] : -1,
            Difficulty = difficulty != null && index >= 0 && index < difficulty.Length ? difficulty[index] : -1,
            Utage = GameManager.IsUtage };
    }
    private static MusicIdentity Identity(Component owner)
    {
        return Identity(owner, out _);
    }
    private static KOP_ResultTrackData FindResultTrack(Component owner)
    {
        return Nearest(owner, ResultTrackBuffer);
    }
    private static void Register(MusicChainCardObejct card, MusicIdentity identity)
    {
        if (!(card == null))
        {
            // Always registered: ExtraMode.ModeChart reads Cards for the score-kind icons even without themes.
            Put(Cards, card, identity);
            MusicChainCardObejct musicChainCardObejct = Field<MusicChainCardObejct>(card, "_miniCard");
            if (musicChainCardObejct != null)
            {
                Put(Cards, musicChainCardObejct, identity);
            }
            // The rest only themes: ThemeFor is null for every chart while noThemes, and no slot lease can exist.
            if (noThemes)
            {
                return;
            }
            DifficultyTheme theme = ThemeFor(identity, identity.Difficulty);
            string text = ThemeCode(theme);
            ApplySlot(Field<MultipleImage>(card, "_difficultyText"), theme, "UI_MSS_MBase_" + text + "_Text", forceVisible: false, Math.Max(0, identity.Difficulty));
            ApplySlot(Field<MultipleImage>(card, "_difficultyBase"), theme, "UI_MSS_MBase_LvBase_" + text, forceVisible: false, Math.Max(0, identity.Difficulty));
        }
    }
    private static void PrepareNumbers(object owner, int difficulty, bool utage, Theme theme)
    {
        BindLevelText(owner, difficulty, utage, theme);
        var sheet = NumberSheet(theme) ?? NativeSheet(difficulty, utage);
        if (sheet == null) return;
        foreach (var name in new[] { "_digitLevel", "_doubleDigitLevel", "_difficultySingle", "_difficultyDouble", "_singleLevel", "_doubleLevel" })
        {
            var counter = Field<SpriteCounter>(owner, name);
            if (counter == null) continue;
            counter.SetSpriteSheet(sheet);
            // SetAllDirty does not recompute SpriteCounter's stored UVs.
            var text = Field<string>(counter, "mainText");
            if (text != null) counter.ChangeText(text);
        }
        var image = Field<Image>(owner, "_levelTextImage");
        if (image != null && sheet.Length > 14) image.sprite = sheet[14];
    }
    private static void Scan(MonitorBase monitor)
        => ScanImages(monitor);
    private static void ScanImages(Component owner)
    {
        if (owner == null)
        {
            return;
        }
        Image[] componentsInChildren = owner.GetComponentsInChildren<Image>(includeInactive: true);
        foreach (Image image in componentsInChildren)
        {
            // MayChange first: all three tests are side-effect free and only lead to continue, and it is the cheap one.
            if (!MayChange(image) || IsDifficultyButtonBase(image) || IsDifficultyHeaderImage(image))
            {
                continue;
            }
            MusicIdentity musicIdentity = Identity(image);
            DifficultyTheme theme = ThemeFor(musicIdentity, musicIdentity.Difficulty);
            if (image is MultipleImage multipleImage)
            {
                bool flag = FindResultTrack(image) != null && multipleImage.MultiSprites != null && multipleImage.MultiSprites.Count > 3 && (multipleImage.MultiSprites[3]?.name.Contains("KopMBase") ?? false);
                ApplySlot(multipleImage, theme, null, flag, flag ? 3 : Math.Max(0, musicIdentity.Difficulty));
                continue;
            }
            if (Images.TryGetValue(image.GetInstanceID(), out var value) && image.sprite == value.Applied)
            {
                ApplyImage(image, null);
            }
            ApplyImage(image, SpriteFor(ThemeName(image.sprite?.name, theme), theme));
        }
    }
    [HarmonyPatch(typeof(Graphic), "OnDidApplyAnimationProperties")]
    public static class ResultAnimationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Graphic __instance)
        {
            // MayChange is side-effect free and false only when the body below could neither restore nor apply a sprite.
            if (noThemes || !(__instance is Image image) || !MayChange(image) || (image.GetComponentInParent<ResultMonitor>() == null && FindResultTrack(image) == null && Nearest(image, SummaryBuffer) == null))
            {
                return;
            }
            MusicIdentity musicIdentity = Identity(image);
            DifficultyTheme theme = ThemeFor(musicIdentity, musicIdentity.Difficulty);
            if (image is MultipleImage multipleImage)
            {
                bool flag = multipleImage.MultiSprites != null && multipleImage.MultiSprites.Count > 3 && (multipleImage.MultiSprites[3]?.name.Contains("KopMBase") ?? false);
                ApplySlot(multipleImage, theme, null, flag, flag ? 3 : Math.Max(0, musicIdentity.Difficulty));
                return;
            }
            if (Images.TryGetValue(image.GetInstanceID(), out var value) && image.sprite == value.Applied)
            {
                ApplyImage(image, null);
            }
            ApplyImage(image, SpriteFor(ThemeName(image.sprite?.name, theme), theme));
        }
    }
    private static IEnumerator ScanLater(MonitorBase monitor, MusicIdentity identity)
    {
        float[] array = new float[2] { 0.6f, 1.2f };
        foreach (float seconds in array)
        {
            yield return new WaitForSeconds(seconds);
            // ThemeFor is null under the gate (noThemes): a rescan would find nothing to theme or restore.
            if (noThemes || monitor == null)
            {
                break;
            }
            MusicIdentity musicIdentity = MonitorIdentity(monitor);
            if (musicIdentity.Id != identity.Id || musicIdentity.Difficulty != identity.Difficulty)
            {
                break;
            }
            Scan(monitor);
        }
    }

    [HarmonyPatch(typeof(MusicSelectChainList), "SetChainData")]
    public static class CardDataPatch
    {
        [HarmonyPrefix] public static void Prefix(MusicSelectChainList __instance, MusicChainCardObejct __0, int __1, int __3)
        {
            var identity = new MusicIdentity { Difficulty = __3 };
            // Clear pooled ownership before resolving, including negative list
            // offsets. A failed lookup must never retain the preceding song.
            Register(__0, identity);
            var process = Field<IMusicSelectProcess>(__instance, "SelectProcess");
            var data = process?.GetCombineMusic(__1);
            identity.Id = data == null ? -1 : data.GetID(process.ScoreType); identity.Utage = identity.Id >= 100000;
            Register(__0, identity);
        }
    }
    [HarmonyPatch(typeof(KOP_ResultTrackData), "SetMusicData")]
    public static class ResultTrackDataPatch
    {
        [HarmonyPrefix]
        public static void Prefix(KOP_ResultTrackData __instance, int __0)
        {
            MusicIdentity musicIdentity = new MusicIdentity();
            MonitorBase[] componentsInParent = __instance.GetComponentsInParent<MonitorBase>(includeInactive: true);
            MonitorBase monitorBase = ((componentsInParent.Length == 0) ? null : componentsInParent[0]);
            if (monitorBase != null && monitorBase.MonitorIndex >= 0 && monitorBase.MonitorIndex < 4 && __0 >= 0)
            {
                GameScoreList gameScore = Singleton<GamePlayManager>.Instance.GetGameScore(monitorBase.MonitorIndex, __0);
                if (gameScore != null)
                {
                    SessionInfo sessionInfo = gameScore.SessionInfo;
                    musicIdentity.Id = sessionInfo.musicId;
                    musicIdentity.Difficulty = sessionInfo.difficulty;
                    musicIdentity.Utage = sessionInfo.musicId >= 100000;
                }
            }
            // Not gated by noThemes: LevelPatch reads this identity's Utage for KOP_ResultTrackData.SetDifficultyLevel
            // and picks the native or utage number sheet from it, and ExtraMode.ModeChart reads it for KOP icons.
            Put(ResultTracks, __instance, musicIdentity);
        }
    }
    [HarmonyPatch(typeof(PhotoEditProcess), "SetData")]
    public static class SummaryDataPatch
    {
        // This is the ordinary three-track summary, separate from KOP history.
        // Native SetData indexes [player, track], then fills monitorIndex. Only
        // isMyself updates its SingleResultCard; the other call fills a rival.
        [HarmonyPrefix]
        public static void Prefix(int __0, int __1, int __2, bool __4,
            PhotoEditMonitor[] ____monitors, GameScoreList[,] ____gameScoreLists)
        {
            if (!__4 || ____monitors == null || __0 < 0 || __0 >= ____monitors.Length) return;
            var monitor = ____monitors[__0];
            if (monitor == null) return;
            foreach (var card in monitor.GetComponentsInChildren<SingleResultCardController>(true))
            {
                var identity = new MusicIdentity();
                if (____gameScoreLists != null && __1 >= 0 && __1 < ____gameScoreLists.GetLength(0) &&
                    __2 >= 0 && __2 < ____gameScoreLists.GetLength(1))
                {
                    var score = ____gameScoreLists[__1, __2];
                    if (score != null)
                    {
                        var session = score.SessionInfo;
                        identity.Id = session.musicId; identity.Difficulty = session.difficulty;
                        identity.Utage = session.musicId >= 100000;
                    }
                }
                Put(SummaryCards, card, identity);
            }
        }
    }
    [HarmonyPatch]
    public static class MenuPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        { yield return AccessTools.Method(typeof(MenuChainList), "Deploy"); yield return AccessTools.Method(typeof(MenuChainList), "ChangeDifficulty"); }
        [HarmonyPrefix] private static void Prefix(MenuChainList __instance, out MusicIdentity __state)
        { __state = menuIdentity; menuIdentity = Selected(Field<IMusicSelectProcess>(__instance, "SelectProcess")); }
        [HarmonyFinalizer] private static void Finalizer(MusicIdentity __state) => menuIdentity = __state;
    }
    [HarmonyPatch(typeof(MusicChainCardObejct), "SetMusicData")]
    public static class CentralCardPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MusicChainCardObejct __instance, int __5)
        {
            // Not gated by noThemes (nor MenuPatch, CardDataPatch, InformationDataPatch's Put): ExtraMode.ModeChart
            // reads these identities whatever the flag files say. Register gates its own theme work.
            Tagged<MusicChainCardObejct> value;
            if (menuIdentity != null && __instance.GetComponentsInParent<MenuCardObject>(includeInactive: true).Length != 0)
            {
                Register(__instance, new MusicIdentity
                {
                    Id = menuIdentity.Id,
                    Difficulty = __5,
                    Utage = (menuIdentity.Id >= 100000)
                });
            }
            else if (Cards.TryGetValue(__instance.GetInstanceID(), out value))
            {
                Register(__instance, new MusicIdentity
                {
                    Id = value.Identity.Id,
                    Difficulty = __5,
                    Utage = value.Identity.Utage
                });
            }
        }
    }
    [HarmonyPatch(typeof(MusicChainCardObejct), "SetDifficulty")]
    public static class CardBackgroundPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MusicChainCardObejct __instance, Sprite __0, Sprite __1, Sprite __2)
        {
            // ThemeFor is null under the gate (noThemes): three ApplyImage(image, null) with no lease to restore.
            if (noThemes)
            {
                return;
            }
            MusicIdentity musicIdentity = Identity(__instance);
            DifficultyTheme theme = ThemeFor(musicIdentity, musicIdentity.Difficulty);
            ApplyImage(Field<Image>(__instance, "_background"), SpriteFor(ThemeName(__0?.name, theme), theme));
            ApplyImage(Field<Image>(__instance, "_tagBaseFront"), SpriteFor(ThemeName(__1?.name, theme), theme));
            ApplyImage(Field<Image>(__instance, "_tagBaseBack"), SpriteFor(ThemeName(__2?.name, theme), theme));
        }
    }

    [HarmonyPatch(typeof(MusicInfomationController), "SetMusicData")]
    public static class InformationDataPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MusicInfomationController __instance, MessageMusicData __0)
        {
            Put(Information, __instance, new MusicIdentity
            {
                Id = (__0?.MusicId ?? (-1)),
                Difficulty = (__0?.MusicDifficultyID ?? (-1)),
                Utage = (__0 != null && __0.MusicId >= 100000)
            });
            if (__0 == null && Settings.Current.ExtraDifficulty)
            {
                // Without a flag file no theme was ever applied, so the two ApplyImage(null) have nothing to restore;
                // the native sheet still applies.
                if (!noThemes)
                {
                    ApplyImage(Field<Image>(__instance, "difficultyImage"), null);
                    ApplyImage(Field<Image>(__instance, "musicJacketBgImage"), null);
                }
                PrepareNumbers(__instance, 3, utage: false, null);
            }
        }
    }
    [HarmonyPatch(typeof(MusicInfomationController), "SetDifficulty")]
    public static class InformationDifficultyPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MusicInfomationController __instance, MusicDifficultyID difficulty, bool isUtage)
        {
            // ThemeFor is null under the gate (noThemes): two ApplyImage(image, null) with no lease to restore.
            if (noThemes)
            {
                return;
            }
            DifficultyTheme theme = ((!isUtage) ? ThemeFor(Identity(__instance), (int)difficulty) : null);
            string text = ThemeCode(theme);
            ApplyImage(Field<Image>(__instance, "difficultyImage"), SpriteFor("UI_UPE_MBase_" + text, theme));
            ApplyImage(Field<Image>(__instance, "musicJacketBgImage"), SpriteFor("UI_UPE_MusicJacket_Base_" + text, theme));
        }
    }
    [HarmonyPatch]
    public static class LevelPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MusicChainCardObejct), "SetLevel");
            yield return AccessTools.Method(typeof(ResultMonitor), "SetLevel");
            yield return AccessTools.Method(typeof(SingleResultCardController), "SetLevel");
            yield return AccessTools.Method(typeof(KOP_ResultTrackData), "SetDifficultyLevel");
            yield return AccessTools.Method(typeof(MusicInfomationController), "Setlevel");
            yield return AccessTools.Method(typeof(TrackStartMonitor), "SetTrackStart");
        }
        private static void Context(Component owner, MethodBase method, object[] args, out int difficulty, out bool utage)
        {
            var identity = Identity(owner); difficulty = identity.Difficulty; utage = identity.Utage;
            if (method.Name == "SetLevel") { difficulty = (int)args[2]; utage = (bool)args[3]; }
            else if (method.Name == "SetDifficultyLevel") { difficulty = (int)args[2]; }
            else if (method.Name == "Setlevel") { difficulty = (int)args[1]; utage = (bool)args[2]; }
        }
        [HarmonyPrefix]
        public static void Prefix(Component __instance, MethodBase __originalMethod, object[] __args, out DifficultyTheme __state)
        {
            __state = levelTheme;
            Context(__instance, __originalMethod, __args, out var difficulty, out var utage);
            // ThemeFor is null under the gate (noThemes), so levelTheme is null without the lookup.
            levelTheme = ((!noThemes && !utage) ? ThemeFor(Identity(__instance), difficulty) : null);
            PrepareNumbers(__instance, difficulty, utage, levelTheme);
        }
        [HarmonyPostfix]
        public static void Postfix(Component __instance, MethodBase __originalMethod, object[] __args)
        {
            Context(__instance, __originalMethod, __args, out var difficulty, out var utage);
            PrepareNumbers(__instance, difficulty, utage, levelTheme);
            // ThemeFor is null under the gate (noThemes): Scan/ScanImages would find nothing to theme or restore.
            if (noThemes)
            {
                return;
            }
            if (__instance is MonitorBase monitor)
            {
                Scan(monitor);
            }
            else if (__instance is KOP_ResultTrackData || __instance is SingleResultCardController)
            {
                ScanImages(__instance);
            }
        }
        [HarmonyFinalizer] public static void Finalizer(Theme __state) => levelTheme = __state;
    }
    [HarmonyPatch(typeof(CommonPrefab), "GetMusicLevelSprites")]
    public static class CommonNumbersPatch
    {
        [HarmonyPrefix] public static bool Prefix(int difficulty, ref Sprite[] __result)
        {
            if (levelTheme == Theme.None || difficulty < 0 || difficulty > 4) return true;
            var sheet = NumberSheet(levelTheme); if (sheet == null) return true;
            __result = sheet; return false;
        }
    }
    [HarmonyPatch(typeof(MultipleImage), "ChangeSprite")]
    public static class MultiplePatch
    {
        [HarmonyPrefix]
        public static void Prefix(MultipleImage __instance, int index)
        {
            if (noThemes)
            {
                return;
            }
            // ApplySlot can only restore an applied lease or theme MultiSprites[index] when its name carries a
            // difficulty token, so every other call (gameplay rank/sync icons, every frame in 2P) is skipped.
            List<Sprite> multiSprites = __instance.MultiSprites;
            if (!HasAppliedSlot(__instance) && (multiSprites == null || index < 0 || index >= multiSprites.Count || (SpriteFlags(multiSprites[index]) & SpriteThemeable) == 0))
            {
                return;
            }
            ApplySlot(__instance, ThemeFor(Identity(__instance), index), null, forceVisible: false, index);
        }
        [HarmonyPostfix]
        public static void Postfix(MultipleImage __instance)
        {
            if (noThemes)
            {
                return;
            }
            // Same conditions as before, cheapest first; the walk now runs only for KopMBase sprites.
            List<Sprite> multiSprites = __instance.MultiSprites;
            if (multiSprites == null || multiSprites.Count <= 3)
            {
                return;
            }
            Sprite sprite = multiSprites[3];
            if ((object)sprite == null)
            {
                return;
            }
            byte flags = SpriteFlags(sprite);
            if ((flags & SpriteKop) != 0 && !(FindResultTrack(__instance) == null) && (flags != SpriteUnknown || sprite.name.Contains("KopMBase")))
            {
                MusicIdentity musicIdentity = Identity(__instance);
                ApplySlot(__instance, ThemeFor(musicIdentity, musicIdentity.Difficulty), null, forceVisible: true);
            }
        }
    }
    [HarmonyPatch]
    public static class ProcessStartPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(TrackStartProcess), "OnStart");
            yield return AccessTools.Method(typeof(GameProcess), "OnStart");
            yield return AccessTools.Method(typeof(ResultProcess), "OnStart");
        }
        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            // Drop entries whose owner died with an earlier process. Done at the start of the next process,
            // never on release: a FadeProcess runs this OnStart a frame before the outgoing OnRelease.
            PruneTables();
            if (noThemes)
            {
                return;
            }
            MonitorBase[] array = Field<MonitorBase[]>(__instance, "_monitors");
            if (array == null)
            {
                return;
            }
            MonitorBase[] array2 = array;
            foreach (MonitorBase monitorBase in array2)
            {
                if (!(monitorBase == null))
                {
                    Scan(monitorBase);
                    MelonCoroutines.Start(ScanLater(monitorBase, MonitorIdentity(monitorBase)));
                }
            }
        }
    }
}
