using Manager;
using Monitor;
using UnityEngine;

namespace SinmaiAlpha.Notes;

/// <summary>
/// 独立地雷 Touch 类（B 类触摸）。
/// 判定/贴图自持：Judge / JudgeToolate override 反转（TouchNoteB.Judge 是 virtual override，可再 override）。
/// 判定规则（标准地雷，2026-08-26）：碰到 → Miss；没碰到（超时）→ Critical Perfect（InvertStandard）。
/// SE：override PlayJudgeSe 按反转后判定播（命中播 Miss 音、没碰播 CP 音），防音画不同步。
/// 地雷绝赞（MBTTP，kind=MineTouchBreak）：统计按 BREAK（由 TouchNoteB.EndNote transpiler 处理）。
/// </summary>
public class MineTouchNoteB : TouchNoteB, ISelfJudgingMineNote
{
    private bool _isMineTouchBreak;

    internal bool IsMineTouchBreak => _isMineTouchBreak;

    public override void Initialize(NoteData note)
    {
        base.Initialize(note);
        _isMineTouchBreak = CustomNoteTypes.GetNoteKind(note) == CustomNoteTypes.CustomNoteKind.MineTouchBreak;
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

    private new void Awake()
    {
    }

    internal void RunBaseAwake() => base.Awake();

    public static MineTouchNoteB CreateFrom(TouchNoteB prefab, Transform parent)
    {
        return MineNoteFactory.CreateFrom<MineTouchNoteB, TouchNoteB>(prefab, parent, m => m.RunBaseAwake());
    }
}
