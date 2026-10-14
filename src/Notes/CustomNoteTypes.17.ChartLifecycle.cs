using HarmonyLib;
using Process;

namespace SinmaiAlpha.Notes;

// 谱面开始及结束时的缓存清理。
public partial class CustomNoteTypes
{

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameProcess), "OnStart")]
    public static void OnGameStartClearHyperSpeed()
    {
        NoteSpeedMultipliers.Clear();
        NoteSpeedByIndex.Clear();
        PendingNoteSpeedMultipliers.Clear();
        PendingTouchSpeedMultipliers.Clear();
        PendingSvSegments.Clear();
        PendingHsSegments.Clear();
        SvCurves.Clear();
        HsCurves.Clear();
        SvClearTimes.Clear();
        SvTypeByNoteIndex.Clear();
        IgnoreSvByNoteIndex.Clear();
        SpeedMultByNoteIndex.Clear();
        EachChildByNoteIndex.Clear();
        _loggedSvInject = false;
        _loggedSvLeadScale = false;
        NoteKinds.Clear();
        MineNoteBehaviour.ClearInstances();
        MinePools.Clear();
        TouchBreakPools.Clear();
        TouchStarPools.Clear();
        MineTouchStarPools.Clear();
        PoolRoutes.Clear();
        DeferredMirrorPoolsByCtrl.Clear();
        HandledInitializations.Clear();
        RestoreAllMineGuideSprites();
        MineBreakSlides.Clear();
        MineBreakStars.Clear();
        MineHoldPenalties.Clear();
        _currentNoteData = null;
        _optionalSpeedForCurrentLoad = null;
        _removedOptionalSpeedString = null;
        _optionalSpeedRemoved = false;
        _optionalStreamIdForCurrentLoad = null;
        _removedOptionalStreamString = null;
        _optionalStreamRemoved = false;
        StreamTypeByNoteIndex.Clear();
        NoteScrollTableByNoteIndex.Clear();
        _pendingMineForCurrentLoad = false;
        _pendingTouchBreakForCurrentLoad = false;
        _pendingTouchStarForCurrentLoad = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameProcess), "OnRelease")]
    public static void OnGameReleaseClearHyperSpeed()
    {
        NoteSpeedMultipliers.Clear();
        NoteSpeedByIndex.Clear();
        PendingNoteSpeedMultipliers.Clear();
        PendingTouchSpeedMultipliers.Clear();
        PendingSvSegments.Clear();
        PendingHsSegments.Clear();
        SvCurves.Clear();
        HsCurves.Clear();
        SvClearTimes.Clear();
        SvTypeByNoteIndex.Clear();
        IgnoreSvByNoteIndex.Clear();
        SpeedMultByNoteIndex.Clear();
        EachChildByNoteIndex.Clear();
        _loggedSvInject = false;
        _loggedSvLeadScale = false;
        NoteKinds.Clear();
        MineNoteBehaviour.ClearInstances();
        MinePools.Clear();
        TouchBreakPools.Clear();
        TouchStarPools.Clear();
        MineTouchStarPools.Clear();
        PoolRoutes.Clear();
        DeferredMirrorPoolsByCtrl.Clear();
        HandledInitializations.Clear();
        RestoreAllMineGuideSprites();
        MineBreakSlides.Clear();
        MineBreakStars.Clear();
        MineHoldPenalties.Clear();
        _currentNoteData = null;
        _optionalSpeedForCurrentLoad = null;
        _removedOptionalSpeedString = null;
        _optionalSpeedRemoved = false;
        _optionalStreamIdForCurrentLoad = null;
        _removedOptionalStreamString = null;
        _optionalStreamRemoved = false;
        StreamTypeByNoteIndex.Clear();
        NoteScrollTableByNoteIndex.Clear();
        _pendingMineForCurrentLoad = false;
        _pendingTouchBreakForCurrentLoad = false;
        _pendingTouchStarForCurrentLoad = false;
    }
}
