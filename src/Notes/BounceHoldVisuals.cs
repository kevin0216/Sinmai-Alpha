using SinmaiAlpha.ChartVisuals;
using System;
using DB;
using MAI2.Util;
using Manager;
using Monitor;
using UnityEngine;

namespace SinmaiAlpha.Notes;

public partial class CustomNoteTypes
{
    // Native Hold.Execute derives its tail from a straight approach. Apply the
    // complete bounce body after Execute/NoteCheck, retaining their input clock.
    private static bool TryBounceHoldPresentation(NoteBase owner)
    {
        if (owner == null || owner.IsEnd() || (!(owner is HoldNote) && !(owner is BreakHoldNote)))
        {
            return false;
        }
        int noteIndex = owner.GetNoteIndex();
        if (!BounceDurationByNoteIndex.TryGetValue(noteIndex, out var value) || value <= 0f)
        {
            return false;
        }
        GameObject gameObject = NoteObjectOf(owner);
        if (gameObject == null)
        {
            return false;
        }
        float num = AppearMsecOf(owner);
        float num2 = TailMsecOf(owner);
        float currentMsec = NotesManager.GetCurrentMsec();
        string type = (SvTypeByNoteIndex.TryGetValue(noteIndex, out var value2) ? value2 : "hold");
        RingValue settings = RingSettings(noteIndex, num, type);
        float num3 = (HsMultByNoteIndex.TryGetValue(noteIndex, out var value3) ? value3 : 1f);
        float num4 = DefaultMsecOf(owner);
        float num5 = (ReferenceMotionByNoteIndex.Contains(noteIndex) ? (RingViewSpeed(num4) * 0.001f) : (3.5750003f / Math.Max(num4, 0.001f))) * num3;
        float noteScroll = GetNoteScroll(noteIndex, num);
        float noteScroll2 = GetNoteScroll(noteIndex, num2);
        float noteScroll3 = GetNoteScroll(noteIndex, currentMsec);
        float num6 = ((Math.Abs(settings.Destroy - settings.Spawn) < 1E-06f) ? 1f : ((float)Math.Sign(settings.Destroy - settings.Spawn)));
        bool flag = currentMsec < num;
        float num12;
        float radius;
        bool flag3;
        if (flag)
        {
            float num7 = BounceHoldDirection(noteIndex, num, num3);
            float num8 = 1f + num7 * num3 * (noteScroll3 - noteScroll) / (value * 1000f);
            float num9 = BounceHoldTakeoff(noteIndex, num, noteScroll, value * 1000f, num3 * num7);
            bool flag2 = currentMsec >= num9 && (settings.Once || num8 >= -1E-06f);
            SetBounceAlpha(owner, flag2 ? 1f : 0f);
            SetBounceChildrenActive(owner, flag2);
            SetBounceGuideActive(owner, flag2);
            if (!flag2)
            {
                GameObject value4 = EndPointOf(owner);
                if (value4 != null)
                {
                    value4.SetActive(value: false);
                }
                return true;
            }
            float num10 = 2f * num8 - 1f;
            float num11 = (settings.Destroy - settings.Spawn) * (1f - num10 * num10);
            num12 = ((num7 < 0f) ? (settings.Destroy + num11) : (settings.Destroy - num11));
            float value5 = num6 * num5 * (noteScroll2 - noteScroll);
            float num13 = (float)Math.Sign(value5) * Math.Min(Math.Abs(value5), Math.Abs(settings.Destroy - settings.Spawn));
            radius = num12 - num13;
            flag3 = Math.Abs(num13) > 0.001f;
        }
        else
        {
            SetBounceAlpha(owner, 1f);
            SetBounceChildrenActive(owner, active: true);
            SetBounceGuideActive(owner, active: true);
            if (TryRingPresentation(owner, tail: false, out var _))
            {
                return false;
            }
            num12 = settings.Destroy;
            ScrollVisualTiming.Presentation presentation2 = ScrollVisualTiming.Ring(noteScroll2, noteScroll3, num5, settings.Spawn, settings.Destroy, settings.Once && RingEverCrossed(owner, tail: true, noteScroll2, num5, settings, currentMsec));
            radius = presentation2.Radius;
            flag3 = presentation2.Running;
        }
        float num14 = RingNativeY(owner, num12);
        float num15 = RingNativeY(owner, radius);
        if (flag)
        {
            float noteSpeedOffsetY = GetNoteSpeedOffsetY(owner, EndPosOf(owner) - StartPosOf(owner));
            num14 += noteSpeedOffsetY;
            num15 += noteSpeedOffsetY;
        }
        bool flag4 = currentMsec < num2;
        UnityEngine.Vector3 localPosition = gameObject.transform.localPosition;
        localPosition.y = (flag4 ? ((num14 + num15) * 0.5f) : num14);
        gameObject.transform.localPosition = localPosition;
        float value6 = Singleton<GamePlayManager>.Instance.GetGameScore(owner.MonitorId).UserOption.NoteSize.GetValue();
        float y = ((flag4 && num14 < num15) ? (-1f) : 1f);
        gameObject.transform.localScale = new UnityEngine.Vector3(value6, y, 0f);
        SpriteRenderer spriteRenderer = NoteSpriteOf(owner);
        spriteRenderer.size = new UnityEngine.Vector2(spriteRenderer.size.x, (flag4 ? Math.Abs(num14 - num15) : 0f) + 140f);
        SpriteRenderer spriteRenderer2 = NoteSpriteExOf(owner);
        if (spriteRenderer2 != null)
        {
            spriteRenderer2.size = spriteRenderer.size;
        }
        SpriteRenderer value7 = EffectSpriteOf(owner);
        if (value7 != null)
        {
            value7.size = spriteRenderer.size;
        }
        SyncBreakHoldGlow(owner, spriteRenderer);
        GameObject value8 = EndPointOf(owner);
        if (value8 != null)
        {
            value8.SetActive(flag4 & flag3);
            UnityEngine.Vector3 localPosition2 = value8.transform.localPosition;
            localPosition2.y = num15;
            value8.transform.localPosition = localPosition2;
            value8.transform.localScale = UnityEngine.Vector3.one;
        }
        NoteGuide noteGuide = NoteGuideOf(owner);
        if (noteGuide != null)
        {
            float num16 = Math.Abs(num12 / 4.8f);
            noteGuide.transform.localScale = new UnityEngine.Vector3(num16, num16, 1f);
            noteGuide.SetAlpha(1f);
        }
        return true;
    }

