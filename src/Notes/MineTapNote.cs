using Manager;
using Monitor;
using UnityEngine;

namespace SinmaiAlpha.Notes;

/// <summary>
/// 独立地雷 Tap 类：地雷池里放的就是 MineTapNote 对象，不再复用原版 TapNote 组件。
///
/// - 贴图：override Initialize，原版贴图赋值后自动覆盖成地雷贴图（无需 MineNoteBehaviour）。
/// - 判定：override Judge / JudgeToolate —— 命中 -> Miss(TooLate)，未命中 -> Critical。
///   原版把结果写在 private 字段 JudgeResult 里，由 MineJudgeHelper 用 Traverse 写回；
///   全局判定 transpiler（MineJudgeInversion）通过 ISelfJudgingMineNote 识别并放行。
/// - SE：override PlayJudgeSe，按反转后的判定播对应音效（打中播 Miss 音，没打中播 Critical 音）。
/// - Awake 被 new 遮蔽：组件替换（MineNoteFactory.CreateFrom）时先恢复 prefab 序列化字段，
///   再手动执行基类 Awake 链，避免字段为 null 时原版 Awake 抛异常。
/// </summary>
public class MineTapNote : TapNote, ISelfJudgingMineNote
{
    public override void Initialize(NoteData note)
    {
        base.Initialize(note);
        // 此时原版贴图已按 TapDesign 赋值，覆盖成地雷贴图。
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

    // Unity 消息遮蔽：CreateFrom 里恢复字段前 AddComponent 会触发 Awake，
    // 此时序列化字段（NoteObj 等）还是 null，原版 Awake 会 NRE，所以这里先空实现。
    private new void Awake()
    {
    }

    /// <summary>CreateFrom 专用：字段恢复后补执行基类 Awake 链（缓存 SpriteRender 等）。</summary>
    internal void RunBaseAwake()
    {
        // base.Awake() 直接调用 TapNote.Awake（override），其内部再 call 基类 NoteBase.Awake。
        base.Awake();
    }

    public static MineTapNote CreateFrom(TapNote prefab, Transform parent)
    {
        return MineNoteFactory.CreateFrom<MineTapNote, TapNote>(prefab, parent, m => m.RunBaseAwake());
    }
}
