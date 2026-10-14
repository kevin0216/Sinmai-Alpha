using System.Globalization;
using System.Collections.Generic;
using SinmaiAlpha.ChartVisuals;
using SinmaiAlpha.Hosting;
using SinmaiAlpha.Notes.Libs;
using HarmonyLib;
using Manager;
using MAI2.Util;
using Monitor;
using Monitor.Game;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private static readonly Dictionary<GameMonitor, NativeComboDisplay> ComboDisplays = new();

    private static void ApplyComboPresentation(GameMonitor monitor)
    {
        NotesReader reader = NotesManager.Instance(monitor.MonitorIndex).getReader();
        CanvasGroup value = MainOf(monitor);
        if ((GuiSizes.SinglePlayer && monitor.MonitorIndex != 0) || value == null || !PresentationTimelines.TryGetValue(reader, out var value2) || value2.ComboDisplay == null)
        {
            ReleaseComboPresentation(monitor);
            return;
        }
        if (value2.ComboDisplay.Evaluate(NotesManager.GetCurrentMsec()) == -1)
        {
            ReleaseComboPresentation(monitor);
            return;
        }
        if (!ComboDisplays.TryGetValue(monitor, out var value3) || value3 == null)
        {
            value3 = (ComboDisplays[monitor] = monitor.gameObject.AddComponent<NativeComboDisplay>());
        }
        GameObjectCtrl value4 = DispCtrlOf(monitor);
        value3.Bind(monitor, reader, value, (value4 != null) ? value4.GetAchiveObj() : null, value2.ComboDisplay);
    }
    private static void ReleaseComboPresentation(GameMonitor monitor)
    {
        if (!ComboDisplays.TryGetValue(monitor, out var display)) return;
        if (display != null) { display.Stop(); Object.Destroy(display); }
        ComboDisplays.Remove(monitor);
    }
    private static void ResetComboPresentation(NotesReader reader)
    {
        var stale = new List<GameMonitor>();
        foreach (var pair in ComboDisplays) if (pair.Value != null && pair.Value.Reader == reader) stale.Add(pair.Key);
        foreach (var monitor in stale) ReleaseComboPresentation(monitor);
    }
    private static void ReleaseComboPresentation()
    {
        foreach (var display in ComboDisplays.Values) if (display != null) { display.Stop(); Object.Destroy(display); }
        ComboDisplays.Clear();
    }
}