    private static float BounceHoldDirection(int index, float appear, float hs)
    {
        if (Math.Abs(hs) < .000001f) return 1;
        if (NoteScrollTableByNoteIndex.TryGetValue(index, out var table))
            for (var i = table.Count - 2; i >= 0; i--)
            {
                if (table[i].Msec >= appear) continue;
                var delta = table[i + 1].Scroll - table[i].Scroll;
                if (Math.Abs(delta) > .000001f) return Math.Sign(hs * delta);
            }
        return Math.Sign(hs);
    }

    private static float BounceHoldTakeoff(int index, float appear, float headScroll, float bounceMsec, float effectiveHs)
    {
        if (Math.Abs(effectiveHs) < .000001f) return appear;
        if (NoteScrollTableByNoteIndex.TryGetValue(index, out var table))
            return ScrollVisualTiming.FirstCrossing(table, headScroll, effectiveHs / bounceMsec, 0, 1);
        return appear - bounceMsec / effectiveHs;
    }

    private static float BounceHoldActivationLead(float lead, NoteData note)
    {
        if (note == null || !note.type.isHold() || !BounceDurationByNoteIndex.TryGetValue(note.indexNote, out var bounce) || bounce <= 0) return lead;
        var index = note.indexNote;
        var hs = HsMultByNoteIndex.TryGetValue(index, out var multiplier) ? multiplier : 1f;
        var start = BounceHoldTakeoff(index, note.time.msec, GetNoteScroll(index, note.time.msec), bounce * 1000f,
            hs * BounceHoldDirection(index, note.time.msec, hs));
        return float.IsNaN(start) ? lead : Math.Min(10000f, Math.Max(lead, note.time.msec - start + 16f));
    }
}
