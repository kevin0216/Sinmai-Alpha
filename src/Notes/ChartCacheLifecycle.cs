using Manager;
using Monitor;
using SinmaiAlpha.ChartVisuals;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    // Unity's Boehm Mono does not collect ConditionalWeakTable keys reliably.
    // Replace chart tables after release, once presentation leases are disposed.
    // Reader timelines must survive OnStart because a reader can be preloaded.
    private static void ReleaseReaderTables()
    {
        MediaTimelines = new();
        JudgeLineTimelines = new();
        PresentationTimelines = new();
        NoiseCharts = new();
        VisualTimelines = new();
        ReserveVisualNotes = new();
        VisualState.ReleaseIndexes();
    }

    private static void ResetChartScopedTables()
    {
        BorrowedNotes = new ConditionalWeakTable<NoteData, BorrowedBinding>();
        DZonePositions = new ConditionalWeakTable<NoteData, DZonePosition>();
        DZoneOwners = new ConditionalWeakTable<NoteBase, DZoneLane>();
        DZoneLanes = new ConditionalWeakTable<Transform, DZoneLane>();
        FakeNotes = new ConditionalWeakTable<NoteData, FakeMarker>();
        FakeLaunchers = new ConditionalWeakTable<Transform, Transform>();
        FireworkNotes = new ConditionalWeakTable<NoteData, FireworkValue>();
        NoteSkins = new ConditionalWeakTable<NoteData, SkinNote>();
        VisualNotes = new ConditionalWeakTable<NoteData, VisualPayload>();
        RingCrossings = new ConditionalWeakTable<NoteBase, RingCrossingLease>();
        SlideAppearances = new ConditionalWeakTable<SlideRoot, SlideAppearanceLease>();
        StarHeads = new ConditionalWeakTable<NoteData, StarHeadValue>();
        TouchRadii = new ConditionalWeakTable<NoteData, TouchRadiusValue>();
        TouchMotionOwners = new ConditionalWeakTable<TouchNoteB, TouchMotionLease>();
        TouchExState.Notes = new ConditionalWeakTable<NoteData, TouchExState.Flag>();
        TouchExState.Owners = new ConditionalWeakTable<TouchNoteB, TouchExState.Flag>();
        FakeLauncherTransforms.Clear();
        ScaledTouchStarNotices.Clear();
        ScaledTouchStarJuts.Clear();
    }
}