// Reuse Sinmai's center counter, native sprite atlases and Execute lifecycle.
// Only presentation is leased: score, life/course/shutter and user options remain native.
public sealed class NativeComboDisplay : MonoBehaviour
{
    private GameMonitor owner;
    private GameAchiveNum native;
    private ComboDisplayCommands.Track track;
    private DB.OptionCenterdisplayID originalMode;
    private int previousMode = int.MinValue;
    private sealed class NumberLease { public int Count; public Vector2 Size; }
    private readonly Dictionary<SpriteCounter, NumberLease> numberLeases = new();
    private void RestoreNumberFrames()
    {
        foreach (var pair in numberLeases)
        {
            if (pair.Key == null) continue;
            while (pair.Key.FrameList.Count > pair.Value.Count) pair.Key.RemoveFormatFrame();
            pair.Key.rectTransform.sizeDelta = pair.Value.Size;
        }
        numberLeases.Clear();
    }
    private readonly Dictionary<GameObject, bool> active = new();
    private readonly Dictionary<Transform, Vector3> positions = new();
    private readonly Dictionary<Graphic, Color> colors = new();
    private readonly Dictionary<UnityEngine.UI.Image, Sprite> sprites = new();
    internal NotesReader Reader { get; private set; }
    internal static readonly string[] CounterFields = {
        "_title", "_titleDxScore", "_intNum", "_intNumMain", "_floatNum", "_floatNumMain",
        "_floatNumDenomi", "_floatNumDenomiMain", "_achiveTiele"
    };
    private T Field<T>(string name)
    {
        if ((object)native != null)
        {
            string[] counterFields = CounterFields;
            for (int i = 0; i < counterFields.Length; i++)
            {
                if (counterFields[i] == name)
                {
                    AccessTools.FieldRef<GameAchiveNum, Component> fieldRef = NativeRefs.Counters[i];
                    if (fieldRef != null)
                    {
                        object obj = fieldRef(native);
                        if (obj == null)
                        {
                            return default(T);
                        }
                        return (T)obj;
                    }
                    break;
                }
            }
        }
        return Traverse.Create(native).Field(name).GetValue<T>();
    }
    internal static DB.OptionCenterdisplayID NativeMode(int mode)
    {
        switch (mode)
        {
            case 1: return DB.OptionCenterdisplayID.Combo;
            case 3: case 11: return DB.OptionCenterdisplayID.AchivePlus;
            case 4: case 12: return DB.OptionCenterdisplayID.AchiveMinus1;
            case 13: return DB.OptionCenterdisplayID.DeluxScore;
            default: return DB.OptionCenterdisplayID.Off;
        }
    }
    internal void Bind(GameMonitor monitor, NotesReader reader, CanvasGroup group, GameAchiveNum counter, ComboDisplayCommands.Track changes)
    {
        if (Reader != reader || native != counter)
        {
            Stop(); native = counter;
            if (native != null)
            {
                originalMode = Field<DB.OptionCenterdisplayID>("_displayCenter");
                foreach (var name in CounterFields)
                {
                    var component=Field<Component>(name); if(component==null)continue;
                    active[component.gameObject]=component.gameObject.activeSelf;
                    positions[component.transform]=component.transform.localPosition;
                    foreach(var graphic in component.GetComponentsInChildren<Graphic>(true))
                    { colors[graphic]=graphic.color; if(graphic is UnityEngine.UI.Image image) sprites[image]=image.sprite; }
                }
            }
        }
        owner=monitor; Reader=reader; track=changes; enabled=true;
    }
    private void InvalidateNativeValue()
    {
        if(native==null)return;
        Traverse.Create(native).Field("_preData").SetValue(int.MinValue);
        Traverse.Create(native).Field("_preDxScoreData").SetValue(int.MinValue);
        Traverse.Create(native).Field("_dispDelayCount").SetValue(3);
    }
    internal void Stop()
    {
        enabled=false;
        RestoreNumberFrames();
        if(native!=null) { Traverse.Create(native).Field("_displayCenter").SetValue(originalMode); InvalidateNativeValue(); }
        foreach(var pair in active) if(pair.Key!=null)pair.Key.SetActive(pair.Value);
        foreach(var pair in positions) if(pair.Key!=null)pair.Key.localPosition=pair.Value;
        foreach(var pair in colors) if(pair.Key!=null) { if(pair.Key is SpriteCounter counter)counter.SetColor(pair.Value); else pair.Key.color=pair.Value; }
        foreach(var pair in sprites) if(pair.Key!=null)pair.Key.sprite=pair.Value;
        active.Clear(); positions.Clear(); colors.Clear(); sprites.Clear();
        native=null; owner=null; Reader=null; track=null; previousMode=int.MinValue;
    }
    private void OnDestroy()=>Stop();
    private void Show(string field,bool visible)
    { var root=Field<Component>(field); if(root!=null&&root.gameObject.activeSelf!=visible)root.gameObject.SetActive(visible); }
    private void Text(string field, string value)
    {
        SpriteCounter spriteCounter = Field<SpriteCounter>(field);
        if (spriteCounter == null)
        {
            return;
        }
        if (value.Length > spriteCounter.FrameList.Count)
        {
            if (!numberLeases.ContainsKey(spriteCounter))
            {
                numberLeases[spriteCounter] = new NumberLease
                {
                    Count = spriteCounter.FrameList.Count,
                    Size = spriteCounter.rectTransform.sizeDelta
                };
            }
            while (spriteCounter.FrameList.Count < value.Length)
            {
                int count = spriteCounter.FrameList.Count;
                spriteCounter.AddFormatFream();
                if (spriteCounter.FrameList.Count == count)
                {
                    break;
                }
                if (count > 0)
                {
                    SpriteCounterData spriteCounterData = spriteCounter.FrameList[count - 1];
                    SpriteCounterData spriteCounterData2 = spriteCounter.FrameList[count];
                    spriteCounterData2.Scale = spriteCounterData.Scale;
                    spriteCounterData2.AnimationScale = spriteCounterData.AnimationScale;
                    if (spriteCounter.BetweenList.Count > 1)
                    {
                        spriteCounter.BetweenList[spriteCounter.BetweenList.Count - 1].Size = spriteCounter.BetweenList[spriteCounter.BetweenList.Count - 2].Size;
                    }
                }
            }
        }
        value = value.PadLeft(spriteCounter.FrameList.Count);
        AccessTools.FieldRef<SpriteCounter, string> mainText = NativeRefs.MainText;
        if (((mainText != null) ? mainText(spriteCounter) : Traverse.Create(spriteCounter).Field("mainText").GetValue<string>()) != value)
        {
            spriteCounter.ChangeText(value);
        }
    }
    private void SetLayout(int mode)
    {
        RestoreNumberFrames();
        foreach(var pair in positions) if(pair.Key!=null)pair.Key.localPosition=pair.Value;
        Traverse.Create(native).Field("_animCount").SetValue(0);
        var mapped=NativeMode(mode);
        Traverse.Create(native).Field("_displayCenter").SetValue(mapped);
        InvalidateNativeValue();
        var rate=mode==3||mode==4||mode==11||mode==12;
        foreach(var name in CounterFields) Show(name,false);
        if(mode==0)return;
        Show("_title",mapped!=DB.OptionCenterdisplayID.Off);
        var title=Field<UI.MultipleImage>("_title");
        if(title!=null&&mapped!=DB.OptionCenterdisplayID.Off)title.ChangeSprite((int)mapped);
        Show("_intNum",!rate); Show("_intNumMain",!rate);
        Show("_floatNum",rate); Show("_floatNumMain",rate);
        Show("_floatNumDenomi",rate); Show("_floatNumDenomiMain",rate); Show("_achiveTiele",rate);
        var table=CommonScriptable.GetColorSetting();
        if(table!=null)
        {
            var palette=table.GameCenterNumColor;
            if(palette!=null&&(int)mapped<palette.Length)
                foreach(var field in new[]{"_intNumMain","_floatNumMain","_floatNumDenomiMain"}) Field<SpriteCounter>(field)?.SetColor(palette[(int)mapped]);
        }
    }
    private void LateUpdate()
    {
        if (!enabled || owner == null || native == null || track == null)
        {
            return;
        }
        int num = track.Evaluate(NotesManager.GetCurrentMsec());
        if (num == -1)
        {
            Stop();
            return;
        }
        if (num != previousMode)
        {
            SetLayout(num);
            previousMode = num;
        }
        switch (num)
        {
            case 0:
                {
                    string[] counterFields = CounterFields;
                    foreach (string field in counterFields)
                    {
                        Show(field, visible: false);
                    }
                    break;
                }
            case 2:
            case 3:
            case 4:
            case 101:
            case 102:
                {
                    GamePlayManager instance = Singleton<GamePlayManager>.Instance;
                    // Only mode 4 reads AchievementDownClassic, the one value that needs the per-note loss walk.
                    NativeComboValues nativeComboValues = NativeComboValues.CaptureSession(instance.GetGameScore(owner.MonitorIndex), instance.GetGameScore(1 - owner.MonitorIndex), num == 4);
                    long num2;
                    switch (num)
                    {
                        case 3:
                        case 4:
                            {
                                string[] array = nativeComboValues.Rate(num).ToString("0.0000", CultureInfo.InvariantCulture).Split('.');
                                Text("_floatNum", array[0].PadLeft(3));
                                Text("_floatNumMain", array[0].PadLeft(3));
                                Text("_floatNumDenomi", "." + array[1] + "%");
                                Text("_floatNumDenomiMain", "." + array[1] + "%");
                                return;
                            }
                        default:
                            num2 = nativeComboValues.NormalizedDownScore;
                            break;
                        case 101:
                            num2 = nativeComboValues.NormalizedScore;
                            break;
                        case 2:
                            num2 = nativeComboValues.Score;
                            break;
                    }
                    long num3 = num2;
                    string value = num3.ToString(CultureInfo.InvariantCulture);
                    Text("_intNum", value);
                    Text("_intNumMain", value);
                    break;
                }
        }
    }


    internal static class NativeRefs
    {
        internal static readonly AccessTools.FieldRef<GameAchiveNum, Component>[] Counters = BuildCounters();

        internal static readonly AccessTools.FieldRef<SpriteCounter, string> MainText = TryFieldRef<SpriteCounter, string>("mainText");

        private static AccessTools.FieldRef<GameAchiveNum, Component>[] BuildCounters()
        {
            string[] counterFields = CounterFields;
            AccessTools.FieldRef<GameAchiveNum, Component>[] array = new AccessTools.FieldRef<GameAchiveNum, Component>[counterFields.Length];
            for (int i = 0; i < counterFields.Length; i++)
            {
                array[i] = TryFieldRef<GameAchiveNum, Component>(counterFields[i]);
            }
            return array;
        }

        private static AccessTools.FieldRef<T, F> TryFieldRef<T, F>(string name) where T : class
        {
            try
            {
                return AccessTools.FieldRefAccess<T, F>(name);
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
