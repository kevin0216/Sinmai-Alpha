using HarmonyLib;
using Manager;
using Monitor.Game;
using Monitor;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
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

    private static class PresentationRefs
    {
        internal static readonly AccessTools.FieldRef<GameMonitor, GameCtrl> GameController = TryFieldRef<GameMonitor, GameCtrl>("GameController");

        internal static readonly AccessTools.FieldRef<GameMonitor, CanvasGroup> Main = TryFieldRef<GameMonitor, CanvasGroup>("Main");

        internal static readonly AccessTools.FieldRef<GameMonitor, GameObjectCtrl> GameDispCtrl = TryFieldRef<GameMonitor, GameObjectCtrl>("GameDispCtrl");

        internal static readonly AccessTools.FieldRef<GameCtrl, GameObject> GuideEndPoint = TryFieldRef<GameCtrl, GameObject>("_guideEndPointObj");

        internal static readonly AccessTools.FieldRef<GameCtrl, GameObject> TouchCLauncherLayer = TryFieldRef<GameCtrl, GameObject>("_touchCLauncherLayer");
    }

    private static class NoteRefs
    {
        internal static readonly AccessTools.FieldRef<NoteBase, float> AppearMsec = TryFieldRef<NoteBase, float>("AppearMsec");

        internal static readonly AccessTools.FieldRef<NoteBase, float> DefaultMsec = TryFieldRef<NoteBase, float>("DefaultMsec");

        internal static readonly AccessTools.FieldRef<NoteBase, float> TailMsec = TryFieldRef<NoteBase, float>("TailMsec");

        internal static readonly AccessTools.FieldRef<NoteBase, float> StartPos = TryFieldRef<NoteBase, float>("StartPos");

        internal static readonly AccessTools.FieldRef<NoteBase, float> EndPos = TryFieldRef<NoteBase, float>("EndPos");

        internal static readonly AccessTools.FieldRef<NoteBase, int> NoteIndex = TryFieldRef<NoteBase, int>("NoteIndex");

        internal static readonly AccessTools.FieldRef<NoteBase, bool> EndFlag = TryFieldRef<NoteBase, bool>("EndFlag");

        internal static readonly AccessTools.FieldRef<NoteBase, SpriteRenderer> SpriteRender = TryFieldRef<NoteBase, SpriteRenderer>("SpriteRender");

        internal static readonly AccessTools.FieldRef<NoteBase, SpriteRenderer> SpriteRenderEx = TryFieldRef<NoteBase, SpriteRenderer>("SpriteRenderEx");

        internal static readonly AccessTools.FieldRef<NoteBase, NoteGuide> GuideObj = TryFieldRef<NoteBase, NoteGuide>("GuideObj");

        internal static readonly AccessTools.FieldRef<NoteBase, Transform> NoteGuideTrans = TryFieldRef<NoteBase, Transform>("NoteGuideTrans");

        internal static readonly AccessTools.FieldRef<NoteBase, GameObject> NoteObj = TryFieldRef<NoteBase, GameObject>("NoteObj");

        internal static readonly AccessTools.FieldRef<NoteBase, GameObject> ExObj = TryFieldRef<NoteBase, GameObject>("ExObj");

        // EffectSprite is declared only on HoldNote, BreakHoldNote and BreakNote (BreakStarNote and the Mine*
        // variants inherit those), EndPointObj only on HoldNote and BreakHoldNote; see EffectSpriteOf/EndPointOf.
        internal static readonly AccessTools.FieldRef<HoldNote, SpriteRenderer> HoldEffect = TryFieldRef<HoldNote, SpriteRenderer>("EffectSprite");

        internal static readonly AccessTools.FieldRef<BreakHoldNote, SpriteRenderer> BreakHoldEffect = TryFieldRef<BreakHoldNote, SpriteRenderer>("EffectSprite");

        internal static readonly AccessTools.FieldRef<BreakNote, SpriteRenderer> BreakEffect = TryFieldRef<BreakNote, SpriteRenderer>("EffectSprite");

        internal static readonly AccessTools.FieldRef<HoldNote, GameObject> HoldEndPoint = TryFieldRef<HoldNote, GameObject>("EndPointObj");

        internal static readonly AccessTools.FieldRef<BreakHoldNote, GameObject> BreakHoldEndPoint = TryFieldRef<BreakHoldNote, GameObject>("EndPointObj");

        internal static readonly AccessTools.FieldRef<StarNote, bool> StarMultiSlide = TryFieldRef<StarNote, bool>("MultiSlide");

        internal static readonly AccessTools.FieldRef<TouchNoteB, SpriteRenderer[]> TouchPetals = TryFieldRef<TouchNoteB, SpriteRenderer[]>("ColorsObject");

        internal static readonly AccessTools.FieldRef<TouchNoteB, UnityEngine.Vector3[]> TouchPositions = TryFieldRef<TouchNoteB, UnityEngine.Vector3[]>("DefaultCorlsPos");

        internal static readonly AccessTools.FieldRef<TouchNoteB, GameObject> TouchNotice = TryFieldRef<TouchNoteB, GameObject>("NoticeObject");

        internal static readonly AccessTools.FieldRef<TouchHoldC, SpriteRenderer> TouchGauge = TryFieldRef<TouchHoldC, SpriteRenderer>("HoldGaugeObject");

        // BreakEffectSprite is declared only on BreakHoldNote (1.66, 1.70; MineBreakHoldNote does not redeclare it).
        internal static readonly AccessTools.FieldRef<BreakHoldNote, SpriteRenderer> BreakHoldGlow = TryFieldRef<BreakHoldNote, SpriteRenderer>("BreakEffectSprite");

        // NoteBase.ButtonId is a non-virtual auto-property declared only on NoteBase (1.66, 1.70).
        internal static readonly Func<NoteBase, int> ButtonId = TryButtonIdGetter();

        private static Func<NoteBase, int> TryButtonIdGetter()
        {
            try
            {
                MethodInfo methodInfo = AccessTools.PropertyGetter(typeof(NoteBase), "ButtonId");
                return (methodInfo == null) ? null : ((Func<NoteBase, int>)Delegate.CreateDelegate(typeof(Func<NoteBase, int>), methodInfo));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private static class SlideRefs
    {
        internal static readonly AccessTools.FieldRef<SlideRoot, NoteData> Note = TryFieldRef<SlideRoot, NoteData>("NoteData");

        internal static readonly AccessTools.FieldRef<SlideRoot, int> DispLaneNum = TryFieldRef<SlideRoot, int>("_dispLaneNum");

        internal static readonly AccessTools.FieldRef<SlideRoot, List<SpriteRenderer>> Sprites = TryFieldRef<SlideRoot, List<SpriteRenderer>>("_spriteRenders");

        internal static readonly AccessTools.FieldRef<SlideRoot, List<BreakSlide>> BreakSprites = TryFieldRef<SlideRoot, List<BreakSlide>>("_breakSpriteRenders");

        internal static readonly AccessTools.FieldRef<SlideRoot, bool> BreakFlag = TryFieldRef<SlideRoot, bool>("BreakFlag");

        internal static readonly AccessTools.FieldRef<SlideRoot, bool> EndFlag = TryFieldRef<SlideRoot, bool>("EndFlag");

        internal static readonly AccessTools.FieldRef<SlideRoot, float> StarLaunchMsec = TryFieldRef<SlideRoot, float>("StarLaunchMsec");

        internal static readonly AccessTools.FieldRef<SlideRoot, float> StarArriveMsec = TryFieldRef<SlideRoot, float>("StarArriveMsec");

        internal static readonly AccessTools.FieldRef<SlideRoot, GameObject> BaseStarNote = TryFieldRef<SlideRoot, GameObject>("_baseStarNote");

        internal static readonly AccessTools.FieldRef<SlideRoot, List<List<UnityEngine.Vector4>>> SlideVecListList = TryFieldRef<SlideRoot, List<List<UnityEngine.Vector4>>>("_slideVecListList");

        internal static readonly AccessTools.FieldRef<SlideFan, GameObject[]> FanBaseStarObjs = TryFieldRef<SlideFan, GameObject[]>("_baseStarObjs");

        internal static readonly AccessTools.FieldRef<SlideFan, List<UnityEngine.Vector4>[]> FanSlideVecList = TryFieldRef<SlideFan, List<UnityEngine.Vector4>[]>("_slideVecList");
    }

    private static void WarmFieldRefs()
    {
        try
        {
            RuntimeHelpers.RunClassConstructor(typeof(PresentationRefs).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(NoteRefs).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(SlideRefs).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(ChartFrameMotion.NativeRefs).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(NativeComboDisplay.NativeRefs).TypeHandle);
        }
        catch (Exception)
        {
        }
    }

    private static GameCtrl ControllerOf(GameMonitor monitor)
    {
        if ((object)monitor == null)
        {
            return null;
        }
        AccessTools.FieldRef<GameMonitor, GameCtrl> gameController = PresentationRefs.GameController;
        return (gameController != null) ? gameController(monitor) : Traverse.Create(monitor).Field("GameController").GetValue<GameCtrl>();
    }

    private static CanvasGroup MainOf(GameMonitor monitor)
    {
        if ((object)monitor == null)
        {
            return null;
        }
        AccessTools.FieldRef<GameMonitor, CanvasGroup> main = PresentationRefs.Main;
        return (main != null) ? main(monitor) : Traverse.Create(monitor).Field("Main").GetValue<CanvasGroup>();
    }

    private static GameObjectCtrl DispCtrlOf(GameMonitor monitor)
    {
        if ((object)monitor == null)
        {
            return null;
        }
        AccessTools.FieldRef<GameMonitor, GameObjectCtrl> gameDispCtrl = PresentationRefs.GameDispCtrl;
        return (gameDispCtrl != null) ? gameDispCtrl(monitor) : Traverse.Create(monitor).Field("GameDispCtrl").GetValue<GameObjectCtrl>();
    }

    private static GameObject GuideEndPointOf(GameCtrl controller)
    {
        if ((object)controller == null)
        {
            return null;
        }
        AccessTools.FieldRef<GameCtrl, GameObject> guideEndPoint = PresentationRefs.GuideEndPoint;
        return (guideEndPoint != null) ? guideEndPoint(controller) : Traverse.Create(controller).Field("_guideEndPointObj").GetValue<GameObject>();
    }

    private static GameObject TouchCLauncherOf(GameCtrl controller)
    {
        if ((object)controller == null)
        {
            return null;
        }
        AccessTools.FieldRef<GameCtrl, GameObject> touchCLauncherLayer = PresentationRefs.TouchCLauncherLayer;
        return (touchCLauncherLayer != null) ? touchCLauncherLayer(controller) : Traverse.Create(controller).Field("_touchCLauncherLayer").GetValue<GameObject>();
    }

    private static float AppearMsecOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, float> appearMsec = NoteRefs.AppearMsec;
        return (appearMsec != null) ? appearMsec(note) : ((float)FAppearMsec.GetValue(note));
    }

    private static float DefaultMsecOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, float> defaultMsec = NoteRefs.DefaultMsec;
        return (defaultMsec != null) ? defaultMsec(note) : ((float)FDefaultMsec.GetValue(note));
    }

    private static float TailMsecOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, float> tailMsec = NoteRefs.TailMsec;
        return (tailMsec != null) ? tailMsec(note) : ((float)FTailMsec.GetValue(note));
    }

    private static float StartPosOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, float> startPos = NoteRefs.StartPos;
        return (startPos != null) ? startPos(note) : ((float)FStartPos.GetValue(note));
    }

    private static float EndPosOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, float> endPos = NoteRefs.EndPos;
        return (endPos != null) ? endPos(note) : ((float)FEndPos.GetValue(note));
    }

    private static GameObject NoteObjectOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, GameObject> noteObj = NoteRefs.NoteObj;
        return (noteObj != null) ? noteObj(note) : ((GameObject)RingNoteObject.GetValue(note));
    }

    private static SpriteRenderer NoteSpriteOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, SpriteRenderer> spriteRender = NoteRefs.SpriteRender;
        return (spriteRender != null) ? spriteRender(note) : ((SpriteRenderer)RingSprite.GetValue(note));
    }

    private static SpriteRenderer NoteSpriteExOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, SpriteRenderer> spriteRenderEx = NoteRefs.SpriteRenderEx;
        return (spriteRenderEx != null) ? spriteRenderEx(note) : ((SpriteRenderer)RingExSprite.GetValue(note));
    }

    private static NoteGuide NoteGuideOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, NoteGuide> guideObj = NoteRefs.GuideObj;
        return (guideObj != null) ? guideObj(note) : ((NoteGuide)RingGuide.GetValue(note));
    }

    private static SpriteRenderer[] TouchPetalsOf(TouchNoteB note)
    {
        AccessTools.FieldRef<TouchNoteB, SpriteRenderer[]> touchPetals = NoteRefs.TouchPetals;
        return (touchPetals != null) ? touchPetals(note) : ((SpriteRenderer[])TouchPetals.GetValue(note));
    }

    private static UnityEngine.Vector3[] TouchPositionsOf(TouchNoteB note)
    {
        AccessTools.FieldRef<TouchNoteB, UnityEngine.Vector3[]> touchPositions = NoteRefs.TouchPositions;
        return (touchPositions != null) ? touchPositions(note) : ((UnityEngine.Vector3[])TouchPositions.GetValue(note));
    }

    private static GameObject TouchNoticeOf(TouchNoteB note)
    {
        AccessTools.FieldRef<TouchNoteB, GameObject> touchNotice = NoteRefs.TouchNotice;
        return (touchNotice != null) ? touchNotice(note) : ((GameObject)TouchNotice.GetValue(note));
    }

    private static SpriteRenderer TouchGaugeOf(TouchHoldC note)
    {
        AccessTools.FieldRef<TouchHoldC, SpriteRenderer> touchGauge = NoteRefs.TouchGauge;
        return (touchGauge != null) ? touchGauge(note) : ((SpriteRenderer)TouchGauge.GetValue(note));
    }

    private static int NoteIndexOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, int> noteIndex = NoteRefs.NoteIndex;
        return (noteIndex != null) ? noteIndex(note) : Traverse.Create(note).Field("NoteIndex").GetValue<int>();
    }

    private static Transform NoteGuideTransOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, Transform> noteGuideTrans = NoteRefs.NoteGuideTrans;
        return (noteGuideTrans != null) ? noteGuideTrans(note) : Traverse.Create(note).Field("NoteGuideTrans").GetValue<Transform>();
    }

    private static GameObject NoteExObjectOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, GameObject> exObj = NoteRefs.ExObj;
        return (exObj != null) ? exObj(note) : Traverse.Create(note).Field("ExObj").GetValue<GameObject>();
    }

    private static SpriteRenderer BounceSpriteOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, SpriteRenderer> spriteRender = NoteRefs.SpriteRender;
        return (spriteRender != null) ? spriteRender(note) : Traverse.Create(note).Field("SpriteRender").GetValue<SpriteRenderer>();
    }

    private static SpriteRenderer BounceSpriteExOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, SpriteRenderer> spriteRenderEx = NoteRefs.SpriteRenderEx;
        return (spriteRenderEx != null) ? spriteRenderEx(note) : Traverse.Create(note).Field("SpriteRenderEx").GetValue<SpriteRenderer>();
    }

    private static NoteGuide BounceGuideOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, NoteGuide> guideObj = NoteRefs.GuideObj;
        return (guideObj != null) ? guideObj(note) : Traverse.Create(note).Field("GuideObj").GetValue<NoteGuide>();
    }

    private static GameObject BounceNoteObjectOf(NoteBase note)
    {
        AccessTools.FieldRef<NoteBase, GameObject> noteObj = NoteRefs.NoteObj;
        return (noteObj != null) ? noteObj(note) : Traverse.Create(note).Field("NoteObj").GetValue<GameObject>();
    }

    private static SpriteRenderer EffectSpriteOf(NoteBase note)
    {
        if (note is HoldNote holdNote)
        {
            AccessTools.FieldRef<HoldNote, SpriteRenderer> holdEffect = NoteRefs.HoldEffect;
            return (holdEffect != null) ? holdEffect(holdNote) : Traverse.Create(note).Field("EffectSprite").GetValue<SpriteRenderer>();
        }
        if (note is BreakHoldNote breakHoldNote)
        {
            AccessTools.FieldRef<BreakHoldNote, SpriteRenderer> breakHoldEffect = NoteRefs.BreakHoldEffect;
            return (breakHoldEffect != null) ? breakHoldEffect(breakHoldNote) : Traverse.Create(note).Field("EffectSprite").GetValue<SpriteRenderer>();
        }
        if (note is BreakNote breakNote)
        {
            AccessTools.FieldRef<BreakNote, SpriteRenderer> breakEffect = NoteRefs.BreakEffect;
            return (breakEffect != null) ? breakEffect(breakNote) : Traverse.Create(note).Field("EffectSprite").GetValue<SpriteRenderer>();
        }
        return null;
    }

    private static GameObject EndPointOf(NoteBase note)
    {
        if (note is HoldNote holdNote)
        {
            AccessTools.FieldRef<HoldNote, GameObject> holdEndPoint = NoteRefs.HoldEndPoint;
            return (holdEndPoint != null) ? holdEndPoint(holdNote) : Traverse.Create(note).Field("EndPointObj").GetValue<GameObject>();
        }
        if (note is BreakHoldNote breakHoldNote)
        {
            AccessTools.FieldRef<BreakHoldNote, GameObject> breakHoldEndPoint = NoteRefs.BreakHoldEndPoint;
            return (breakHoldEndPoint != null) ? breakHoldEndPoint(breakHoldNote) : Traverse.Create(note).Field("EndPointObj").GetValue<GameObject>();
        }
        return null;
    }

    private static bool EndFlagOf(Component owner)
    {
        if (owner is NoteBase noteBase && NoteRefs.EndFlag != null)
        {
            return NoteRefs.EndFlag(noteBase);
        }
        if (owner is SlideRoot slideRoot && SlideRefs.EndFlag != null)
        {
            return SlideRefs.EndFlag(slideRoot);
        }
        return Traverse.Create(owner).Field("EndFlag").GetValue<bool>();
    }

    private static F TraverseRead<T, F>(AccessTools.FieldRef<T, F> fieldRef, T instance, string name) where T : class
    {
        return (fieldRef != null) ? fieldRef(instance) : Traverse.Create(instance).Field(name).GetValue<F>();
    }

    private static NoteData SlideNoteDataOf(SlideRoot slide)
    {
        AccessTools.FieldRef<SlideRoot, NoteData> noteData = SlideRefs.Note;
        return (noteData != null) ? noteData(slide) : Traverse.Create(slide).Field("NoteData").GetValue<NoteData>();
    }
}
