using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Manager;
using MelonLoader;
using Monitor;
using UnityEngine;
using SinmaiAlpha.Notes.Libs;

namespace SinmaiAlpha.Notes;

// 自定义滑条路径、判定区和轨道长度接入。
public partial class CustomNoteTypes
{

    private static bool IsSlideType(NotesTypeID.Def type)
    {
        return type is NotesTypeID.Def.Slide or NotesTypeID.Def.BreakSlide or
            NotesTypeID.Def.ExSlide or NotesTypeID.Def.ExBreakSlide or NotesTypeID.Def.ConnectSlide;
    }

    private static void TryApplySpeedToNoteObject(object instance, NoteData note)
    {
        var component = instance as Component;
        if (component == null) return;

        // 去重：同一个对象同一 note 只处理一次。
        if (HandledInitializations.TryGetValue(component, out var handledIndex) && handledIndex == note.indexNote)
        {
            return;
        }

        HandledInitializations[component] = note.indexNote;

        NoteKinds.TryGetValue(note.indexNote, out var kind);

        if (kind != CustomNoteKind.None)
        {
            var isMine = kind is CustomNoteKind.Mine or CustomNoteKind.MineTouchBreak;
            var isTouchBreak = kind is CustomNoteKind.TouchBreak or CustomNoteKind.MineTouchBreak;

            var go = component.gameObject;
            // SlideRoot 内部实例化的星（StarNote/BreakStarNote 用 slide 的 NoteData 初始化）
            // 是 slide 的视觉星，不是独立地雷键：不贴地雷贴图、不挂 behaviour、判定保持原版。
            var isSlideStar = instance is StarNote or BreakStarNote && IsSlideType(note.type.getEnum());
            // ISelfJudgingMineNote 独立类贴图/判定自己负责，不需要 MineNoteBehaviour；
            // Hold/Slide 等非自反转类仍需 behaviour（transpiler 靠它识别地雷）。
            if (!isSlideStar && !(instance is ISelfJudgingMineNote))
            {
                var behaviour = go.GetComponent<MineNoteBehaviour>();
                if (behaviour == null) behaviour = go.AddComponent<MineNoteBehaviour>();
                // SlideRoot/SlideFan 的 ApplyMineVisual 会遍历子物体误伤内部星（_starNote），
                // 所以只挂判定标记不贴图；轨道贴图由独立地雷箭头池负责。
                var applyTextures = !(instance is MineSlideRoot or SlideFan);
                behaviour.Setup(note.indexNote, isMine, isTouchBreak, applyTextures);
            }
        }

        // 2026-08-23 SV/HS 精简：速度应用部分移除——内嵌 x{m} 倍率由 GetNoteSpeed/
        // GetTouchSpeed postfix 统一缩放 DefaultMsec；本函数只负责地雷 behaviour 挂载。
    }

    [HarmonyPatch]
    public static class SlideNoteDataHack

    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return
            [
                AccessTools.Method(typeof(SlideRoot), "Initialize"),
                AccessTools.Method(typeof(SlideFan), "Initialize"),
                // Temporarily disabled: the IL injection here can crash on built-in slide notes.
                // （待办：排查崩溃原因后恢复，见同目录 TODO.md §2.5）
                // AccessTools.Method(typeof(SlideRoot), "GetSlideArrowNum", [typeof(NoteData)]),
                AccessTools.Method(typeof(StarNote), "Initialize"),
                AccessTools.Method(typeof(BreakStarNote), "Initialize"),
            ];
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var methodGetSlidePathRedirect = AccessTools.Method(typeof(CustomNoteTypes), "GetSlidePathRedirect");
            var methodGetSlideHitAreaRedirect = AccessTools.Method(typeof(CustomNoteTypes), "GetSlideHitAreaRedirect");
            var methodGetSlideLengthRedirect = AccessTools.Method(typeof(CustomNoteTypes), "GetSlideLengthRedirect");

