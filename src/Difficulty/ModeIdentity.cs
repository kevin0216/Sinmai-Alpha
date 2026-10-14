using Manager.MaiStudio;
using MelonLoader;
using SinmaiAlpha.Assets;
using SinmaiAlpha.Hosting;
using System.Collections.Generic;
using System.IO;
using System;
using UI;
using UnityEngine.UI;
using MAI2.Util;
using Manager;
using Monitor.MusicSelect.ChainList;
using UnityEngine;

namespace SinmaiAlpha.Difficulty;

public static partial class ExtraDifficulty
{
    internal static string ModeChart(Component owner, string nativeIcon)
    {
        MusicIdentity musicIdentity = Identity(owner, out int onCard);
        if (musicIdentity == null || musicIdentity.Utage || musicIdentity.Id <= 0 || musicIdentity.Id >= 20000 || musicIdentity.Difficulty < 0 || musicIdentity.Difficulty > 4)
        {
            return null;
        }
        int num = musicIdentity.Id;
        if (onCard < 0)
        {
            onCard = (((object)Nearest(owner, CardBuffer) != null) ? 1 : 0);
        }
        if (onCard != 0)
        {
            bool flag = nativeIcon.EndsWith("DeluxeMode", StringComparison.Ordinal);
            if (flag && num < 10000)
            {
                num += 10000;
            }
            else if (!flag && num >= 10000)
            {
                num -= 10000;
            }
        }
        MusicData music = Singleton<DataManager>.Instance.GetMusic(num);
        if (music?.notesData == null || music.notesData.Count <= musicIdentity.Difficulty)
        {
            return null;
        }
        return music.notesData[musicIdentity.Difficulty]?.file?.path;
    }

    private interface IOwned
    {
        UnityEngine.Object OwnerObject { get; }
    }

    private struct Tagged<T> : IOwned where T : UnityEngine.Object
    {
        public T Owner;

        public MusicIdentity Identity;

        public UnityEngine.Object OwnerObject => Owner;
    }

    private static readonly List<int> DeadKeys = new List<int>();

    private static int identityPruneAt = 256;

    private const byte SpriteThemeable = 1;

    private const byte SpriteKop = 2;

    private const byte SpriteUnknown = byte.MaxValue;

    private static readonly string[] InfixTokens = new string[6] { "_MST_Re_", "_MST_", "_EXP_", "_ADV_", "_BSC_", "_BAS_" };

    private static readonly string[] SuffixTokens = new string[6] { "_MST_Re", "_MST", "_EXP", "_ADV", "_BSC", "_BAS" };

    private static readonly Dictionary<int, byte> SpriteFlagCache = new Dictionary<int, byte>();

    private static readonly Dictionary<int, DifficultyTheme> ThemeMemo = new Dictionary<int, DifficultyTheme>();

    private static readonly List<KOP_ResultTrackData> ResultTrackBuffer = new List<KOP_ResultTrackData>();

    private static readonly List<MonitorBase> MonitorBuffer = new List<MonitorBase>();

    private static readonly List<MusicChainCardObejct> CardBuffer = new List<MusicChainCardObejct>();

    private static readonly List<MusicInfomationController> InformationBuffer = new List<MusicInfomationController>();

