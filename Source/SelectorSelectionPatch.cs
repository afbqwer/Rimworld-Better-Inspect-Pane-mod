using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 选择行为补丁（对应设置「效果与杂项」页的杂项区块）：
///   1) 修改选择数量上限：把 Selector.SelectInternal 里硬编码的 200 改为可调值；
///   2) 改进的原版 Shift 选择：框选优先级降级 + 类型不同则忽略选择。
/// 两个设置默认关闭，关闭时全部走原版行为。
/// </summary>
public static class SelectorSelectionPatch
{
    // ==================== 选择数量上限 ====================

    /// <summary>
    /// 把 Selector.SelectInternal 里「selected.Count &lt; 200」的常量 200 替换为对
    /// MyModTemplateSettings.CurrentSelectionLimit 的调用（运行时按设置返回滑条值或 200）。
    /// 编译期常量会被内联进方法体，只能通过 Transpiler 修改（IL 中 ldc.i4 200 仅出现一次）。
    /// 带错误处理：若未找到常量 200，设置开启时记 Log.Error（修改未生效），设置关闭时记 Log.Message（信息）。
    /// </summary>
    [HarmonyPatch(typeof(Selector), "SelectInternal")]
    public static class SelectionLimitPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo limitGetter = AccessTools.PropertyGetter(
                typeof(MyModTemplateSettings), nameof(MyModTemplateSettings.CurrentSelectionLimit));
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            bool replaced = false;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!replaced && codes[i].opcode == OpCodes.Ldc_I4 && codes[i].operand is int n && n == 200)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, limitGetter);
                    replaced = true;
                }
            }
            if (!replaced)
            {
                string msg = "ASQBetterInspectPane: 未在 " + __originalMethod.FullDescription()
                    + " 中找到常量 200，选择数量上限修改未生效。";
                if (MyModTemplateSettings.enableSelectionLimitModify)
                {
                    Log.Error(msg);
                }
                else
                {
                    Log.Message(msg);
                }
            }
            return codes;
        }
    }

    // ==================== 改进 Shift 选择：类型不同则忽略 ====================

    /// <summary>
    /// 原版 SelectInternal 在切换 Zone/Plan 类型时会 ClearSelection 清空现有选择：
    /// 例如已选 Pawn 再按住 Shift 选 Zone，会先把 Pawn 全部清空。
    /// 开启改进后：按住 Shift 且现有选择中没有任何与本次同类别（Thing / Zone / Plan）的对象时，
    /// 直接忽略此次选择（返回 false 跳过原版），保留现有选择。
    /// </summary>
    [HarmonyPatch(typeof(Selector), "SelectInternal")]
    public static class ImprovedShiftTypeGuardPatch
    {
        static bool Prefix(Selector __instance, object obj)
        {
            if (!MyModTemplateSettings.enabled || !MyModTemplateSettings.enableImprovedShiftSelection)
            {
                return true;
            }
            if (!Selector.ShiftIsHeld || obj == null)
            {
                return true;
            }
            if (__instance.NumSelected == 0 || __instance.IsSelected(obj))
            {
                return true;
            }
            int cat = SelectCategory(obj);
            foreach (object o in __instance.SelectedObjects)
            {
                if (SelectCategory(o) == cat)
                {
                    return true; // 现有选择里已有同类别 → 放行原版。
                }
            }
            return false; // 类型不同 → 忽略此次选择，保留现有选择。
        }

        /// <summary>选择类别：0 = Thing，1 = Zone，2 = Plan（仅区分原版会互斥清空的类别）。</summary>
        private static int SelectCategory(object o) => o is Zone ? 1 : o is Plan ? 2 : 0;
    }

    // ==================== 改进 Shift 选择：框选优先级降级 ====================

    /// <summary>
    /// 重写 SelectInsideDragBox（仅设置开启时生效，关闭时返回 true 走原版）：
    /// 原版优先级链（殖民者→人类→资源→Pawn→可选物）只要当前级匹配到任何物体就结束，
    /// 即使这些物体都已选中（Shift 追加时等于什么都没做）。
    /// 改进后：按住 Shift 时，若当前优先级只匹配到已选中的物体（没有新增），
    /// 则降级尝试下一优先级；非 Shift 保持原版「清空后按第一命中优先级选择」。
    /// 其余（殖民者条、商队、区域、计划、兜底按单击处理）与原版完全一致。
    /// </summary>
    [HarmonyPatch(typeof(Selector), "SelectInsideDragBox")]
    public static class ImprovedShiftDragBoxPatch
    {
        // 原版 SelectUnderMouse 为 private，兜底「框内无可选对象按单击处理」时经反射调用。
        private static readonly MethodInfo SelectUnderMouseMethod =
            typeof(Selector).GetMethod("SelectUnderMouse", BindingFlags.Instance | BindingFlags.NonPublic);

        static bool Prefix(Selector __instance)
        {
            if (!MyModTemplateSettings.enabled || !MyModTemplateSettings.enableImprovedShiftSelection)
            {
                return true;
            }
            // 仅处理按住 Shift 的框选；非 Shift（含 Ctrl 框选）交给原版 / Ctrl 补丁。
            if (!Selector.ShiftIsHeld)
            {
                return true;
            }

            // Shift 模式不清空现有选择，直接在现有选择上追加。
            bool selectedSomething = false;

            // 优先级 1：殖民者条（与原版一致：命中即结束）。
            List<Thing> colonistBarThings = Find.ColonistBar.MapColonistsOrCorpsesInScreenRect(__instance.dragBox.ScreenRect);
            foreach (Thing t in colonistBarThings)
            {
                selectedSomething = true;
                __instance.Select(t);
            }
            if (selectedSomething)
            {
                return false;
            }

            // 优先级 2：商队（与原版一致）。
            List<Caravan> caravans = Find.ColonistBar.CaravanMembersCaravansInScreenRect(__instance.dragBox.ScreenRect);
            foreach (Caravan c in caravans)
            {
                if (!selectedSomething)
                {
                    CameraJumper.TryJumpAndSelect(c);
                    selectedSomething = true;
                }
                else
                {
                    Find.WorldSelector.Select(c);
                }
            }
            if (selectedSomething)
            {
                return false;
            }

            // 优先级 3：地图物体优先级链。Shift 模式：当前级无新增则继续下一级。
            List<Thing> boxThings = ThingSelectionUtility.MultiSelectableThingsInScreenRectDistinct(__instance.dragBox.ScreenRect).ToList();
            if (SelectWhere(IsColonist, SelectorUtility.SortInColonistBarOrder)
                || SelectWhere(IsHumanlike, null)
                || SelectWhere(IsResource, null)
                || SelectWhere(IsPawn, null)
                || SelectWhere(t => t.def.selectable, null))
            {
                return false;
            }

            // 优先级 4/5：区域与计划（与原版一致）。
            foreach (Zone z in ThingSelectionUtility.MultiSelectableZonesInScreenRectDistinct(__instance.dragBox.ScreenRect).ToList())
            {
                selectedSomething = true;
                __instance.Select(z);
            }
            foreach (Plan p in ThingSelectionUtility.MultiSelectablePlansInScreenRectDistinct(__instance.dragBox.ScreenRect).ToList())
            {
                selectedSomething = true;
                __instance.Select(p);
            }

            // 兜底：框内无可选对象时按单击处理。
            if (!selectedSomething)
            {
                SelectUnderMouseMethod.Invoke(__instance, null);
            }
            return false;

            // ---- 局部函数（与原版 SelectInsideDragBox 内的判定一致）----

            bool IsColonist(Thing t)
            {
                if (t.def.category == ThingCategory.Pawn && (((Pawn)t).RaceProps.Humanlike || (ModsConfig.BiotechActive && ((Pawn)t).RaceProps.IsMechanoid)))
                {
                    return t.Faction == Faction.OfPlayer;
                }
                return false;
            }

            bool IsHumanlike(Thing t)
            {
                if (t.def.category == ThingCategory.Pawn)
                {
                    return ((Pawn)t).RaceProps.Humanlike;
                }
                return false;
            }

            bool IsPawn(Thing t) => t.def.category == ThingCategory.Pawn;

            bool IsResource(Thing t) => t.def.CountAsResource;

            /// <summary>当前优先级：匹配到物体即视为框内有该优先级物体；返回是否应结束优先级链。</summary>
            bool SelectWhere(Predicate<Thing> predicate, Action<List<Thing>>? postProcessor)
            {
                List<Thing> matches = boxThings.Where(t => predicate(t)).ToList();
                if (matches.Count == 0)
                {
                    return false;
                }
                selectedSomething = true; // 框内存在该优先级物体（兜底判定与原版一致）。
                postProcessor?.Invoke(matches);
                bool addedNew = false;
                foreach (Thing t in matches)
                {
                    if (!__instance.IsSelected(t))
                    {
                        addedNew = true;
                    }
                    __instance.Select(t);
                }
                // Shift 模式：仅当本优先级新增了物体才结束，否则降级尝试下一级。
                return addedNew;
            }
        }
    }

    // ==================== Ctrl 选择功能 ====================

    /// <summary>
    /// 按住 Ctrl 框选时的批量选择（仅设置开启时生效；未按 Ctrl 或按了 Shift 时返回 true 走原版/Shift 补丁）。
    /// 按当前选择状态分三类：
    ///   - 无选择：优先选中框内所有玩家 Pawn；没有则选所有玩家炮塔；再没有则选所有建筑；再没有则按原版方式。
    ///   - 单选 / 多选（以首个选中为准）：先增量选中框内所有「相似」项（Thing 用原版相似判断、
    ///     Zone 同类型、Plan 同颜色）；无新增则按基准类型增量选择（区域→区域 / Pawn→Pawn（同阵营）/
    ///     Building→Building（同阵营）/ Resource→Resource / 其它 Thing→非 Pawn 非 Resource 非 Building 可选物）；
    ///     仍无新增则保持现有选择不变。
    /// 全程增量追加，不清空现有选择。
    /// </summary>
    [HarmonyPatch(typeof(Selector), "SelectInsideDragBox")]
    public static class CtrlSelectionDragBoxPatch
    {
        private static bool CtrlIsHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        static bool Prefix(Selector __instance)
        {
            if (!MyModTemplateSettings.enabled || !MyModTemplateSettings.enableCtrlSelection)
            {
                return true;
            }
            if (!CtrlIsHeld || Selector.ShiftIsHeld)
            {
                return true; // 未按 Ctrl 或按了 Shift → 交给原版 / Shift 补丁。
            }

            List<Thing> boxThings = ThingSelectionUtility.MultiSelectableThingsInScreenRectDistinct(__instance.dragBox.ScreenRect).ToList();
            if (__instance.NumSelected == 0)
            {
                return SelectNothingSelected(__instance, boxThings);
            }
            return SelectWithBase(__instance, __instance.SelectedObjects[0], boxThings);
        }

        /// <summary>无选择状态：优先玩家 Pawn，其次玩家炮塔，再其次所有建筑，再否则返回 true 走原版。</summary>
        private static bool SelectNothingSelected(Selector __instance, List<Thing> boxThings)
        {
            List<Thing> playerPawns = boxThings.Where(t => t is Pawn p && p.Faction == Faction.OfPlayer).ToList();
            if (playerPawns.Count > 0)
            {
                foreach (Thing t in playerPawns)
                {
                    __instance.Select(t);
                }
                return false;
            }
            List<Thing> playerTurrets = boxThings.Where(t => t is Building_Turret && t.Faction == Faction.OfPlayer).ToList();
            if (playerTurrets.Count > 0)
            {
                foreach (Thing t in playerTurrets)
                {
                    __instance.Select(t);
                }
                return false;
            }
            List<Thing> buildings = boxThings.Where(t => t.def.category == ThingCategory.Building).ToList();
            if (buildings.Count > 0)
            {
                foreach (Thing t in buildings)
                {
                    __instance.Select(t);
                }
                return false;
            }
            return true; // 按原版方式。
        }

        /// <summary>单选/多选状态（以首个选中为准）：先相似，再按类型，再否则保持现有选择不变（跳过原版）。</summary>
        private static bool SelectWithBase(Selector __instance, object baseObj, List<Thing> boxThings)
        {
            if (TrySelectSimilar(__instance, baseObj, boxThings))
            {
                return false;
            }
            if (TrySelectByType(__instance, baseObj, boxThings))
            {
                return false;
            }
            return false; // 找不到相似/同类：保持现有选择不变（不清空），跳过原版。
        }

        /// <summary>增量选择框内所有「相似」项。返回是否新增了选中物体。</summary>
        private static bool TrySelectSimilar(Selector __instance, object baseObj, List<Thing> boxThings)
        {
            bool anyNew = false;
            if (baseObj is Thing baseThing)
            {
                Thing baseInner = baseThing.GetInnerIfMinified();
                foreach (Thing t in boxThings)
                {
                    if (IsSimilarThing(t, baseInner))
                    {
                        if (!__instance.IsSelected(t))
                        {
                            anyNew = true;
                        }
                        __instance.Select(t);
                    }
                }
                return anyNew;
            }
            if (baseObj is Zone baseZone)
            {
                foreach (Zone z in ThingSelectionUtility.MultiSelectableZonesInScreenRectDistinct(__instance.dragBox.ScreenRect).ToList())
                {
                    if (z.GetType() == baseZone.GetType())
                    {
                        if (!__instance.IsSelected(z))
                        {
                            anyNew = true;
                        }
                        __instance.Select(z);
                    }
                }
                return anyNew;
            }
            if (baseObj is Plan basePlan)
            {
                foreach (Plan p in ThingSelectionUtility.MultiSelectablePlansInScreenRectDistinct(__instance.dragBox.ScreenRect).ToList())
                {
                    if (p.Color == basePlan.Color)
                    {
                        if (!__instance.IsSelected(p))
                        {
                            anyNew = true;
                        }
                        __instance.Select(p);
                    }
                }
                return anyNew;
            }
            return anyNew;
        }

        /// <summary>按基准对象类型增量选择。返回是否新增了选中物体。</summary>
        private static bool TrySelectByType(Selector __instance, object baseObj, List<Thing> boxThings)
        {
            bool anyNew = false;
            if (baseObj is Zone)
            {
                foreach (Zone z in ThingSelectionUtility.MultiSelectableZonesInScreenRectDistinct(__instance.dragBox.ScreenRect).ToList())
                {
                    if (!__instance.IsSelected(z))
                    {
                        anyNew = true;
                    }
                    __instance.Select(z);
                }
                return anyNew;
            }
            if (baseObj is Pawn basePawn)
            {
                foreach (Thing t in boxThings)
                {
                    if (t.def.category == ThingCategory.Pawn && t.Faction == basePawn.Faction)
                    {
                        if (!__instance.IsSelected(t))
                        {
                            anyNew = true;
                        }
                        __instance.Select(t);
                    }
                }
                return anyNew;
            }
            if (baseObj is Thing baseBuilding && baseBuilding.def.category == ThingCategory.Building)
            {
                foreach (Thing t in boxThings)
                {
                    if (t.def.category == ThingCategory.Building && t.Faction == baseBuilding.Faction)
                    {
                        if (!__instance.IsSelected(t))
                        {
                            anyNew = true;
                        }
                        __instance.Select(t);
                    }
                }
                return anyNew;
            }
            if (baseObj is Thing baseThing && baseThing.def.CountAsResource)
            {
                foreach (Thing t in boxThings)
                {
                    if (t.def.CountAsResource)
                    {
                        if (!__instance.IsSelected(t))
                        {
                            anyNew = true;
                        }
                        __instance.Select(t);
                    }
                }
                return anyNew;
            }
            if (baseObj is Thing)
            {
                // 既不是 Pawn 也不是 Resource 也不是 Building → 增量选择其余可选物。
                foreach (Thing t in boxThings)
                {
                    if (t.def.category != ThingCategory.Pawn && !t.def.CountAsResource && t.def.category != ThingCategory.Building)
                    {
                        if (!__instance.IsSelected(t))
                        {
                            anyNew = true;
                        }
                        __instance.Select(t);
                    }
                }
                return anyNew;
            }
            return anyNew;
        }

        /// <summary>原版相似判断（与 SelectAllMatchingObjectUnderMouseOnScreen 的 Validator 一致）：
        /// 同阵营 +（同种 Pawn：宿主阵营 / 突变 / 等价种族）或（同 def）。</summary>
        private static bool IsSimilarThing(Thing t, Thing baseInner)
        {
            Thing inner = t.GetInnerIfMinified();
            if (inner.Faction != baseInner.Faction)
            {
                return false;
            }
            if (baseInner is Pawn pawn && inner is Pawn pawn2)
            {
                if (pawn2.HostFaction != pawn.HostFaction)
                {
                    return false;
                }
                if (pawn2.mutant?.Def != pawn.mutant?.Def)
                {
                    return false;
                }
                return SelectorUtility.IsEquivalentRace(pawn2, pawn);
            }
            return inner.def == baseInner.def;
        }
    }
}
