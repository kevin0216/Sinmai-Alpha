using Manager;
using Monitor;
using UnityEngine;

namespace SinmaiAlpha.Notes;

/// <summary>独立地雷 Break Tap 类（判定/贴图自持，模式同 MineTapNote）。</summary>
public class MineBreakNote : BreakNote, ISelfJudgingMineNote
{
    public override void Initialize(NoteData note)
    {
        base.Initialize(note);
        CustomNoteTypes.ApplyMineTexturesToObject(gameObject);
    }

    protected override bool Judge()
    {
        bool flag = base.Judge();
        if (flag)
        {
            MineJudgeHelper.LogFirstUse();
            // The protected NoteBase.JudgeResult field, set directly (was a Traverse write through MineJudgeHelper.SetJudgeResult).
            JudgeResult = MineJudgeHelper.InvertStandard(GetJudgeResult());
        }
        return flag;
    }

    protected override bool JudgeToolate()
    {
        bool flag = base.JudgeToolate();
        if (flag)
        {
            MineJudgeHelper.LogFirstUse();
            // The protected NoteBase.JudgeResult field, set directly (was a Traverse write through MineJudgeHelper.SetJudgeResult).
            JudgeResult = MineJudgeHelper.InvertStandard(GetJudgeResult());
        }
        return flag;
    }

    protected override void PlayJudgeSe()
    {
        // The protected NoteBase.ShotJudgeSound field, read and set directly (was two Traverse lookups per call).
        if (!ShotJudgeSound)
        {
            NoteJudge.JudgeBox judge = NoteJudge.ConvertJudge(MineJudgeHelper.InvertStandard(GetJudgeResult()));
            if (IsExNote)
            {
                ReserveExJudgeSe(judge);
            }
            else
            {
                ReserveTapJudgeSe(judge);
            }
            ShotJudgeSound = true;
        }
    }

    protected override void SetEach(bool eachFlag)
    {
        base.SetEach(eachFlag);
        // The protected BreakNote.EffectSprite field, read directly (was a Traverse read).
        CustomNoteTypes.ApplyMineEffectTexture(EffectSprite, "tap_break_eff_mine");
    }

    private new void Awake()
    {
    }

    internal void RunBaseAwake() => base.Awake();

    public static MineBreakNote CreateFrom(BreakNote prefab, Transform parent)
    {
        return MineNoteFactory.CreateFrom<MineBreakNote, BreakNote>(prefab, parent, m => m.RunBaseAwake());
    }
}
