using System;
using HarmonyLib;
using MAI2.Util;
using Manager;
using Monitor;
using UnityEngine;

namespace SinmaiAlpha.Notes;

// 弹跳轨迹、可见性和附属特效同步。
public partial class CustomNoteTypes
{
    /// <summary>弹跳视觉：GetNoteYPosition 后置改写音符位置（基类，Tap/Star/Hold/Break 全覆盖）。
    /// __result 返回弹跳 y（HoldNote.Execute 用返回值算 body 长度/中点，不能改 0）。
    /// 外框（NoteGuide 弧形引导环）：按【原版运动轨迹 + 弹跳位移】处理——贴图/缩放/朝向
    /// 全部保持原版逻辑，只在其原版 local 位置上叠加 delta（= 弹跳位置 − 原版位置），
    /// 与音符的相对关系与原版任意时刻一致（原版判定线处对齐 → 弹跳中自然对齐）。
    /// 光效/绝赞挂到 NoteObj 下跟随（ReparentBounceEffects）。
    /// 弹跳窗口前：音符本体隐藏（alpha=0，对应 Majdata forceRenderingOff）；
    /// 判定后：外框位移归零（原版），恢复可见。</summary>
    public static void BounceNoteVisualPostfix(NoteBase __instance, ref float __result)
    {
        if (BounceSegmentsByType.Count == 0 || __instance == null || __instance is TouchHoldC || __instance is HoldNote || __instance is BreakHoldNote)
        {
            return;
        }
        try
        {
            // Typed field reads in place of a Traverse plus a Field chain per read. NoteIndex is read once: the
            // reads have no side effects and nothing in between can change it.
            float value = TraverseRead(NoteRefs.AppearMsec, __instance, "AppearMsec");
            int noteIndex = NoteIndexOf(__instance);
            if (!BounceDurationByNoteIndex.TryGetValue(noteIndex, out var value2) || value2 <= 0f)
            {
                if (!BounceTypeByNoteIndex.TryGetValue(noteIndex, out var value3))
                {
                    return;
                }
                value2 = GetBounceDurationAt(value, value3);
                if (value2 <= 0f)
                {
                    return;
                }
            }
            float num = NotesManager.GetCurrentMsec() - value;
            float num2 = value2 * 1000f;
            if (num < -num2)
            {
                SetBounceAlpha(__instance, 0f);
                SetBounceChildrenActive(__instance, active: false);
                SetBounceGuideActive(__instance, active: false);
            }
            else if (num < 0f)
            {
                int value4 = noteIndex;
                float value5 = 1.225f;
                if (!SpawnRadiusByNoteIndex.TryGetValue(value4, out value5) && BounceTypeByNoteIndex.TryGetValue(value4, out var value6))
                {
                    value5 = GetSpawnRadiusAt(value, value6);
                }
                float num3 = Mathf.Clamp(num + num2, 0f, num2);
                float num4 = num2 * 0.5f;
                float num5 = (RingValuesByNoteIndex.TryGetValue(value4, out var value7) ? value7.Destroy : 4.8f);
                float num6 = 8f * (num5 - value5) / (num2 * num2);
                float num7 = num3 - num4;
                float num8 = value5 + 0.5f * num6 * num7 * num7;
                float num9 = (num8 - 1.225f) / 3.5750003f;
                float value8 = TraverseRead(NoteRefs.StartPos, __instance, "StartPos");
                float num10 = TraverseRead(NoteRefs.EndPos, __instance, "EndPos") - value8;
                float noteSpeedOffsetY = GetNoteSpeedOffsetY(__instance, num10);
                float num11 = value8 + num10 * num9 + noteSpeedOffsetY;
                __result = num11;
                try
                {
                    NoteGuide value9 = BounceGuideOf(__instance);
                    Transform transform = ((value9 != null) ? value9.transform : NoteGuideTransOf(__instance));
                    if (transform != null)
                    {
                        float num12 = Mathf.Clamp01(num8 / 4.8f);
                        if (num12 < 0.01f)
                        {
                            num12 = 0.01f;
                        }
                        transform.localScale = new UnityEngine.Vector3(num12, num12, 1f);
                    }
                }
                catch
                {
                }
                ReparentBounceEffects(__instance);
                SetBounceAlpha(__instance, 1f);
                SetBounceChildrenActive(__instance, active: true);
                SetBounceGuideActive(__instance, active: true);
            }
            else
            {
                SetBounceAlpha(__instance, 1f);
                SetBounceChildrenActive(__instance, active: true);
                SetBounceGuideActive(__instance, active: true);
            }
        }
        catch
        {
        }
    }

