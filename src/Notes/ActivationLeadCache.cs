using SinmaiAlpha.ChartVisuals;
using System.Collections.Generic;
using Manager;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class RingActivationCacheEntry
    {
        internal NoteData Note;
        internal int NoteIndex, TableCount;
        internal NotesTypeID NoteTypeOwner;
        internal NotesTypeID.Def? NoteType;
        internal float Appear, Duration, Hs, Target, Spawn, Destroy, Value;
        internal string ScrollType;
        internal bool ReferenceMotion, Once;
        internal List<(float Msec, float Scroll, float Max)> Table;
    }

    // A reader's runtime state owns the entries. Fresh loads/reset replace the
    // state; a rebuild replaces each immutable scroll table. Neither can reuse
    // an earlier chart's activation time, including the other player's chart.





    private static float GetRingFirstVisibleTimeCached(NoteData note, float duration)
    {
        ChartRuntimeState current = RuntimeCharts.Current;
        if (note == null || duration <= 0f || !current.NoteScrollTableByNoteIndex.TryGetValue(note.indexNote, out List<(float, float, float)> value) || value.Count == 0 || !current.HsMultByNoteIndex.TryGetValue(note.indexNote, out var value2) || !current.SvScrollPosByNoteIndex.TryGetValue(note.indexNote, out var value3) || !current.SvTypeByNoteIndex.TryGetValue(note.indexNote, out var value4))
        {
            return RingFirstVisibleTime(note, duration);
        }
        int indexNote = note.indexNote;
        float msec = note.time.msec;
        NotesTypeID.Def? def = note.type?.getEnum();
        bool flag = current.ReferenceMotionByNoteIndex.Contains(indexNote);
        RingValue ringValue = RingSettings(indexNote, msec, value4);
        Dictionary<int, RingActivationCacheEntry> value5 = current.RingActivationLeadCache ?? (current.RingActivationLeadCache = new Dictionary<int, RingActivationCacheEntry>());
        if (value5.TryGetValue(indexNote, out var value6) && value6.Note == note && value6.NoteIndex == indexNote && (object)value6.NoteTypeOwner == note.type && value6.NoteType == def && value6.Appear == msec && value6.Duration == duration && value6.Table == value && value6.TableCount == value.Count && value6.Hs == value2 && value6.Target == value3 && value6.ScrollType == value4 && value6.ReferenceMotion == flag && value6.Spawn == ringValue.Spawn && value6.Destroy == ringValue.Destroy && value6.Once == ringValue.Once)
        {
            return value6.Value;
        }
        float num = RingFirstVisibleTime(note, duration);
        if (value6 == null)
        {
            value6 = (value5[indexNote] = new RingActivationCacheEntry());
        }
        value6.Note = note;
        value6.NoteIndex = indexNote;
        value6.NoteTypeOwner = note.type;
        value6.NoteType = def;
        value6.Appear = msec;
        value6.Duration = duration;
        value6.Table = value;
        value6.TableCount = value.Count;
        value6.Hs = value2;
        value6.Target = value3;
        value6.ScrollType = value4;
        value6.ReferenceMotion = flag;
        value6.Spawn = ringValue.Spawn;
        value6.Destroy = ringValue.Destroy;
        value6.Once = ringValue.Once;
        value6.Value = num;
        return num;
    }
}
