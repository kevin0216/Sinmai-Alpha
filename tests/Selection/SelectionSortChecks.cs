using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using MAI2System;
using Manager;
using Process;
using SinmaiAlpha.Difficulty;

internal static partial class Program
{
    private static bool sortGenreMissing;
    private static int sortObserved;
    // SetSortList asks DataManager for one genre before sorting; no catalogue is loaded here.
    private static bool StubSortGenre(ref Manager.MaiStudio.MusicGenreData __result)
    { __result = sortGenreMissing ? null : new Manager.MaiStudio.MusicGenreData(); return false; }
    private static void ObserveSortDifficulty(MusicSelectProcess __instance, int player) =>
        sortObserved = __instance.GetCurrentDifficulty(player);

    // In game, SortSettingSequence -> OnSortChange rebuilds the catalogue while the native
    // sort comparers query the current difficulty; the postfix then crashed in GetMusic on a
    // half-built row (fewer score kinds than ScoreType). Only original game methods run here.
    private static void SelectionSortChecks()
    {
        var harmony = new HarmonyLib.Harmony("SinmaiAlpha.Tests.SelectionSort");
        var sorting = AccessTools.Field(typeof(ExtraDifficulty), "sortingSongs");
        try
        {
            harmony.PatchAll(typeof(ExtraDifficulty.AvailableResolvedDifficultyPatch));
            harmony.Patch(AccessTools.Method(typeof(DataManager), "GetMusicGenre"), prefix: new HarmonyMethod(typeof(Program), nameof(StubSortGenre)));
            harmony.Patch(AccessTools.Method(typeof(MusicSelectProcess), "SetRankSortList"), prefix: new HarmonyMethod(typeof(Program), nameof(ObserveSortDifficulty)));
            var setSort = AccessTools.Method(typeof(MusicSelectProcess), "SetSortList");

            var process = (MusicSelectProcess)FormatterServices.GetUninitializedObject(typeof(MusicSelectProcess));
            process.CurrentDifficulty = new[] { MusicDifficultyID.Master, MusicDifficultyID.Advanced };
            Set(process, "_currentPlayerSubSequence", new[] { MusicSelectProcess.SubSequence.SortSetting, MusicSelectProcess.SubSequence.SortSetting });
            var sortSetting = AccessTools.Field(typeof(MusicSelectProcess), "_musicSortSetting");
            sortSetting.SetValue(process, Enum.Parse(sortSetting.FieldType, "Rank"));
            // The cursor row has an Expert chart only, and just one score kind.
            var row = (MusicSelectProcess.MusicSelectData)FormatterServices.GetUninitializedObject(typeof(MusicSelectProcess.MusicSelectData));
            row.MusicData = Music(16050, 2);
            var combined = (MusicSelectProcess.CombineMusicSelectData)FormatterServices.GetUninitializedObject(typeof(MusicSelectProcess.CombineMusicSelectData));
            combined.musicSelectData = new List<MusicSelectProcess.MusicSelectData> { row };
            Set(process, "_combineMusicDataList", new List<ReadOnlyCollection<MusicSelectProcess.CombineMusicSelectData>> {
                new List<MusicSelectProcess.CombineMusicSelectData> { combined }.AsReadOnly() });

            int Sort(int player)
            {
                sortObserved = -1;
                setSort.Invoke(process, new object[] { player, 12345, new List<MusicSelectProcess.CombineMusicSelectData>(), false });
                return sortObserved;
            }
            bool Throws<T>(Func<int> action) where T : Exception
            {
                try { action(); return false; }
                catch (TargetInvocationException e) when (e.InnerException is T) { return true; }
            }

            process.ScoreType = ConstParameter.ScoreKind.Deluxe;
            Check(Throws<ArgumentOutOfRangeException>(() => Sort(0)), "Unguarded sort reproduces the half-built row crash");
            process.ScoreType = ConstParameter.ScoreKind.Standard;
            Check(Sort(0) == (int)MusicDifficultyID.Expert, "Unguarded sort key follows the cursor song");

            harmony.PatchAll(typeof(ExtraDifficulty.AvailableSortPatch));
            foreach (var kind in new[] { ConstParameter.ScoreKind.Deluxe, ConstParameter.ScoreKind.Standard })
            {
                process.ScoreType = kind;
                for (int player = 0; player < 2; player++)
                {
                    Check(Sort(player) == (int)process.CurrentDifficulty[player], "Sorting keeps the player's own difficulty: " + kind);
                    Check((int)sorting.GetValue(null) == 0, "Sort guard is released after sorting");
                }
            }
            Check(process.GetCurrentDifficulty(0) == (int)MusicDifficultyID.Expert, "Resolution resumes after sorting");
            Check(process.GetCurrentDifficulty(1) == (int)MusicDifficultyID.Expert, "Second player resolves after sorting");

            sortGenreMissing = true;
            Check(Throws<NullReferenceException>(() => Sort(0)), "Native sort failure still propagates");
            Check((int)sorting.GetValue(null) == 0, "Sort guard is released when the native sort throws");

            System.Console.WriteLine("PASS: catalogue rebuild sorting keeps native difficulty for both players and score kinds; resolution resumes afterwards.");
        }
        finally { harmony.UnpatchSelf(); sortGenreMissing = false; sorting.SetValue(null, 0); }
    }
}