    /// <summary>原版 GetNoteYPosition 的返回 = 插值位置 + V_9（音符速度常量偏移）：
    /// V_9 = (EndPos−StartPos) × (−0.008333334) × (GetNoteSpeed()/150 − 1)。
    /// GetNoteSpeed() 读 UserOption 的原始选项值（不经 HS postfix），与原版 IL 一致。</summary>
    private static float GetNoteSpeedOffsetY(NoteBase note, float span)
    {
        try
        {
            var score = Singleton<GamePlayManager>.Instance.GetGameScore(note.MonitorId, -1);
            var speed = score.UserOption.GetNoteSpeed;
            var v8 = Convert.ToInt32(speed) / 150f;
            return span * -0.008333334f * (v8 - 1f);
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>光效/绝赞挂到 NoteObj 下（保持世界位置），随音符弹跳移动（音符的一部分，
    /// 圆形/星形光效旋转无碍）。音符回池复用 Initialize 时层级由原版重新设置。
    /// 外框（NoteGuide 弧形）不挂载——见 bounce 分支的 delta 位移方案。</summary>




    /// <summary>外框（NoteGuide 判定提示线）的显示开关。弹跳窗口前隐藏（note 弹出前外框不可见），
    /// 弹跳窗口内显示并跟随缩放。SetActive 不会被原版每帧覆盖（原版 SetAlpha 仍执行，恢复后正常）。</summary>


    /// <summary>光效（Break 脉冲）的显示开关（SetActive）。它的颜色每帧被原版
    /// GetNoteYPosition 重设，用颜色隐藏无效（且会破坏脉冲观感），只能开关 active。
    /// 外框（NoteGuide）不隐藏——原版淡入，无闪现问题。</summary>


    /// <summary>弹跳音符激活瞬间（base Initialize）隐藏光效并把光效挂到 NoteObj 下
    /// ——否则激活帧光效会在生成点闪现一瞬。音符贴图的最终隐藏由各子类 Initialize 后置
    /// 完成（子类会重设颜色覆盖 alpha），外框不隐藏（原版淡入）。</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NoteBase), "Initialize")]
    public static void BounceHideOnInit(NoteBase __instance)
    {
        if (!FeaturesEnabled(__instance) || BounceDurationByNoteIndex.Count == 0 || __instance == null || __instance is TouchHoldC || __instance is TouchNoteB)
        {
            return;
        }
        try
        {
            int value = NoteIndexOf(__instance);
            if (BounceDurationByNoteIndex.TryGetValue(value, out var value2) && !(value2 <= 0f))
            {
                SetBounceChildrenActive(__instance, active: false);
                SetBounceAlpha(__instance, 0f);
                ReparentBounceEffects(__instance);
            }
        }
        catch
        {
        }
    }

    /// <summary>注册流程内、渲染前把音符贴图 alpha 最终归零（子类 Initialize 会把颜色重置回 1，
    /// 而 UpdateCtrl 注册在 UpdateNotes 之后——不在注册流程内隐藏就会闪一帧）。</summary>
    private static void BounceHideVisualFinal(NoteBase note)
    {
        if (BounceDurationByNoteIndex.Count == 0 || note == null)
        {
            return;
        }
        try
        {
            int value = NoteIndexOf(note);
            if (BounceDurationByNoteIndex.TryGetValue(value, out var value2) && !(value2 <= 0f))
            {
                SetBounceAlpha(note, 0f);
            }
        }
        catch
        {
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TapNote), "Initialize")]
    public static void BounceHideTapInit(TapNote __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        BounceHideVisualFinal(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BreakNote), "Initialize")]
    public static void BounceHideBreakInit(BreakNote __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        BounceHideVisualFinal(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HoldNote), "Initialize")]
    public static void BounceHideHoldInit(HoldNote __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        BounceHideVisualFinal(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BreakHoldNote), "Initialize")]
    public static void BounceHideBreakHoldInit(BreakHoldNote __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        BounceHideVisualFinal(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StarNote), "Initialize")]
    public static void BounceHideStarInit(StarNote __instance)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return; }

        BounceHideVisualFinal(__instance);
    }


    private static void ReparentBounceEffects(NoteBase tr)
    {
        try
        {
            GameObject value = BounceNoteObjectOf(tr);
            if (value == null)
            {
                return;
            }
            Transform transform = value.transform;
            SpriteRenderer value2 = EffectSpriteOf(tr);
            if (value2 != null && value2.transform.parent != transform)
            {
                value2.transform.SetParent(transform, worldPositionStays: true);
            }
        }
        catch
        {
        }
        try
        {
            GameObject value3 = BounceNoteObjectOf(tr);
            if (!(value3 == null))
            {
                Transform transform2 = value3.transform;
                GameObject value4 = NoteExObjectOf(tr);
                if (value4 != null && value4.transform.parent != transform2)
                {
                    value4.transform.SetParent(transform2, worldPositionStays: true);
                }
            }
        }
        catch
        {
        }
    }

    private static void SetBounceAlpha(NoteBase tr, float alpha)
    {
        SpriteRenderer value = BounceSpriteOf(tr);
        if (value != null)
        {
            Color color = value.color;
            color.a = alpha;
            value.color = color;
        }
        SpriteRenderer value2 = BounceSpriteExOf(tr);
        if (value2 != null)
        {
            Color color2 = value2.color;
            color2.a = alpha;
            value2.color = color2;
        }
    }

    private static void SetBounceGuideActive(NoteBase tr, bool active)
    {
        try
        {
            NoteGuide value = BounceGuideOf(tr);
            if (value != null)
            {
                value.gameObject.SetActive(active);
            }
        }
        catch
        {
        }
    }

    private static void SetBounceChildrenActive(NoteBase tr, bool active)
    {
        try
        {
            SpriteRenderer value = EffectSpriteOf(tr);
            if (value != null)
            {
                value.gameObject.SetActive(active);
            }
        }
        catch
        {
        }
    }
}