            var newInstList = new List<CodeInstruction>();
            var injected = 0;
            void Redirect(CodeInstruction original, MethodInfo method)
            {
                var note = FindSlideNoteLoad(newInstList);
                note.labels.AddRange(original.labels);
                var call = new CodeInstruction(OpCodes.Call, method);
                call.blocks.AddRange(original.blocks);
                newInstList.Add(note);
                newInstList.Add(call);
                injected++;
            }
            foreach (var inst in instructions)
            {
                if (IsSlideManagerCall(inst, "GetSlidePath"))
                {
                    // Append the NoteData used by this lookup. Star heads and connected
                    // slides read a child local, while root/fan lookups read arg_1.
                    // Always passing arg_1 loses custom geometry on those child lookups.
                    Redirect(inst, methodGetSlidePathRedirect);
                }
                else if (IsSlideManagerCall(inst, "GetSlideHitArea"))
                {
                    // instance + four arguments + the source NoteData
                    Redirect(inst, methodGetSlideHitAreaRedirect);
                }
                else if (IsSlideManagerCall(inst, "GetSlideLength"))
                {
                    // GetSlideLength(SlideType, int, int) 栈 4 值 + noteData = 5 = redirect 参数数
                    Redirect(inst, methodGetSlideLengthRedirect);
                }
                else
                {
                    newInstList.Add(inst);
                }
            }
            // 2026-08-25 修复：原实现用 inst.Calls(MethodInfo)——object.Equals 按引用比较
            // operand 与 AccessTools.Method 结果（反汇编 MethodInfo 实例与 GetMethod 实例
            // 不是同一对象）→ 永远不命中 → 自定义 slide（NMSSS/BRSSS）的路径/判定区/长度
            // redirect 从未注入，一直静默退化到原生模板近似形状。必须按名字+声明类型匹配。
            if (injected == 0)
            {
                MelonLogger.Warning("[CustomNoteType] SlideNoteDataHack: no SlideManager calls matched; custom slide redirects NOT injected");
            }
            else
            {
                MelonLogger.Msg($"[CustomNoteType] SlideNoteDataHack: {injected} SlideManager calls redirected");
            }
            return newInstList;
        }

        private static bool IsSlideManagerCall(CodeInstruction inst, string methodName)
        {
            if (inst.opcode != OpCodes.Call && inst.opcode != OpCodes.Callvirt) return false;
            return IsMember(inst.operand, "SlideManager", methodName);
        }

        private static CodeInstruction FindSlideNoteLoad(List<CodeInstruction> instructions)
        {
            // Every lookup reads its start argument from NoteData.startButtonPos,
            // including fans whose slide type and end position are computed separately.
            for (var i = instructions.Count - 1; i > 0; i--)
            {
                var field = instructions[i];
                if (field.opcode != OpCodes.Ldfld ||
                    !IsMember(field.operand, nameof(NoteData), nameof(NoteData.startButtonPos))) continue;
                var source = instructions[i - 1];
                if (!source.IsLdarg() && !source.IsLdloc()) break;
                // Replay only the load, without duplicating its labels or exception blocks.
                return new CodeInstruction(source.opcode, source.operand);
            }
            throw new InvalidOperationException("[CustomNoteType] Cannot identify the NoteData for a SlideManager lookup");
        }

        private static bool IsMember(object operand, string declaringType, string memberName)
        {
            // Harmony supplies reflection members or Cecil references, depending on runtime.
            if (operand is MemberInfo member)
                return member.Name == memberName && member.DeclaringType?.Name == declaringType;
            if (operand != null)
            {
                var name = operand.GetType().GetProperty("Name")?.GetValue(operand) as string;
                if (name != memberName) return false;
                var decl = operand.GetType().GetProperty("DeclaringType")?.GetValue(operand);
                return decl?.GetType().GetProperty("Name")?.GetValue(decl) as string == declaringType;
            }
            return false;
        }
    }


    public static List<Vector4> GetSlidePathRedirect(SlideManager instance, SlideType slideType, int start, int end,
        int starButton, NoteData noteData)
    {
        if (noteData is CustomSlideNoteData data)
        {
            if (data.IsFan) return data.FanPathLists[starButton][data.FanLane(end)];
            return data.SlidePathList[starButton];
        }
        return instance.GetSlidePath(slideType, start, end, starButton);
    }

    /// <summary>
    /// GameCtrl.RegistNote 在 SlideRoot.Initialize 之前调用 GetSlideArrowNum(NoteData) 计算箭头排序号。
    /// 该方法内部的 GetSlidePath 调用没有被 SlideNoteDataHack 重定向（注入曾致内置 slide 崩溃被禁用），
    /// 自定义路径的 slideData.type（GetEndType 结果 1/2/3）× start/end 组合在原生查表里越界 → 进曲崩溃。
    /// 对 CustomSlideNoteData 直接按自定义路径复刻原版箭头数算法短路。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SlideRoot), "GetSlideArrowNum", new[] { typeof(NoteData) })]
    public static bool SlideArrowNumCustomPrefix(SlideRoot __instance, NoteData note, ref int __result)
    {
        if (!(CustomNoteTypes.FeaturesEnabled(__instance))) {  return true; }

        if (note is not CustomSlideNoteData data) return true;
        var path = data.SlidePathList[__instance.ButtonId];
        if (path == null || path.Count < 3)
        {
            __result = 0;
            return false;
        }
        // 复刻原版：倒数第二点 z + 23.556 &lt; 末点 z → Count-2，否则 Count-3
        __result = path[path.Count - 2].z + 23.556f < path[path.Count - 1].z
            ? path.Count - 2
            : path.Count - 3;
        return false;
    }

    /// <summary>
    /// UpdateAlpha 的循环边界是 _dispLaneNum（Initialize 里按路径点算），
    /// 但 _spriteRenders/_breakSpriteRenders 由 GameCtrl.RegistNote 按 GetSlideArrowNum 分配。
    /// 两者理论上一致；若不一致（分配不足/池占用），UpdateAlpha 越界崩溃。
    /// 这里做 clamp 兜底 + 打印一次差异日志定位根因。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SlideRoot), "UpdateAlpha")]
    public static void UpdateAlphaSafePrefix(SlideRoot __instance)
    {
        if (!FeaturesEnabled(__instance))
        {
            return;
        }
        // Same reads and the same conditional write through typed refs; the reads have no side effects, and
        // the list that is dereferenced (and could throw) is chosen by BreakFlag exactly as before.
        AccessTools.FieldRef<SlideRoot, NoteData> note = SlideRefs.Note;
        AccessTools.FieldRef<SlideRoot, int> dispLaneNum = SlideRefs.DispLaneNum;
        AccessTools.FieldRef<SlideRoot, List<SpriteRenderer>> sprites = SlideRefs.Sprites;
        AccessTools.FieldRef<SlideRoot, List<BreakSlide>> breakSprites = SlideRefs.BreakSprites;
        AccessTools.FieldRef<SlideRoot, bool> breakFlag = SlideRefs.BreakFlag;
        if (note != null && dispLaneNum != null && sprites != null && breakSprites != null && breakFlag != null)
        {
            if (note(__instance) != null)
            {
                int count = (breakFlag(__instance) ? breakSprites(__instance).Count : sprites(__instance).Count);
                ref int reference = ref dispLaneNum(__instance);
                if (reference > count)
                {
                    reference = count;
                }
            }
            return;
        }
        Traverse traverse = Traverse.Create(__instance);
        if (traverse.Field("NoteData").GetValue<NoteData>() != null)
        {
            int value = traverse.Field("_dispLaneNum").GetValue<int>();
            List<SpriteRenderer> list = (List<SpriteRenderer>)traverse.Field("_spriteRenders").GetValue();
            List<BreakSlide> list2 = (List<BreakSlide>)traverse.Field("_breakSpriteRenders").GetValue();
            int num = (traverse.Field("BreakFlag").GetValue<bool>() ? list2.Count : list.Count);
            if (value > num)
            {
                traverse.Field("_dispLaneNum").SetValue(num);
            }
        }
    }

    public static List<SlideManager.HitArea> GetSlideHitAreaRedirect(SlideManager instance, SlideType slideType,
        int start, int end, int starButton, NoteData noteData)
    {
        if (noteData is CustomSlideNoteData data)
        {
            if (data.IsFan) return data.FanHitAreaLists[starButton][data.FanLane(end)];
            return data.SlideHitAreasList[starButton];
        }
        return instance.GetSlideHitArea(slideType, start, end, starButton);
    }

    public static float GetSlideLengthRedirect(SlideManager instance, SlideType slideType,
        int start, int end, NoteData noteData)
    {
        if (noteData is CustomSlideNoteData data)
        {
            return data.SlidePathLength;
        }
        try
        {
            // 预检：原生 _slidePathList 查表要求 slideType∈[0,13] 且 start/end∈[0,7]。
            // 越界直接走日志+兜底，避免 AOOE 在 catch 里被二次 NRE 掩蔽。
            if ((int)slideType < 0 || (int)slideType > 13 || start < 0 || start > 7 || end < 0 || end > 7)
            {
                LogGetSlideLengthOOB(slideType, start, end, noteData, "range");
                return 1f;
            }
            return instance.GetSlideLength(slideType, start, end);
        }
        catch (Exception ex)
        {
            // NotesTypeID 是 class 且重载了 ==（op_Equality 会解引用两个操作数），
            // 判空必须用 ReferenceEquals，否则 catch 里 nt == null 二次 NRE，
            // 把真实的 AOOE 信息掩蔽成表面上的 NullReferenceException。
            LogGetSlideLengthOOB(slideType, start, end, noteData, ex.GetType().Name + ":" + ex.Message);
            return 1f;
        }
    }

    private static void LogGetSlideLengthOOB(SlideType slideType, int start, int end, NoteData noteData, string why)
    {
        try
        {
            var nt = ReferenceEquals(noteData, null) ? null : noteData.type;
            var noteDesc = ReferenceEquals(nt, null)
                ? "null"
                : $"{nt.getEnum()} isTouch={nt.isTouch()} isStar={nt.isStar()} isSlide={nt.isSlide()}";
            var extra = "";
            try
            {
                if (!ReferenceEquals(noteData, null) && noteData.slideData != null)
                {
                    extra = $" slideType={noteData.slideData.type} targetNote={noteData.slideData.targetNote}";
                }
            }
            catch (Exception) { }
            MelonLogger.Error(
                $"[CustomNoteType] GetSlideLengthRedirect OOB({why}): slideType={(int)slideType}({slideType}) " +
                $"start={start} end={end} noteType={noteDesc} " +
                $"index={noteData?.index} indexNote={noteData?.indexNote} indexSlide={noteData?.indexSlide} " +
                $"startButtonPos={noteData?.startButtonPos}{extra}");
        }
        catch (Exception logEx)
        {
            MelonLogger.Error($"[CustomNoteType] GetSlideLengthRedirect LOGGING FAILED: {logEx}");
        }
    }
}
