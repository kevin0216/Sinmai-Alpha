using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Manager;
using Monitor;
using SinmaiAlpha.ChartVisuals;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    private sealed class ReserveVisual
    {
        public int Monitor;
        public List<TouchReserve.ReserveData> Queue;
        public SpriteRenderer[] Renderers;
    }
    private static ConditionalWeakTable<NotesReader, Dictionary<int, NoteData>> ReserveVisualNotes = new();
    private static readonly Dictionary<TouchReserve, ReserveVisual> ReserveVisuals = new();

    private static void TrackReserveVisuals(TouchReserve owner, List<TouchReserve.ReserveData> queue, SpriteRenderer[] renderers)
    {
        if (!TouchReserveMonitors.TryGetValue(owner, out var monitor)) return;
        if (!ReserveVisuals.TryGetValue(owner, out var state))
            ReserveVisuals[owner] = state = new ReserveVisual();
        state.Monitor = monitor.Index; state.Queue = queue; state.Renderers = renderers;
    }

    private static void RestoreReserveVisuals(int monitor)
    {
        foreach (var state in ReserveVisuals.Values)
            if (state.Monitor == monitor)
                foreach (var renderer in state.Renderers)
                    if (renderer != null && VisualOverlays.TryGetValue(renderer, out var overlay)) overlay.Restore();
    }

    private static void ApplyReserveVisuals(int monitor, NotesReader reader, VisualTimeline timeline)
    {
        Dictionary<int, NoteData> value = ReserveVisualNotes.GetValue(reader, (NotesReader r) =>
        {
            Dictionary<int, NoteData> dictionary = new Dictionary<int, NoteData>();
            foreach (NoteData note in r.GetNoteList())
            {
                if (note != null && note.indexNote >= 0)
                {
                    dictionary[note.indexNote] = note;
                }
            }
            return dictionary;
        });
        foreach (KeyValuePair<TouchReserve, ReserveVisual> reserveVisual in ReserveVisuals)
        {
            ReserveVisual value2 = reserveVisual.Value;
            if (value2.Monitor != monitor || reserveVisual.Key == null || !reserveVisual.Key.gameObject.activeInHierarchy)
            {
                continue;
            }
            for (int num = 0; num < value2.Renderers.Length && num < value2.Queue.Count; num++)
            {
                int index = value2.Queue[num].Index;
                if (value.TryGetValue(index, out var value3) && VisualNotes.TryGetValue(value3, out var value4))
                {
                    VisualValue visualValue = EmptyVisualValue;
                    if (timeline != null && timeline.Streams.TryGetValue(value4.Stream, out var value5))
                    {
                        visualValue = VisualState.Resolve(value5, NotesManager.GetCurrentMsec(), value4.Note);
                    }
                    // ApplyVisualPart only reads its arguments and keeps no reference to them, so per-frame scratch
                    // objects (only Native/Alpha are ever written) replace the three allocations per reserve symbol.
                    // The fields are filled in the order the object initialisers evaluated them, inside the try so a
                    // throw cannot leave Native pinning a renderer.
                    try
                    {
                        ReserveScratchPart.Native = value2.Renderers[num];
                        ReserveScratchLive.Alpha = visualValue.Alpha;
                        ReserveScratchBase.Alpha = value4.Note.Base.Alpha;
                        ApplyVisualPart(ReserveScratchPart, ReserveScratchLive, ReserveScratchBase);
                    }
                    finally
                    {
                        ReserveScratchPart.Native = null;
                    }
                }
            }
        }
    }


    private static void PruneDeadTouchReserves()
    {
        int frameCount = Time.frameCount;
        if (frameCount == _touchReservePruneFrame)
        {
            return;
        }
        _touchReservePruneFrame = frameCount;
        List<TouchReserve> list = TouchReservePruneScratch;
        try
        {
            foreach (TouchReserve key in TouchReserveMonitors.Keys)
            {
                if (key == null)
                {
                    list.Add(key);
                }
            }
            for (int i = 0; i < list.Count; i++)
            {
                TouchReserveMonitors.Remove(list[i]);
            }
            list.Clear();
            foreach (KeyValuePair<TouchReserve, ReserveVisual> reserveVisual in ReserveVisuals)
            {
                if (reserveVisual.Key == null && AllDestroyed(reserveVisual.Value.Renderers))
                {
                    list.Add(reserveVisual.Key);
                }
            }
            for (int j = 0; j < list.Count; j++)
            {
                ReserveVisuals.Remove(list[j]);
            }
            if (list.Count > 0)
            {
                RebuildReserveVisuals();
            }
        }
        finally
        {
            list.Clear();
        }
    }

    private static void RebuildReserveVisuals()
    {
        List<KeyValuePair<TouchReserve, ReserveVisual>> list = ReserveVisualRebuildScratch;
        try
        {
            foreach (KeyValuePair<TouchReserve, ReserveVisual> reserveVisual in ReserveVisuals)
            {
                list.Add(reserveVisual);
            }
            ReserveVisuals.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                ReserveVisuals.Add(list[i].Key, list[i].Value);
            }
        }
        finally
        {
            list.Clear();
        }
    }

    private static bool AllDestroyed(SpriteRenderer[] renderers)
    {
        if (renderers == null)
        {
            return false;
        }
        foreach (SpriteRenderer spriteRenderer in renderers)
        {
            if (spriteRenderer != null)
            {
                return false;
            }
        }
        return true;
    }
}