    private static DifficultyTheme MarkerTheme(string chartPath, int difficulty = 3, int memoKey = -1)
    {
        if (string.IsNullOrWhiteSpace(chartPath))
        {
            return null;
        }
        try
        {
            string directoryName = System.IO.Path.GetDirectoryName(FileSystem.ResolvePath(chartPath));
            if (string.IsNullOrEmpty(directoryName))
            {
                return null;
            }
            string text = FileSystem.ResolvePath(chartPath);
            string key = text + "|" + difficulty;
            if (Markers.TryGetValue(key, out var value))
            {
                if (memoKey >= 0)
                {
                    ThemeMemo[memoKey] = value;
                }
                return value;
            }
            string text2 = System.IO.Path.ChangeExtension(text, ".ExtraDifficulty.flag");
            if (!File.Exists(text2) && difficulty == 3)
            {
                text2 = System.IO.Path.Combine(directoryName, "ExtraDifficulty.flag");
            }
            value = null;
            if (File.Exists(text2))
            {
                string theme = ChartFlags.Parse(File.ReadAllText(text2)).Theme;
                value = DifficultyThemeCatalog.Resolve(Themes, theme);
                if (value == null && !theme.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    MelonLogger.Warning("[ExtraDifficulty] Unknown theme in " + text2 + ": " + theme + "; native appearance retained");
                }
            }
            Markers[key] = value;
            if (memoKey >= 0)
            {
                ThemeMemo[memoKey] = value;
            }
            return value;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (IOException ex3)
        {
            MelonLogger.Warning("[ExtraDifficulty] Marker read failed: " + ex3.Message);
            return null;
        }
        catch (UnauthorizedAccessException ex4)
        {
            MelonLogger.Warning("[ExtraDifficulty] Marker read failed: " + ex4.Message);
            return null;
        }
    }

    private static MusicIdentity Identity(Component owner, out int onCard)
    {
        onCard = -1;
        if (owner == null)
        {
            return new MusicIdentity();
        }
        KOP_ResultTrackData kOP_ResultTrackData = FindResultTrack(owner);
        if (kOP_ResultTrackData != null)
        {
            if (!ResultTracks.TryGetValue(kOP_ResultTrackData.GetInstanceID(), out var value))
            {
                return new MusicIdentity();
            }
            return value.Identity;
        }
        var summary = Nearest(owner, SummaryBuffer);
        if (summary != null) return SummaryCards.TryGetValue(summary.GetInstanceID(), out var summaryIdentity) ? summaryIdentity.Identity : new MusicIdentity();
        MusicChainCardObejct musicChainCardObejct = Nearest(owner, CardBuffer);
        onCard = (((object)musicChainCardObejct != null) ? 1 : 0);
        if (musicChainCardObejct != null)
        {
            if (!Cards.TryGetValue(musicChainCardObejct.GetInstanceID(), out var value2))
            {
                return new MusicIdentity();
            }
            return value2.Identity;
        }
        MusicInfomationController musicInfomationController = Nearest(owner, InformationBuffer);
        if (musicInfomationController != null && Information.TryGetValue(musicInfomationController.GetInstanceID(), out var value3))
        {
            return value3.Identity;
        }
        return MonitorIdentity(Nearest(owner, MonitorBuffer));
    }

    private static T Nearest<T>(Component owner, List<T> buffer) where T : Component
    {
        buffer.Clear();
        owner.GetComponentsInParent(includeInactive: true, buffer);
        T result = ((buffer.Count == 0) ? null : buffer[0]);
        buffer.Clear();
        return result;
    }

    private static void Put<T>(Dictionary<int, Tagged<T>> table, T owner, MusicIdentity identity) where T : Component
    {
        // A destroyed owner reports instance id 0; no live component can look it up, so it is not stored.
        int instanceID = owner.GetInstanceID();
        if (instanceID == 0)
        {
            return;
        }
        table[instanceID] = new Tagged<T>
        {
            Owner = owner,
            Identity = identity
        };
        // Amortised pruning for when ProcessStartPatch is not patched (ExtraDifficulty off): only dead owners go.
        if (Cards.Count + Information.Count + ResultTracks.Count + SummaryCards.Count >= identityPruneAt)
        {
            PruneTables();
            identityPruneAt = Math.Max(256, 2 * (Cards.Count + Information.Count + ResultTracks.Count + SummaryCards.Count));
        }
    }

    private static void PruneTables()
    {
        Prune(Cards);
        Prune(Information);
        Prune(ResultTracks);
        Prune(SummaryCards);
        Prune(Images);
        Prune(Slots);
    }

    private static void Prune<TValue>(Dictionary<int, TValue> table) where TValue : IOwned
    {
        foreach (KeyValuePair<int, TValue> item in table)
        {
            if (item.Value.OwnerObject == null)
            {
                DeadKeys.Add(item.Key);
            }
        }
        for (int i = 0; i < DeadKeys.Count; i++)
        {
            table.Remove(DeadKeys[i]);
        }
        DeadKeys.Clear();
    }

    private static bool HasAppliedSlot(MultipleImage image)
    {
        if (Slots.TryGetValue(image.GetInstanceID(), out var value))
        {
            return value.Applied != null;
        }
        return false;
    }

    private static bool HasAppliedImage(Image image)
    {
        if (Images.TryGetValue(image.GetInstanceID(), out var value))
        {
            return value.Applied != null;
        }
        return false;
    }

    private static bool MayChange(Image image)
    {
        if (image is MultipleImage multipleImage)
        {
            if (HasAppliedSlot(multipleImage))
            {
                return true;
            }
            List<Sprite> multiSprites = multipleImage.MultiSprites;
            if (multiSprites == null)
            {
                return false;
            }
            for (int i = 0; i < multiSprites.Count; i++)
            {
                if ((SpriteFlags(multiSprites[i]) & SpriteThemeable) != 0)
                {
                    return true;
                }
            }
            return false;
        }
        if (!HasAppliedImage(image))
        {
            return (SpriteFlags(image.sprite) & SpriteThemeable) != 0;
        }
        return true;
    }

    private static byte SpriteFlags(Sprite sprite)
    {
        if ((object)sprite == null)
        {
            return 0;
        }
        // Unity 2018.4 returns 0 once the native object is gone; reading .name would throw there.
        int instanceID = sprite.GetInstanceID();
        if (instanceID == 0)
        {
            return SpriteUnknown;
        }
        if (SpriteFlagCache.TryGetValue(instanceID, out var value))
        {
            return value;
        }
        string name = sprite.name;
        if (name == null)
        {
            return SpriteUnknown;
        }
        value = NameFlags(name);
        if (SpriteFlagCache.Count >= 8192)
        {
            SpriteFlagCache.Clear();
        }
        SpriteFlagCache[instanceID] = value;
        return value;
    }

    private static byte NameFlags(string name)
    {
        byte b = 0;
        if (!string.IsNullOrEmpty(name) && !name.Contains("TitleTextImage"))
        {
            for (int i = 0; i < InfixTokens.Length; i++)
            {
                if (name.Contains(InfixTokens[i]) || name.EndsWith(SuffixTokens[i], StringComparison.Ordinal))
                {
                    b |= SpriteThemeable;
                    break;
                }
            }
        }
        if (name != null && name.Contains("KopMBase"))
        {
            b |= SpriteKop;
        }
        return b;
    }

}
