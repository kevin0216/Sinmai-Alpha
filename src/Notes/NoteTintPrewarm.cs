using HarmonyLib;
using Manager;
using MelonLoader;
using Monitor.Game;
using Monitor;
using SinmaiAlpha.ChartVisuals;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    [HarmonyPatch(typeof(GameCtrl), "IsReady")]
    public static class EachGuideTintPrewarmPatch
    {
        public static void Postfix(GameCtrl __instance, ref bool __result)
        {
            // Attract mode never reaches ReleaseChartPresentation, so prewarmed textures would stay until the next game;
            // keep it lazy there.
            if (GameManager.IsAdvDemo || !FeaturesEnabled(__instance))
            {
                return;
            }
            try
            {
                if (!TickEachGuideTintPrewarm(__instance))
                {
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                if (!EachGuideTintPrewarmWarning)
                {
                    EachGuideTintPrewarmWarning = true;
                    MelonLogger.Warning("[Note Visual] Each-guide tint prewarm skipped: " + ex.Message);
                }
            }
        }
    }

    private sealed class EachGuideTintPrewarm
    {
        public NoteData First;

        public int Count;

        // Non-null while the candidates are still being collected.
        public EachGuideTintCandidates Candidates;

        public readonly List<Sprite> Sprites = new List<Sprite>();

        public readonly List<int> Colours = new List<int>();

        public readonly List<Color> Vertices = new List<Color>();

        public int Next;

        // For the one log line per chart: keys this queue built, time spent in its IsReady steps, and steps.
        public int Built;

        public long Ticks;

        public int Calls;

        public bool Logged;
    }

    private sealed class EachGuideTintCandidates
    {
        public Sprite EachBar;

        public readonly List<Color> VertexColours = new List<Color>();

        public readonly HashSet<int> VertexKeys = new HashSet<int>();

        public VisualTimeline Timeline;

        public Dictionary<int, CustomNoteKind> NoteKinds;

        public Sprite Mine;

        public bool MineLooked;

        public int Note;

        public readonly Dictionary<VisualTintKey, int> Slots = new Dictionary<VisualTintKey, int>();

        public readonly List<Sprite> Sprites = new List<Sprite>();

        public readonly List<int> Rgbs = new List<int>();

        public readonly List<Color> KeyVertices = new List<Color>();

        public readonly List<double> Needs = new List<double>();

        public readonly Dictionary<(List<VisualChange>, bool, bool, bool, string), EachGuideColourList> ColourLists = new Dictionary<(List<VisualChange>, bool, bool, bool, string), EachGuideColourList>();

        public readonly List<int> Colours = new List<int>();

        public readonly List<double> Times = new List<double>();
    }

    private sealed class EachGuideColourList
    {
        public readonly List<int> Rgbs = new List<int>();

        public readonly List<double> Times = new List<double>();
    }

    private static readonly Dictionary<NotesReader, EachGuideTintPrewarm> EachGuideTintPrewarms = new Dictionary<NotesReader, EachGuideTintPrewarm>();

    private static readonly FieldInfo GuideObjectList = AccessTools.Field(typeof(GameCtrl), "_guideObjectList");

    private static bool EachGuideTintPrewarmWarning;

    private const int EachGuideTintPrewarmLimit = 32;

    private static int EachGuideTintPrewarmBuilt;

    private const double EachGuideTintCollectBudgetMs = 4.0;

    private static bool TickEachGuideTintPrewarm(GameCtrl controller)
    {
        long start = Stopwatch.GetTimestamp();
        NotesReader notesReader = NotesManager.Instance(controller.MonitorIndex)?.getReader();
        NoteDataList noteDataList = notesReader?.GetNoteList();
        if (noteDataList == null)
        {
            return true;
        }
        NoteData noteData = ((noteDataList.Count > 0) ? noteDataList[0] : null);
        if (!EachGuideTintPrewarms.TryGetValue(notesReader, out var value) || (object)value.First != noteData || value.Count != noteDataList.Count)
        {
            value = new EachGuideTintPrewarm
            {
                First = noteData,
                Count = noteDataList.Count
            };
            EachGuideTintPrewarms[notesReader] = value;
            try
            {
                value.Candidates = StartEachGuideTintPrewarm(controller, notesReader);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Note Visual] Each-guide tint prewarm skipped: " + ex.Message);
            }
        }
        if (value.Candidates != null)
        {
            try
            {
                if (!CollectEachGuideTintCandidates(value.Candidates, noteDataList))
                {
                    return EachGuideTintPrewarmStep(value, start, done: false);
                }
                FinishEachGuideTintCandidates(value.Candidates, value);
            }
            catch (Exception ex2)
            {
                value.Sprites.Clear();
                MelonLogger.Warning("[Note Visual] Each-guide tint prewarm skipped: " + ex2.Message);
            }
            value.Candidates = null;
        }
        // Keys already built (by the other monitor's chart, or an earlier IsReady of this one) cost nothing: skip to the
        // next one that needs building.
        while (value.Next < value.Sprites.Count)
        {
            if (EachGuideTintPrewarmBuilt >= EachGuideTintPrewarmLimit)
            {
                value.Next = value.Sprites.Count;
                break;
            }
            int index = value.Next++;
            Sprite sprite = value.Sprites[index];
            Color color = value.Vertices[index];
            if (!(sprite == null) && !VisualTintSprites.ContainsKey(new VisualTintKey(sprite, value.Colours[index], new Color(color.r, color.g, color.b, 1f))) && !VisualTintFailures.Contains(sprite))
            {
                GetVisualTintSprite(sprite, value.Colours[index], color);
                EachGuideTintPrewarmBuilt++;
                value.Built++;
                break;
            }
        }
        return EachGuideTintPrewarmStep(value, start, value.Next >= value.Sprites.Count);
    }

    private static bool EachGuideTintPrewarmStep(EachGuideTintPrewarm prewarm, long start, bool done)
    {
        prewarm.Ticks += Stopwatch.GetTimestamp() - start;
        prewarm.Calls++;
        if (done && !prewarm.Logged)
        {
            prewarm.Logged = true;
            if (prewarm.Sprites.Count > 0)
            {
                MelonLogger.Msg("[Note Visual] Each-guide tint prewarm: built " + prewarm.Built + " of " + prewarm.Sprites.Count + " keys in " + ((double)prewarm.Ticks * 1000.0 / (double)Stopwatch.Frequency).ToString("F0", CultureInfo.InvariantCulture) + " ms over " + prewarm.Calls + " loading frames");
            }
        }
        return done;
    }

    private static EachGuideTintCandidates StartEachGuideTintPrewarm(GameCtrl controller, NotesReader reader)
    {
        PruneVisualSourceTextures();
        NoteGuide guide = GameNotePrefabContainer.Guide;
        if (guide == null || guide.transform.childCount == 0)
        {
            return null;
        }
        // NoteGuide.Awake: _spriteEachRender = transform.GetChild(0).GetComponent<SpriteRenderer>().
        SpriteRenderer component = guide.transform.GetChild(0).GetComponent<SpriteRenderer>();
        if (component == null || component.sprite == null)
        {
            return null;
        }
        EachGuideTintCandidates eachGuideTintCandidates = new EachGuideTintCandidates
        {
            EachBar = component.sprite
        };
        AddEachGuideTintVertex(eachGuideTintCandidates, component.color);
        if (GuideObjectList?.GetValue(controller) is List<NoteGuide> list)
        {
            foreach (NoteGuide item in list)
            {
                if (item != null && item.transform.childCount > 0)
                {
                    SpriteRenderer component2 = item.transform.GetChild(0).GetComponent<SpriteRenderer>();
                    if (component2 != null)
                    {
                        AddEachGuideTintVertex(eachGuideTintCandidates, component2.color);
                    }
                }
            }
        }
        VisualTimelines.TryGetValue(reader, out eachGuideTintCandidates.Timeline);
        eachGuideTintCandidates.NoteKinds = RuntimeMonitor(controller.MonitorIndex).NoteKinds;
        return eachGuideTintCandidates;
    }

    private static void AddEachGuideTintVertex(EachGuideTintCandidates candidates, Color color)
    {
        if (candidates.VertexKeys.Add(new VisualTintKey(null, 0, new Color(color.r, color.g, color.b, 1f)).Vertex))
        {
            candidates.VertexColours.Add(color);
        }
    }

    private static bool CollectEachGuideTintCandidates(EachGuideTintCandidates candidates, NoteDataList notes)
    {
        long num = Stopwatch.GetTimestamp() + (long)((double)Stopwatch.Frequency * EachGuideTintCollectBudgetMs / 1000.0);
        while (candidates.Note < notes.Count)
        {
            AddEachGuideTintNote(candidates, notes[candidates.Note++]);
            if (Stopwatch.GetTimestamp() >= num)
            {
                break;
            }
        }
        return candidates.Note >= notes.Count;
    }

    private static void AddEachGuideTintNote(EachGuideTintCandidates candidates, NoteData note)
    {
        if (note == null || IsBorrowed(note) || !VisualNotes.TryGetValue(note, out var value) || !HasEachGuideArc(note))
        {
            return;
        }
        List<VisualChange> value2 = null;
        bool flag = candidates.Timeline != null && candidates.Timeline.Streams.TryGetValue(value.Stream, out value2);
        if (!flag && IsEmptyVisual(value.Note.Base) && IsEmptyVisual(value.Note.Guide))
        {
            return;
        }
        List<int> colours = candidates.Colours;
        List<double> times = candidates.Times;
        colours.Clear();
        times.Clear();
        double num = (double)note.time.msec - 2000.0;
        if (value.Note.Base.Color.HasValue)
        {
            colours.Add(value.Note.Base.Color.Value);
            times.Add(num);
        }
        if (flag)
        {
            EachGuideColourList eachGuideColourList = GetEachGuideColourList(candidates, value2, value.Note);
            for (int i = 0; i < eachGuideColourList.Rgbs.Count; i++)
            {
                double num2 = eachGuideColourList.Times[i];
                colours.Add(eachGuideColourList.Rgbs[i]);
                times.Add((num2 > num) ? num2 : num);
            }
        }
        if (colours.Count == 0)
        {
            return;
        }
        bool flag2 = candidates.NoteKinds.TryGetValue(note.indexNote, out var value3) && (value3 == CustomNoteKind.Mine || value3 == CustomNoteKind.MineTouchBreak || value3 == CustomNoteKind.MineTouchStar);
        bool flag3 = flag2 || value.Note.Mine;
        if (flag3 && !candidates.MineLooked)
        {
            candidates.MineLooked = true;
            if (MineTextures.TryGetValue("mine", out var value4))
            {
                candidates.Mine = CreateSpriteFromTexture("mine", value4, candidates.EachBar);
            }
        }
        for (int j = 0; j < colours.Count; j++)
        {
            if (!flag2 || candidates.Mine == null)
            {
                AddEachGuideTintCandidate(candidates, candidates.EachBar, colours[j], times[j]);
            }
            if (flag3 && candidates.Mine != null)
            {
                AddEachGuideTintCandidate(candidates, candidates.Mine, colours[j], times[j]);
            }
        }
    }

    private static void AddEachGuideTintCandidate(EachGuideTintCandidates candidates, Sprite source, int rgb, double need)
    {
        foreach (Color vertexColour in candidates.VertexColours)
        {
            VisualTintKey key = new VisualTintKey(source, rgb, new Color(vertexColour.r, vertexColour.g, vertexColour.b, 1f));
            if (candidates.Slots.TryGetValue(key, out var value))
            {
                if (need < candidates.Needs[value])
                {
                    candidates.Needs[value] = need;
                }
            }
            else
            {
                candidates.Slots.Add(key, candidates.Sprites.Count);
                candidates.Sprites.Add(source);
                candidates.Rgbs.Add(rgb);
                candidates.KeyVertices.Add(vertexColour);
                candidates.Needs.Add(need);
            }
        }
    }

    private static void FinishEachGuideTintCandidates(EachGuideTintCandidates candidates, EachGuideTintPrewarm prewarm)
    {
        List<double> needs = candidates.Needs;
        foreach (int item in Enumerable.Range(0, candidates.Sprites.Count).OrderBy((int i) => needs[i]).Take(EachGuideTintPrewarmLimit))
        {
            prewarm.Sprites.Add(candidates.Sprites[item]);
            prewarm.Colours.Add(candidates.Rgbs[item]);
            prewarm.Vertices.Add(candidates.KeyVertices[item]);
        }
    }

    private static bool HasEachGuideArc(NoteData note)
    {
        List<NoteData> eachChild = note.eachChild;
        if (eachChild == null)
        {
            return false;
        }
        int num = 0;
        int num2 = -note.startButtonPos;
        foreach (NoteData item in eachChild)
        {
            if (item == null)
            {
                return true;
            }
            int num3 = item.startButtonPos + num2;
            if (num3 < 0)
            {
                num3 += 8;
            }
            int num4 = ((num3 <= 4) ? (45 * num3) : (45 * (8 - num3) * -1));
            if (Mathf.Abs(num4) > Mathf.Abs(num))
            {
                num = num4;
            }
        }
        return num != 0;
    }

    private static bool IsEmptyVisual(VisualValue value)
    {
        if (!value.Color.HasValue && !value.X.HasValue)
        {
            return !value.Alpha.HasValue;
        }
        return false;
    }

    private static EachGuideColourList GetEachGuideColourList(EachGuideTintCandidates candidates, List<VisualChange> stream, VisualNote note)
    {
        (List<VisualChange>, bool, bool, bool, string) key = (stream, note.Mine, note.Break, note.Each, note.Family);
        if (!candidates.ColourLists.TryGetValue(key, out var value))
        {
            value = CollectEachGuideColours(stream, note);
            candidates.ColourLists.Add(key, value);
        }
        return value;
    }

    private static EachGuideColourList CollectEachGuideColours(List<VisualChange> stream, VisualNote note)
    {
        EachGuideColourList eachGuideColourList = new EachGuideColourList();
        Dictionary<int, int> dictionary = new Dictionary<int, int>();
        string family = note.Family;
        foreach (VisualChange item in stream)
        {
            if (item == null || item.Kind != "color" || item.Value == null || !item.Value.Color.HasValue)
            {
                continue;
            }
            string type = item.Type;
            if (note.Mine ? (type == "mine") : ((note.Break && type == "break") || (note.Each && type == "each") || type == family || (family == "star" && type == "tap") || type == ""))
            {
                int value = item.Value.Color.Value;
                double time = item.Time;
                if (dictionary.TryGetValue(value, out var value2))
                {
                    if (double.IsNaN(time) || time < eachGuideColourList.Times[value2])
                    {
                        eachGuideColourList.Times[value2] = time;
                    }
                }
                else
                {
                    dictionary.Add(value, eachGuideColourList.Rgbs.Count);
                    eachGuideColourList.Rgbs.Add(value);
                    eachGuideColourList.Times.Add(time);
                }
            }
        }
        return eachGuideColourList;
    }
}
