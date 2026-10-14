using Manager;
using Monitor;
using UnityEngine;

namespace SinmaiAlpha.Notes;

/// <summary>
/// 地雷 TouchStar（MNSTP）独立类（B 类触摸，五瓣星）。
/// 贴图：touch_star_mine（素材方向与 touch_star 一样需要垂直翻转，LoadMineTextures 处理）。
/// 判定：同地雷 touch（MineTouchNoteB）——命中反转成 Miss，未命中反转成 Critical Perfect。
/// 计分：同 touchstar（Touch 分，非 break；由 TouchNoteB.EndNote transpiler 的 GetTouchScoreKind 处理，
///      本类继承 TouchStarNoteB → IsBreakStar=false → Touch）。
/// 结构：继承 TouchStarNoteB 拿到五瓣星贴图/SetEach/PlayJudgeSe/IsBreakStar，
///      Initialize 里 base 先铺 touch_star 结构，再覆盖成 touch_star_mine 地雷贴图。
/// </summary>
public class MineTouchStarNoteB : TouchStarNoteB
{
    public override void Initialize(NoteData note)
    {
        base.Initialize(note);
        CustomNoteTypes.ApplyMineTouchStarTexturesToObject(gameObject);
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

    // RunBaseAwake 继承自 TouchStarNoteB（MineNoteFactory 的 setup 回调调用）。

    public new static MineTouchStarNoteB CreateFrom(TouchNoteB prefab, Transform parent)
    {
        return MineNoteFactory.CreateFrom<MineTouchStarNoteB, TouchNoteB>(prefab, parent, m => m.RunBaseAwake());
    }
}
