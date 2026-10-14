using SinmaiAlpha.ChartVisuals;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SinmaiAlpha.Hosting;
using HarmonyLib;
using Manager;
using MelonLoader;
using UnityEngine;
using SinmaiAlpha.Notes.Libs;

namespace SinmaiAlpha.Notes;

// 读取上下文、MA2 扩展记录表注册及地雷贴图加载。
public partial class CustomNoteTypes
{

    // Hyper Speed: per-note speed overrides keyed by the actual NoteData instance and by note index.
    private static Dictionary<NoteData, float> NoteSpeedMultipliers => RuntimeCharts.Current.NoteSpeedMultipliers;
    private static Dictionary<int, float> NoteSpeedByIndex => RuntimeCharts.Current.NoteSpeedByIndex;
    private static Queue<float> PendingNoteSpeedMultipliers => RuntimeCharts.Current.PendingNoteSpeedMultipliers;
    private static Queue<float> PendingTouchSpeedMultipliers => RuntimeCharts.Current.PendingTouchSpeedMultipliers;
    private static NoteData _currentNoteData { get => RuntimeCharts.Current._currentNoteData; set => RuntimeCharts.Current._currentNoteData = value; }
    private static int _noteCountBeforeLoad { get => RuntimeCharts.Current._noteCountBeforeLoad; set => RuntimeCharts.Current._noteCountBeforeLoad = value; }
    private static float? _optionalSpeedForCurrentLoad { get => RuntimeCharts.Current._optionalSpeedForCurrentLoad; set => RuntimeCharts.Current._optionalSpeedForCurrentLoad = value; }
    private static string _removedOptionalSpeedString { get => RuntimeCharts.Current._removedOptionalSpeedString; set => RuntimeCharts.Current._removedOptionalSpeedString = value; }
    private static bool _optionalSpeedRemoved { get => RuntimeCharts.Current._optionalSpeedRemoved; set => RuntimeCharts.Current._optionalSpeedRemoved = value; }
    // 可重叠流类型键（行尾 s{N} 字段）：当前 loadNote 的音符所属流；非流内音符为 null。
    private static string _optionalStreamIdForCurrentLoad { get => RuntimeCharts.Current._optionalStreamIdForCurrentLoad; set => RuntimeCharts.Current._optionalStreamIdForCurrentLoad = value; }
    private static string _removedOptionalStreamString { get => RuntimeCharts.Current._removedOptionalStreamString; set => RuntimeCharts.Current._removedOptionalStreamString = value; }
    private static bool _optionalStreamRemoved { get => RuntimeCharts.Current._optionalStreamRemoved; set => RuntimeCharts.Current._optionalStreamRemoved = value; }

    private static Dictionary<int, CustomNoteKind> NoteKinds => RuntimeCharts.Current.NoteKinds;

    // 当前谱面 NoteData 列表缓存（loadMa2Main postfix 填充；地雷 hold 触碰检测用——
    // 判定同轨道普通 tap 是否在判定窗口内，让点击优先判定给 tap）。
    private static List<Manager.NoteData> _activeNoteList { get => RuntimeCharts.Current._activeNoteList; set => RuntimeCharts.Current._activeNoteList = value; }

    private static bool _pendingMineForCurrentLoad { get => RuntimeCharts.Current._pendingMineForCurrentLoad; set => RuntimeCharts.Current._pendingMineForCurrentLoad = value; }
    private static bool _pendingTouchBreakForCurrentLoad { get => RuntimeCharts.Current._pendingTouchBreakForCurrentLoad; set => RuntimeCharts.Current._pendingTouchBreakForCurrentLoad = value; }
    private static bool _pendingTouchStarForCurrentLoad { get => RuntimeCharts.Current._pendingTouchStarForCurrentLoad; set => RuntimeCharts.Current._pendingTouchStarForCurrentLoad = value; }

    // findID is called in a batch before loadNote, so use queues to keep mine flags in chart order.




