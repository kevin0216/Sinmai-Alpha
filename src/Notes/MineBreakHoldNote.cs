using Manager;
using Monitor;
using UnityEngine;

namespace SinmaiAlpha.Notes;

/// <summary>
/// 独立地雷 BreakHold 类。
/// 判定：JudgeTotalResult 非虚无法 override，仍由 transpiler + MineNoteBehaviour 负责。
/// </summary>
public class MineBreakHoldNote : BreakHoldNote
{
    public override void Initialize(NoteData note)
    {
        base.Initialize(note);
        // 显式挂 MineNoteBehaviour：本类不实现 ISelfJudgingMineNote，判定反转靠全局
        // transpiler + behaviour 识别；原依赖 NoteSpeedApplyPatch（已删）经基类 postfix 挂载。
        CustomNoteTypes.EnsureMineBehaviour(this, note);
        CustomNoteTypes.ApplyMineTexturesToObject(gameObject);
    }

    protected override void SetEach(bool eachFlag)
    {
        base.SetEach(eachFlag);
        // The protected BreakHoldNote.BreakEffectSprite field, read directly (was a Traverse read).
        CustomNoteTypes.ApplyMineEffectTexture(BreakEffectSprite, "hold_break_eff_mine");
    }

    private new void Awake()
    {
    }

    internal void RunBaseAwake() => base.Awake();

    public static MineBreakHoldNote CreateFrom(BreakHoldNote prefab, Transform parent)
    {
        return MineNoteFactory.CreateFrom<MineBreakHoldNote, BreakHoldNote>(prefab, parent, m => m.RunBaseAwake());
    }
}