    public static void OnAfterPatch()
    {
        ChartFeatureGate.CaptureNativeRecords();
        AllNoteTypeNames.UnionWith(KnownMa2FieldCounts.Keys);
        AllNoteTypeNames.UnionWith(MineToBaseMap.Keys);
        AllNoteTypeNames.Add("BRTTP");
        AllNoteTypeNames.Add("BRTHO");
        AllNoteTypeNames.Add("MBTTP");
        AllNoteTypeNames.Add("MBTHO");
        AllNoteTypeNames.Add("NMSTP");
        AllNoteTypeNames.Add("BRSTP");
        Traverse traverse = Traverse.Create(typeof(Ma2fileRecordID)).Field("s_Ma2fileRecord_Data");
        Array value = traverse.GetValue<Array>();
        int length = value.Length;
        List<object[]> list = new List<object[]>(20);
        list.Add(new object[14]
        {
            length++,
            "NMSSS",
            "过新过热Slide",
            NotesTypeID.Def.Slide,
            SlideType.Slide_MAX,
            8,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "BRSSS",
            "过新过热BreakSlide",
            NotesTypeID.Def.BreakSlide,
            SlideType.Slide_MAX,
            8,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MNTAP",
            "地雷Tap",
            NotesTypeID.Def.Begin,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MNHLD",
            "地雷Hold",
            NotesTypeID.Def.Hold,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MNSTR",
            "地雷Star",
            NotesTypeID.Def.Star,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MNSSS",
            "地雷Slide",
            NotesTypeID.Def.Slide,
            SlideType.Slide_MAX,
            8,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MNTTP",
            "地雷Touch",
            NotesTypeID.Def.TouchTap,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MBTTP",
            "地雷绝赞Touch",
            NotesTypeID.Def.TouchTap,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MNTHO",
            "地雷TouchHold",
            NotesTypeID.Def.TouchHold,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MBTHO",
            "地雷绝赞TouchHold",
            NotesTypeID.Def.TouchHold,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MBTAP",
            "地雷BreakTap",
            NotesTypeID.Def.Break,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MBHLD",
            "地雷BreakHold",
            NotesTypeID.Def.BreakHold,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MBSTR",
            "地雷BreakStar",
            NotesTypeID.Def.BreakStar,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MBSSS",
            "地雷BreakSlide",
            NotesTypeID.Def.BreakSlide,
            SlideType.Slide_MAX,
            8,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MZTAP",
            "地雷ExBreakTap",
            NotesTypeID.Def.ExBreakTap,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MZHLD",
            "地雷ExBreakHold",
            NotesTypeID.Def.ExBreakHold,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MZSTR",
            "地雷ExBreakStar",
            NotesTypeID.Def.ExBreakStar,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MXTAP",
            "地雷ExTap",
            NotesTypeID.Def.Begin,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MXHLD",
            "地雷ExHold",
            NotesTypeID.Def.Hold,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        list.Add(new object[14]
        {
            length++,
            "MXSTR",
            "地雷ExStar",
            NotesTypeID.Def.Star,
            SlideType.Slide_Straight,
            0,
            Ma2Category.MA2_Note,
            2,
            2,
            2,
            2,
            2,
            2,
            0
        });
        List<object[]> list2 = list;
        string[] array = new string[36]
        {
            "COLORV", "SIZEV", "ALPHAV", "SVSP", "HS", "SPAWN", "SPAWNMODE", "DESTROY", "BOUNCE", "SHOWJUDGEINFO",
            "SHOWCOMBOINFO", "SHOWJUDGETEXT", "COMBODISPLAY", "INNERBRIGHTNESS", "OUTERBRIGHTNESS", "SHAKE", "FLASH", "FADE", "TINT", "GAUSSIAN",
            "NEON", "TRAIL", "BRIGHTNESS", "SATURATION", "CONTRAST", "RAINBOW", "VIGNETTE", "ZOOM", "GLITCH", "TVNOISE",
            "HUE", "MOVE", "ROTATE", "TEXT", "AUDIO", "PVOVERLAY"
        };
        foreach (string text in array)
        {
            list2.Add(new object[14]
            {
                length++,
                text,
                "音符即时外观",
                NotesTypeID.Def.Invalid,
                SlideType.Slide_INVALID,
                4,
                Ma2Category.MA2_Composition,
                2,
                2,
                0,
                0,
                0,
                0,
                0
            });
        }
        array = new string[21]
        {
            "NMSCR", "NMSCL", "NMSI_", "NMSLL", "NMSLR", "NMSUL", "NMSUR", "NMSXL", "NMSXR", "NMSF_",
            "NMSV_", "NMSSL", "NMSSR", "NMSL_", "NMSR_", "NMSU_", "NMSD_", "NMSNL", "NMSNR", "NMSFL",
            "NMSFR"
        };
        foreach (string text2 in array)
        {
            string text3 = "MN" + text2.Substring(2);
            list2.Add(new object[14]
            {
                length++,
                text3,
                "地雷" + text2,
                NotesTypeID.Def.Slide,
                SlideType.Slide_MAX,
                8,
                Ma2Category.MA2_Note,
                2,
                2,
                2,
                2,
                2,
                2,
                0
            });
        }
        array = new string[21]
        {
            "BRSCR", "BRSCL", "BRSI_", "BRSLL", "BRSLR", "BRSUL", "BRSUR", "BRSXL", "BRSXR", "BRSF_",
            "BRSV_", "BRSSL", "BRSSR", "BRSL_", "BRSR_", "BRSU_", "BRSD_", "BRSNL", "BRSNR", "BRSFL",
            "BRSFR"
        };
        foreach (string text4 in array)
        {
            string text5 = "MB" + text4.Substring(2);
            list2.Add(new object[14]
            {
                length++,
                text5,
                "地雷" + text4,
                NotesTypeID.Def.BreakSlide,
                SlideType.Slide_MAX,
                8,
                Ma2Category.MA2_Note,
                2,
                2,
                2,
                2,
                2,
                2,
                0
            });
        }
        array = new string[21]
        {
            "BXSCR", "BXSCL", "BXSI_", "BXSLL", "BXSLR", "BXSUL", "BXSUR", "BXSXL", "BXSXR", "BXSF_",
            "BXSV_", "BXSSL", "BXSSR", "BXSL_", "BXSR_", "BXSU_", "BXSD_", "BXSNL", "BXSNR", "BXSFL",
            "BXSFR"
        };
        foreach (string text6 in array)
        {
            string text7 = "MZ" + text6.Substring(2);
            list2.Add(new object[14]
            {
                length++,
                text7,
                "地雷" + text6,
                NotesTypeID.Def.ExBreakSlide,
                SlideType.Slide_MAX,
                8,
                Ma2Category.MA2_Note,
                2,
                2,
                2,
                2,
                2,
                2,
                0
            });
        }
        list2.Add(new object[14]
        {
            length++,
            "NZONE",
            "噪域事件",
            NotesTypeID.Def.Invalid,
            SlideType.Slide_INVALID,
            4,
            Ma2Category.MA2_Composition,
            2,
            2,
            0,
            0,
            0,
            0,
            0
        });
        ExtraScreenFilters.Definition[] all = ExtraScreenFilters.All;
        foreach (ExtraScreenFilters.Definition definition in all)
        {
            list2.Add(new object[14]
            {
                length++,
                definition.Tag,
                "Alpha屏幕滤镜",
                NotesTypeID.Def.Invalid,
                SlideType.Slide_INVALID,
                4,
                Ma2Category.MA2_Composition,
                2,
                2,
                0,
                0,
                0,
                0,
                0
            });
        }
        ChartFeatureGate.RegisterExtensions(list2.Select((object[] e) => (string)e[1]).Concat(AllNoteTypeNames));
        System.Type type = value.GetValue(0).GetType();
        ConstructorInfo constructorInfo = AccessTools.Constructor(type, new System.Type[14]
        {
            typeof(int),
            typeof(string),
            typeof(string),
            typeof(NotesTypeID.Def),
            typeof(SlideType),
            typeof(int),
            typeof(Ma2Category),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int)
        });
        Ma2FileRecordData = Array.CreateInstance(type, value.Length + list2.Count);
        Array.Copy(value, Ma2FileRecordData, value.Length);
        for (int num = 0; num < list2.Count; num++)
        {
            int index = value.Length + num;
            object value2 = constructorInfo.Invoke(list2[num]);
            Ma2FileRecordData.SetValue(value2, index);
        }
        traverse.SetValue(Ma2FileRecordData);
        TotalMa2RecordCount = Ma2FileRecordData.Length;
        LastMa2RecordID = TotalMa2RecordCount - 1;
        BuildMa2RecordIds(type);
        MelonLogger.Msg($"[CustomNoteType] MA2 record data extended, total count: {TotalMa2RecordCount}");
        SlideDataBuilder.InitializeHitAreasLookup();
        MelonLogger.Msg($"[CustomNoteType] HitAreasLookup initialized, total count: {SlideDataBuilder.HitAreasLookup.Count}");
        WarmFieldRefs();
    }

    private static void LoadMineTextures()
    {
        if (_mineTexturesLoaded) return;
        _mineTexturesLoaded = true;

        var dir = FileSystem.ResolvePath("Sinmai-Alpha/CustomNoteTypes");
        if (!Directory.Exists(dir))
        {
            MelonLogger.Warning($"[CustomNoteType] Mine texture directory not found: {dir}");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            if (texture.LoadImage(File.ReadAllBytes(file)))
            {
                // touch_star 花瓣素材方向与原版 touch 相反（上宽下窄 vs 原版下宽上窄），
                // 垂直翻转后瓣尖才朝外/朝上（与原版 touch 一致）。
                // touch_star_mine（地雷 touchstar MNSTP 用）素材方向与 touch_star 相同 → 同样翻转。
                if (name is "touch_star" or "touch_star_break" or "touch_star_each" or "touch_star_break_each" or "touch_star_mine" or "touch_star_mine_each")
                {
                    // 素材方向与原版 touch 相反（上宽下窄 vs 原版下宽上窄），加载时上下翻转。
                    texture = FlipTextureVertical(texture);
                }
                MineTextures[name] = texture;
            }
        }

        MelonLogger.Msg($"[CustomNoteType] Loaded {MineTextures.Count} mine textures from {dir}");
    }

    private static Texture2D FlipTextureVertical(Texture2D tex)
    {
        var w = tex.width;
        var h = tex.height;
        var pixels = tex.GetPixels32();
        var flipped = new Color32[w * h];
        for (var y = 0; y < h; y++)
        {
            Array.Copy(pixels, y * w, flipped, (h - 1 - y) * w, w);
        }
        var result = new Texture2D(w, h, tex.format, false);
        result.SetPixels32(flipped);
        result.Apply();
        return result;
    }
}
