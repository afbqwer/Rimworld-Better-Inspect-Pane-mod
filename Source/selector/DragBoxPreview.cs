using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 框选预览（多选预览）：拖拽原版选择框（地图 / 世界地图）时，在松开鼠标之前就把每一项的最终归属标出来；
/// 顶部殖民者条上被框到的头像同样会标出。
/// <para>
/// 三色语义：
/// <list type="bullet">
/// <item><b>青色</b> = 将要增加：当前未选中，松手后会被选中。</item>
/// <item><b>橙色</b> = 将被排除：当前已选中，松手后会失去选择（仅在会先清空选择的框选里出现，即非 Shift 的普通框选）。</item>
/// <item><b>白色</b>（原版括号）= 不变：已选中且属于本次框选结果，或已选中且本次框选不会影响它。</item>
/// </list>
/// </para>
/// <para>
/// 实现要点：
/// <list type="bullet">
/// <item>预测每帧只算一次（以帧号缓存）：世界空间绘制在 Update 阶段（<c>SelectionDrawer.DrawSelectionOverlays</c> 后置）取用，
/// 数量标签在 OnGUI 阶段（<c>DragBox.DragBoxOnGUI</c> 后置）复用同一帧结果，避免重复遍历框内单元格。</item>
/// <item>预测严格镜像实际选中逻辑：原版 <c>Selector.SelectInsideDragBox</c> 的优先级链，
/// 以及本模组 <see cref="SelectorSelectionPatch"/> 的「改进 Shift」「Ctrl 选择」两条补丁路径。</item>
/// <item>橙色需要盖住原版白色括号：预览材质 / 区域边框的渲染队列都取原版对应材质的队列 +1/+30，
/// 保证稳定画在白括号之上；青色只画在未选中对象上，不与白色重叠。</item>
/// <item>殖民者条头像在原版 <c>ColonistBarColonistDrawer.DrawColonist</c> 之后叠加同款括号，命中直接复用本帧预测集合，
/// 因此头像上的颜色与地图上该殖民者的颜色始终一致（含选择数量上限、Shift / Ctrl 等全部规则）。</item>
/// </list>
/// </para>
/// </summary>
public static class DragBoxPreview
{
    #region 外观

    /// <summary>「将要增加」颜色（浅青色）。</summary>
    public static readonly Color AddColor = new Color(0.35f, 0.92f, 1f);

    /// <summary>「将被排除」颜色（橙色）。</summary>
    public static readonly Color ExcludeColor = new Color(1f, 0.64f, 0.2f);

    // 世界地图对象的括号参数（与原版 WorldSelectionDrawer.DrawSelectionBracketOnGUIFor 一致）。
    private const float WorldBracketHalfSize = 17.5f;
    private const float WorldBracketTexScale = 0.4f;

    // 区域 / 计划预览边框的渲染队列：原版用 2900，这里 +30 保证预览边框画在原版白色边框之上。
    private const int PreviewFieldEdgeRenderQueue = 2930;

    private const string BracketTexPath = "UI/Overlays/SelectionBracket";

    private static readonly Vector2[] worldBracketLocs = new Vector2[4];

    private static Material? addBracketMat;
    private static Material? excludeBracketMat;

    /// <summary>「将要增加」括号材质（浅青色）。</summary>
    private static Material AddBracketMat => GetBracketMat(ref addBracketMat, AddColor);

    /// <summary>「将被排除」括号材质（橙色），需要盖住原版白色括号。</summary>
    private static Material ExcludeBracketMat => GetBracketMat(ref excludeBracketMat, ExcludeColor);

    /// <summary>
    /// 括号材质：沿用原版选择括号纹理 / 着色器，按指定颜色着色。
    /// 渲染队列取「原版括号材质队列 + 1」，这样画在已选中对象上时能稳定覆盖原版白色括号。
    /// </summary>
    private static Material GetBracketMat(ref Material? cache, Color color)
    {
        if (cache == null)
        {
            Material baseMat = MaterialPool.MatFrom(BracketTexPath, ShaderDatabase.MetaOverlay);
            cache = MaterialPool.MatFrom(BracketTexPath, ShaderDatabase.MetaOverlay, color, baseMat.renderQueue + 1);
        }
        return cache;
    }

    /// <summary>功能是否生效：模组总开关 + 框选预览开关。</summary>
    private static bool Active => MyModTemplateSettings.enabled && MyModTemplateSettings.enableDragBoxPreview;

    #endregion

    #region 每帧预测缓存

    // 地图 / 世界地图各自一份缓存（两条绘制 / 标签链互不影响）。
    private static int mapFrame = -1;
    private static int worldFrame = -1;
    private static readonly List<object> mapPredicted = new List<object>();
    private static readonly List<WorldObject> worldPredicted = new List<WorldObject>();
    // 本帧「将被排除」的对象（已选中、但松手后会失去选择）；只在会先清空选择的框选里非空。
    private static readonly List<object> mapExcluded = new List<object>();
    private static readonly List<WorldObject> worldExcluded = new List<WorldObject>();
    private static readonly HashSet<object> tmpPredictedSet = new HashSet<object>();
    private static readonly HashSet<WorldObject> tmpWorldPredictedSet = new HashSet<WorldObject>();
    // 本帧将被选中的总数（含已选中且仍在框内的）与其中的新增数量。
    private static int mapTotalCount;
    private static int mapNewCount;
    private static int worldTotalCount;
    private static int worldNewCount;

    // 预测期间的复用缓冲，避免每帧分配。
    private static readonly List<Thing> tmpBoxThings = new List<Thing>();

    /// <summary>确保本帧的地图框选预测已计算（同帧内被绘制与数量标签共用）。</summary>
    private static void EnsureMapPrediction(Selector? sel)
    {
        int frame = Time.frameCount;
        if (mapFrame == frame)
        {
            return;
        }
        mapFrame = frame;
        mapPredicted.Clear();
        mapExcluded.Clear();
        mapTotalCount = 0;
        mapNewCount = 0;
        if (!Active || sel == null || Find.CurrentMap == null)
        {
            return;
        }
        DragBox box = sel.dragBox;
        if (box == null || !box.IsValidAndActive)
        {
            return;
        }
        bool clearsSelection;
        try
        {
            clearsSelection = PredictMap(sel, box.ScreenRect, mapPredicted);
        }
        catch (Exception e)
        {
            mapPredicted.Clear();
            Log.ErrorOnce("ASQBetterInspectPane: 框选预览预测异常：" + e, 0x5B1D0C1);
            return;
        }
        // 原版非 Shift 框选会先清空选择，此时已选中的对象同样占用数量额度。
        if (clearsSelection)
        {
            TrimToLimit(mapPredicted, 0, MyModTemplateSettings.CurrentSelectionLimit, static _ => false);
        }
        else
        {
            TrimToLimit(mapPredicted, sel.NumSelected, MyModTemplateSettings.CurrentSelectionLimit, sel.IsSelected);
        }
        mapTotalCount = mapPredicted.Count;
        mapNewCount = CountNotSelected(mapPredicted, sel.IsSelected);
        // 先清空选择时，不在预测结果里的已选中对象松手后都会失去选择 —— 这些就是「将被排除」。
        if (clearsSelection)
        {
            CollectExcluded(sel.SelectedObjects, mapPredicted, tmpPredictedSet, mapExcluded);
        }
    }

    /// <summary>确保本帧的世界地图框选预测已计算。</summary>
    private static void EnsureWorldPrediction(WorldSelector? sel)
    {
        int frame = Time.frameCount;
        if (worldFrame == frame)
        {
            return;
        }
        worldFrame = frame;
        worldPredicted.Clear();
        worldExcluded.Clear();
        worldTotalCount = 0;
        worldNewCount = 0;
        if (!Active || sel == null)
        {
            return;
        }
        WorldDragBox box = sel.dragBox;
        if (box == null || !box.IsValidAndActive)
        {
            return;
        }
        try
        {
            PredictWorld(sel, box.ScreenRect, worldPredicted);
        }
        catch (Exception e)
        {
            worldPredicted.Clear();
            Log.ErrorOnce("ASQBetterInspectPane: 世界地图框选预览预测异常：" + e, 0x5B1D0C2);
            return;
        }
        // 世界地图框选同样在非 Shift 时先清空选择（清空后上限全部可用，已选对象要被排除）。
        bool clearsSelection = !Selector.ShiftIsHeld;
        if (clearsSelection)
        {
            TrimToLimit(worldPredicted, 0, 80, static _ => false);
        }
        else
        {
            TrimToLimit(worldPredicted, sel.NumSelectedObjects, 80, sel.IsSelected);
        }
        worldTotalCount = worldPredicted.Count;
        worldNewCount = CountNotSelected(worldPredicted, sel.IsSelected);
        if (clearsSelection)
        {
            CollectExcluded(sel.SelectedObjects, worldPredicted, tmpWorldPredictedSet, worldExcluded);
        }
    }

    /// <summary>按选择数量上限裁剪预测结果（isSelected 为 true 的对象已选中，不占额度）。</summary>
    private static void TrimToLimit<T>(List<T> list, int numSelected, int limit, Predicate<T> isSelected)
    {
        int added = 0;
        int free = Math.Max(0, limit - numSelected);
        int write = 0;
        for (int i = 0; i < list.Count; i++)
        {
            T item = list[i];
            if (isSelected(item))
            {
                list[write++] = item;
                continue;
            }
            if (added >= free)
            {
                continue; // 超出上限：松手后原版也会跳过这些对象。
            }
            added++;
            list[write++] = item;
        }
        if (write < list.Count)
        {
            list.RemoveRange(write, list.Count - write);
        }
    }

    /// <summary>统计结果中「当前未选中」的数量 —— 即青色（将要增加）的数量。</summary>
    private static int CountNotSelected<T>(List<T> list, Predicate<T> isSelected)
    {
        int count = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (!isSelected(list[i]))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>收集「已选中但不在预测结果里」的对象 —— 先清空选择的框选里，松手后它们会失去选择（橙色）。</summary>
    private static void CollectExcluded<T>(List<T> selected, List<T> predicted, HashSet<T> predictedSet, List<T> excluded) where T : class
    {
        predictedSet.Clear();
        for (int i = 0; i < predicted.Count; i++)
        {
            predictedSet.Add(predicted[i]);
        }
        for (int i = 0; i < selected.Count; i++)
        {
            T obj = selected[i];
            if (obj != null && !predictedSet.Contains(obj))
            {
                excluded.Add(obj);
            }
        }
    }

    #endregion

    #region 地图预测

    /// <summary>地图框选预测。返回「松手后原版会先清空现有选择」时为 true（数量额度按 0 个已选计算）。</summary>
    private static bool PredictMap(Selector sel, Rect rect, List<object> outList)
    {
        // 分派顺序与实际补丁一致：
        //   Shift：改进 Shift 补丁（开启时）→ 原版；
        //   非 Shift + Ctrl：Ctrl 补丁（开启时）→ 原版。
        if (Selector.ShiftIsHeld)
        {
            if (MyModTemplateSettings.enableImprovedShiftSelection)
            {
                PredictImprovedShift(sel, rect, outList);
                // 改进 Shift 的 SelectInternal 前缀在「新增对象与现有选择类别不同」时忽略整次选择：
                // 此时实际什么都不会被选中，预览也应清空。
                if (sel.NumSelected > 0 && outList.Count > 0 && SelectCategory(outList[0]) != CurrentSelectionCategory(sel))
                {
                    outList.Clear();
                }
                return false;
            }
            PredictVanilla(rect, outList);
            // 原版 Shift 追加不同类别时，第一条 Select 会清空现有选择（被清空的对象画橙色）。
            return sel.NumSelected > 0 && outList.Count > 0 && SelectCategory(outList[0]) != CurrentSelectionCategory(sel);
        }
        if (CtrlIsHeld && MyModTemplateSettings.enableCtrlSelection)
        {
            PredictCtrlSelection(sel, rect, outList);
            return false;
        }
        PredictVanilla(rect, outList);
        return true;
    }

    /// <summary>当前选择的类别（0 = Thing，1 = Zone，2 = Plan；无选择返回 -1）。
    /// 与原版 SelectInternal 一致：类别不同会先清空现有选择。</summary>
    private static int CurrentSelectionCategory(Selector sel)
    {
        if (sel.NumSelected == 0)
        {
            return -1;
        }
        return SelectCategory(sel.SelectedObjects[0]);
    }

    private static int SelectCategory(object o) => o is Zone ? 1 : o is Plan ? 2 : 0;

    /// <summary>原版 <c>SelectInsideDragBox</c>：殖民者条 → 商队 → 优先级链（首个命中即结束）→ 区域 → 计划。</summary>
    private static void PredictVanilla(Rect rect, List<object> outList)
    {
        List<Thing> colonistBarThings = Find.ColonistBar.MapColonistsOrCorpsesInScreenRect(rect);
        if (colonistBarThings.Count > 0)
        {
            outList.AddRange(colonistBarThings);
            return;
        }
        // 框住殖民者条上的商队图标时，原版选中的是世界地图上的商队，地图上无对象可预览。
        if (Find.ColonistBar.CaravanMembersCaravansInScreenRect(rect).Count > 0)
        {
            return;
        }

        List<Thing> boxThings = GetBoxThings(rect);
        List<Thing>? level = PickPriorityLevel(boxThings, null, requireNew: false);
        if (level != null)
        {
            outList.AddRange(level);
            return;
        }
        // 优先级链完全没命中时，区域与计划都会被选中。
        outList.AddRange(ThingSelectionUtility.MultiSelectableZonesInScreenRectDistinct(rect));
        outList.AddRange(ThingSelectionUtility.MultiSelectablePlansInScreenRectDistinct(rect));
    }

    /// <summary>
    /// 改进 Shift 补丁（<see cref="SelectorSelectionPatch.ImprovedShiftDragBoxPatch"/>）：
    /// 优先级链中当前级只命中已选中对象时降级到下一级；全部没有新增时才落到区域 / 计划。
    /// </summary>
    private static void PredictImprovedShift(Selector sel, Rect rect, List<object> outList)
    {
        List<Thing> colonistBarThings = Find.ColonistBar.MapColonistsOrCorpsesInScreenRect(rect);
        if (colonistBarThings.Count > 0)
        {
            outList.AddRange(colonistBarThings);
            return;
        }
        if (Find.ColonistBar.CaravanMembersCaravansInScreenRect(rect).Count > 0)
        {
            return;
        }

        List<Thing> boxThings = GetBoxThings(rect);
        List<Thing>? level = PickPriorityLevel(boxThings, sel, requireNew: true);
        if (level != null)
        {
            outList.AddRange(level);
            return;
        }
        outList.AddRange(ThingSelectionUtility.MultiSelectableZonesInScreenRectDistinct(rect));
        outList.AddRange(ThingSelectionUtility.MultiSelectablePlansInScreenRectDistinct(rect));
    }

    /// <summary>
    /// Ctrl 补丁（<see cref="SelectorSelectionPatch.CtrlSelectionDragBoxPatch"/>）：
    /// 未选中时按 玩家 Pawn → 玩家炮塔 → 建筑 → 原版；已选中时先「相似」再「同类型」，都没有新增则保持现有选择。
    /// </summary>
    private static void PredictCtrlSelection(Selector sel, Rect rect, List<object> outList)
    {
        List<Thing> boxThings = GetBoxThings(rect);
        if (sel.NumSelected == 0)
        {
            List<Thing> picked = Matches(boxThings, static t => t is Pawn p && p.Faction == Faction.OfPlayer);
            if (picked.Count == 0)
            {
                picked = Matches(boxThings, static t => t is Building_Turret && t.Faction == Faction.OfPlayer);
            }
            if (picked.Count == 0)
            {
                picked = Matches(boxThings, static t => t.def.category == ThingCategory.Building);
            }
            if (picked.Count > 0)
            {
                outList.AddRange(picked);
                return;
            }
            PredictVanilla(rect, outList);
            return;
        }

        object baseObj = sel.SelectedObjects[0];
        if (TryPredictSimilar(sel, baseObj, rect, boxThings, outList))
        {
            return;
        }
        TryPredictByType(sel, baseObj, rect, boxThings, outList);
    }

    /// <summary>Ctrl 路径第一步：「相似」项（与原版相似判断一致）。有新增时整体预览并返回 true。</summary>
    private static bool TryPredictSimilar(Selector sel, object baseObj, Rect rect, List<Thing> boxThings, List<object> outList)
    {
        if (baseObj is Thing baseThing)
        {
            Thing baseInner = baseThing.GetInnerIfMinified();
            List<Thing> matched = new List<Thing>();
            bool anyNew = false;
            for (int i = 0; i < boxThings.Count; i++)
            {
                Thing t = boxThings[i];
                if (!IsSimilarThing(t, baseInner))
                {
                    continue;
                }
                matched.Add(t);
                if (!sel.IsSelected(t))
                {
                    anyNew = true;
                }
            }
            if (anyNew)
            {
                outList.AddRange(matched);
                return true;
            }
            return false;
        }

        if (baseObj is Zone baseZone)
        {
            List<Zone> matched = new List<Zone>();
            bool anyNew = false;
            foreach (Zone z in ThingSelectionUtility.MultiSelectableZonesInScreenRectDistinct(rect))
            {
                if (z.GetType() != baseZone.GetType())
                {
                    continue;
                }
                matched.Add(z);
                if (!sel.IsSelected(z))
                {
                    anyNew = true;
                }
            }
            if (anyNew)
            {
                outList.AddRange(matched);
                return true;
            }
            return false;
        }

        if (baseObj is Plan basePlan)
        {
            List<Plan> matched = new List<Plan>();
            bool anyNew = false;
            foreach (Plan p in ThingSelectionUtility.MultiSelectablePlansInScreenRectDistinct(rect))
            {
                if (p.Color != basePlan.Color)
                {
                    continue;
                }
                matched.Add(p);
                if (!sel.IsSelected(p))
                {
                    anyNew = true;
                }
            }
            if (anyNew)
            {
                outList.AddRange(matched);
                return true;
            }
        }
        return false;
    }

    /// <summary>Ctrl 路径第二步：按基准对象的类型增量选择（有新增时整体预览并返回 true）。</summary>
    private static bool TryPredictByType(Selector sel, object baseObj, Rect rect, List<Thing> boxThings, List<object> outList)
    {
        if (baseObj is Zone)
        {
            return AppendIfAnyNew(sel, ThingSelectionUtility.MultiSelectableZonesInScreenRectDistinct(rect).ToList(), outList);
        }
        if (baseObj is Pawn basePawn)
        {
            return AppendIfAnyNew(sel, Matches(boxThings, t => t.def.category == ThingCategory.Pawn && t.Faction == basePawn.Faction), outList);
        }
        if (baseObj is Thing baseBuilding && baseBuilding.def.category == ThingCategory.Building)
        {
            return AppendIfAnyNew(sel, Matches(boxThings, t => t.def.category == ThingCategory.Building && t.Faction == baseBuilding.Faction), outList);
        }
        if (baseObj is Thing resourceBase && resourceBase.def.CountAsResource)
        {
            return AppendIfAnyNew(sel, Matches(boxThings, static t => t.def.CountAsResource), outList);
        }
        if (baseObj is Thing)
        {
            // 既不是 Pawn 也不是 Resource 也不是 Building → 其余可选物。
            return AppendIfAnyNew(sel, Matches(boxThings, static t =>
                t.def.category != ThingCategory.Pawn && !t.def.CountAsResource && t.def.category != ThingCategory.Building), outList);
        }
        return false;
    }

    /// <summary>结果中存在未选中的项时整体预览（Ctrl / 改进 Shift 都是增量追加，已选中的项也仍属于选中集合）。</summary>
    private static bool AppendIfAnyNew<T>(Selector sel, List<T> candidates, List<object> outList) where T : class
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (!sel.IsSelected(candidates[i]))
            {
                outList.AddRange(candidates);
                return true;
            }
        }
        return false;
    }

    /// <summary>框内可多选的物体（复用缓冲，调用方不得跨调用持有引用）。</summary>
    private static List<Thing> GetBoxThings(Rect rect)
    {
        tmpBoxThings.Clear();
        tmpBoxThings.AddRange(ThingSelectionUtility.MultiSelectableThingsInScreenRectDistinct(rect));
        return tmpBoxThings;
    }

    /// <summary>
    /// 优先级链（殖民者 → 人类 → 资源 → Pawn → 可选物）。
    /// requireNew = false：返回首个「有命中」的优先级（原版行为）；
    /// requireNew = true：返回首个「有未选中命中」的优先级，全部已选中则返回 null（改进 Shift 的降级）。
    /// </summary>
    private static List<Thing>? PickPriorityLevel(List<Thing> boxThings, Selector? sel, bool requireNew)
    {
        if (TryLevel(boxThings, IsColonist, sel, requireNew, out List<Thing>? level))
        {
            // 原版对殖民者按殖民者条顺序排序，影响数量上限裁剪时的保留顺序。
            SelectorUtility.SortInColonistBarOrder(level!);
            return level;
        }
        if (TryLevel(boxThings, IsHumanlike, sel, requireNew, out level))
        {
            return level;
        }
        if (TryLevel(boxThings, IsResource, sel, requireNew, out level))
        {
            return level;
        }
        if (TryLevel(boxThings, IsPawn, sel, requireNew, out level))
        {
            return level;
        }
        return TryLevel(boxThings, IsSelectable, sel, requireNew, out level) ? level : null;
    }

    private static bool TryLevel(List<Thing> boxThings, Predicate<Thing> predicate, Selector? sel, bool requireNew, out List<Thing>? matches)
    {
        matches = Matches(boxThings, predicate);
        if (matches.Count == 0)
        {
            matches = null;
            return false;
        }
        if (!requireNew)
        {
            return true;
        }
        for (int i = 0; i < matches.Count; i++)
        {
            if (sel != null && !sel.IsSelected(matches[i]))
            {
                return true; // 本优先级有新增 → 结束降级。
            }
        }
        matches = null;
        return false; // 全部已选中 → 降级尝试下一优先级。
    }

    /// <summary>按条件筛选（结果为新列表，原列表保持不变）。</summary>
    private static List<Thing> Matches(List<Thing> source, Predicate<Thing> predicate)
    {
        List<Thing> result = new List<Thing>();
        for (int i = 0; i < source.Count; i++)
        {
            if (predicate(source[i]))
            {
                result.Add(source[i]);
            }
        }
        return result;
    }

    // 原版 SelectInsideDragBox 内的优先级判定（与 ImprovedShiftDragBoxPatch 中的局部函数一致）。

    private static readonly Predicate<Thing> IsColonist = static t =>
        t.def.category == ThingCategory.Pawn
        && (((Pawn)t).RaceProps.Humanlike || (ModsConfig.BiotechActive && ((Pawn)t).RaceProps.IsMechanoid))
        && t.Faction == Faction.OfPlayer;

    private static readonly Predicate<Thing> IsHumanlike = static t =>
        t.def.category == ThingCategory.Pawn && ((Pawn)t).RaceProps.Humanlike;

    private static readonly Predicate<Thing> IsResource = static t => t.def.CountAsResource;

    private static readonly Predicate<Thing> IsPawn = static t => t.def.category == ThingCategory.Pawn;

    private static readonly Predicate<Thing> IsSelectable = static t => t.def.selectable;

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

    #endregion

    #region 世界地图预测

    /// <summary>世界地图 <c>WorldSelector.SelectInsideDragBox</c>：商队 → 地图殖民者条 → 世界对象（商队优先 / 玩家优先）。</summary>
    private static void PredictWorld(WorldSelector sel, Rect rect, List<WorldObject> outList)
    {
        if (Current.ProgramState == ProgramState.Playing)
        {
            List<Caravan> caravans = Find.ColonistBar.CaravanMembersCaravansInScreenRect(rect);
            if (caravans.Count > 0)
            {
                outList.AddRange(caravans);
                return;
            }
            // 框住地图上的殖民者条图标时，原版会跳回地图并选中殖民者，世界地图上无对象可预览。
            if (Find.ColonistBar.MapColonistsOrCorpsesInScreenRect(rect).Count > 0)
            {
                return;
            }
        }

        List<WorldObject> objects = WorldObjectSelectionUtility.MultiSelectableWorldObjectsInScreenRectDistinct(rect).ToList();
        if (objects.Any(static o => o is Caravan))
        {
            // 框内有商队时只选商队；若有玩家商队则只选玩家的。
            objects.RemoveAll(static o => o is not Caravan);
            if (objects.Any(static o => o.Faction == Faction.OfPlayer))
            {
                objects.RemoveAll(static o => o.Faction != Faction.OfPlayer);
            }
        }
        outList.AddRange(objects);
    }

    #endregion

    #region 绘制

    /// <summary>地图：在原版选择括号之后绘制预览（同为 Update 阶段的元层绘制，顺序稳定）。</summary>
    private static void DrawMapPreview()
    {
        if (!Active || Find.ScreenshotModeHandler.Active)
        {
            return;
        }
        Selector sel = Find.Selector;
        if (sel == null || !sel.dragBox.IsValidAndActive)
        {
            return;
        }
        EnsureMapPrediction(sel);
        Map map = Find.CurrentMap;
        for (int i = 0; i < mapPredicted.Count; i++)
        {
            object obj = mapPredicted[i];
            // 已选中且仍在框内 → 保持原版白色括号（不变），不覆盖。
            if (sel.IsSelected(obj))
            {
                continue;
            }
            DrawMapObjectHighlight(obj, map, AddColor, AddBracketMat);
        }
        // 已选中但会被排除 → 橙色盖住白色括号。
        for (int i = 0; i < mapExcluded.Count; i++)
        {
            DrawMapObjectHighlight(mapExcluded[i], map, ExcludeColor, ExcludeBracketMat);
        }
    }

    /// <summary>按对象类型绘制地图高亮：Thing 画括号，Zone / Plan 画区域边框。</summary>
    private static void DrawMapObjectHighlight(object obj, Map? map, Color color, Material bracketMat)
    {
        switch (obj)
        {
            case Thing t when t.Spawned && t.MapHeld == map:
                SelectionDrawer.DrawSelectionBracketFor(t, bracketMat);
                break;
            case Zone z when z.Map == map:
                GenDraw.DrawFieldEdges(z.Cells, color, null, null, PreviewFieldEdgeRenderQueue);
                break;
            case Plan p when p.Map == map:
                GenDraw.DrawFieldEdges(p.Cells, color, null, null, PreviewFieldEdgeRenderQueue);
                break;
        }
    }

    /// <summary>世界地图：在原版选择括号之后为预测对象补画括号（原版只在 OnGUI 里画，这里同样在 OnGUI 后置中绘制）。</summary>
    private static void DrawWorldPreview()
    {
        if (!Active || Find.ScreenshotModeHandler.Active)
        {
            return;
        }
        WorldSelector sel = Find.WorldSelector;
        if (sel == null || !sel.dragBox.IsValidAndActive)
        {
            return;
        }
        EnsureWorldPrediction(sel);
        for (int i = 0; i < worldPredicted.Count; i++)
        {
            WorldObject obj = worldPredicted[i];
            // 已选中且仍在框内 → 保持原版白色括号（不变）。
            if (sel.IsSelected(obj))
            {
                continue;
            }
            DrawWorldBracket(obj, AddColor);
        }
        // 已选中但会被排除 → 橙色盖住白色括号（IMGUI 后置绘制，天然在原版之上）。
        for (int i = 0; i < worldExcluded.Count; i++)
        {
            DrawWorldBracket(worldExcluded[i], ExcludeColor);
        }
    }

    /// <summary>按原版世界对象括号的位置与尺寸绘制，仅把颜色换成指定颜色。</summary>
    private static void DrawWorldBracket(WorldObject obj, Color color)
    {
        Vector2 screenPos = obj.ScreenPos();
        Vector2 texSize = new Vector2(SelectionDrawerUtility.SelectedTexGUI.width, SelectionDrawerUtility.SelectedTexGUI.height) * WorldBracketTexScale;
        SelectionDrawerUtility.CalculateSelectionBracketPositionsUI(
            worldBracketLocs,
            obj,
            new Rect(screenPos.x - WorldBracketHalfSize, screenPos.y - WorldBracketHalfSize, WorldBracketHalfSize * 2f, WorldBracketHalfSize * 2f),
            WorldSelectionDrawer.SelectTimes,
            texSize,
            25f);
        Color oldColor = GUI.color;
        GUI.color = color;
        int angle = 90;
        for (int i = 0; i < 4; i++)
        {
            Widgets.DrawTextureRotated(worldBracketLocs[i], SelectionDrawerUtility.SelectedTexGUI, angle, WorldBracketTexScale);
            angle += 90;
        }
        GUI.color = oldColor;
    }

    /// <summary>
    /// 在选择框内绘制「将选中 N 项」；当结果里含有已选中的对象时同时标出新增数量。
    /// 位置取距离鼠标最远的那一角，避免被光标挡住。
    /// </summary>
    private static void DrawCountLabel(Rect boxRect, int total, int newCount)
    {
        if (!MyModTemplateSettings.dragBoxPreviewShowCount || total <= 0 || Find.ScreenshotModeHandler.Active)
        {
            return;
        }
        string text = newCount >= total
            ? "BetterInspectPane.DragBoxPreviewCount".Translate(total)
            : "BetterInspectPane.DragBoxPreviewCountNew".Translate(total, newCount);

        GameFont oldFont = Text.Font;
        TextAnchor oldAnchor = Text.Anchor;
        Color oldColor = GUI.color;
        try
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Vector2 size = Text.CalcSize(text);
            const float padX = 5f;
            const float padY = 2f;
            float w = size.x + padX * 2f;
            float h = size.y + padY * 2f;
            Vector2 mouse = UI.MousePositionOnUIInverted;
            float x = mouse.x < boxRect.center.x ? boxRect.xMax - w - 2f : boxRect.xMin + 2f;
            float y = mouse.y < boxRect.center.y ? boxRect.yMax - h - 2f : boxRect.yMin + 2f;
            // 选择框很小时标签会溢出到框外，仍需限制在屏幕内。
            x = Mathf.Clamp(x, 2f, UI.screenWidth - w - 2f);
            y = Mathf.Clamp(y, 2f, UI.screenHeight - h - 2f);
            Rect rect = new Rect(x, y, w, h);
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.55f));
            GUI.color = AddColor;
            Widgets.Label(rect, text);
        }
        finally
        {
            GUI.color = oldColor;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }
    }

    #endregion

    #region 殖民者条预览

    // 每帧缓存殖民者条预览用的命中集合：直接复用地图预测结果，保证头像上的标记与地图上的标记完全一致
    // （选中的殖民者在地图上画括号，在殖民者条上就画头像括号）。
    private static int barFrame = -1;
    private static bool barDragging;
    private static readonly HashSet<object> barPredictedSet = new HashSet<object>();
    private static readonly HashSet<object> barExcludedSet = new HashSet<object>();

    /// <summary>
    /// 确保本帧殖民者条预览状态已计算（仅地图视图；世界地图里殖民者条框选的语义不同，不预览）。
    /// 预测本身已经包含「框住殖民者条头像」这一分支（原版优先级 1），因此这里只需复用同一帧的预测结果。
    /// </summary>
    private static void EnsureColonistBarState()
    {
        int frame = Time.frameCount;
        if (barFrame == frame)
        {
            return;
        }
        barFrame = frame;
        barDragging = false;
        barPredictedSet.Clear();
        barExcludedSet.Clear();
        if (!Active || Find.CurrentMap == null || !WorldRendererUtility.DrawingMap)
        {
            return;
        }
        Selector sel = Find.Selector;
        if (sel == null || !sel.dragBox.IsValidAndActive)
        {
            return;
        }
        EnsureMapPrediction(sel);
        barDragging = true;
        for (int i = 0; i < mapPredicted.Count; i++)
        {
            barPredictedSet.Add(mapPredicted[i]);
        }
        for (int i = 0; i < mapExcluded.Count; i++)
        {
            barExcludedSet.Add(mapExcluded[i]);
        }
    }

    /// <summary>
    /// 殖民者条条目的预览（由 <c>ColonistBarColonistDrawer.DrawColonist</c> 后置逐条目调用）：
    /// 与地图同一套三色语义 —— 青色 = 将要增加、橙色 = 将被排除、白色 = 不变（直接沿用原版括号）。
    /// </summary>
    private static void DrawColonistBarEntryPreview(Rect entryRect, Pawn colonist)
    {
        if (!Active || Find.ScreenshotModeHandler.Active)
        {
            return;
        }
        EnsureColonistBarState();
        if (!barDragging)
        {
            return;
        }
        // 与原版一致：死亡且尸体已生成时，实际被选中的是尸体。
        Thing? target = colonist.Dead ? colonist.Corpse : colonist;
        if (target == null)
        {
            return;
        }
        bool willSelect = barPredictedSet.Contains(target);
        bool willExclude = barExcludedSet.Contains(target);
        if (!willSelect && !willExclude)
        {
            return; // 不变。
        }
        if (willSelect && Find.Selector.IsSelected(target))
        {
            return; // 已选中且仍会被选中 → 原版白色括号即为「不变」。
        }
        // 与原版 ColonistBarColonistDrawer.DrawSelectionOverlayOnGUI 完全相同的矩形与缩放，保证覆盖白色括号；
        // 其它地图的条目按原版规则降低不透明度。
        Color color = willSelect ? AddColor : ExcludeColor;
        color.a *= Find.ColonistBar.GetEntryRectAlpha(entryRect);
        Color oldColor = GUI.color;
        GUI.color = color;
        SelectionDrawerUtility.DrawSelectionOverlayOnGUI(
            target,
            entryRect.ContractedBy(-2f * Find.ColonistBar.Scale),
            0.4f * Find.ColonistBar.Scale,
            20f * Find.ColonistBar.Scale);
        GUI.color = oldColor;
    }

    #endregion

    #region 补丁

    /// <summary>地图预览绘制（Update 阶段，紧随原版选择括号）。</summary>
    [HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionOverlays))]
    public static class MapPreviewDrawPatch
    {
        static void Postfix()
        {
            DrawMapPreview();
        }
    }

    /// <summary>地图数量标签（OnGUI 阶段，紧随原版选择框绘制）。</summary>
    [HarmonyPatch(typeof(DragBox), nameof(DragBox.DragBoxOnGUI))]
    public static class MapPreviewCountPatch
    {
        static void Postfix(DragBox __instance)
        {
            if (!Active || !__instance.IsValidAndActive)
            {
                return;
            }
            EnsureMapPrediction(Find.Selector);
            DrawCountLabel(__instance.ScreenRect, mapTotalCount, mapNewCount);
        }
    }

    /// <summary>世界地图预览绘制（OnGUI 阶段，紧随原版世界对象括号）。</summary>
    [HarmonyPatch(typeof(WorldSelectionDrawer), nameof(WorldSelectionDrawer.SelectionOverlaysOnGUI))]
    public static class WorldPreviewDrawPatch
    {
        static void Postfix()
        {
            DrawWorldPreview();
        }
    }

    /// <summary>世界地图数量标签（OnGUI 阶段，紧随原版选择框绘制）。</summary>
    [HarmonyPatch(typeof(WorldDragBox), nameof(WorldDragBox.DragBoxOnGUI))]
    public static class WorldPreviewCountPatch
    {
        static void Postfix(WorldDragBox __instance)
        {
            if (!Active || !__instance.IsValidAndActive)
            {
                return;
            }
            EnsureWorldPrediction(Find.WorldSelector);
            DrawCountLabel(__instance.ScreenRect, worldTotalCount, worldNewCount);
        }
    }

    /// <summary>殖民者条条目预览（OnGUI Repaint 阶段，原版条目绘制之后）。</summary>
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    public static class ColonistBarPreviewPatch
    {
        static void Postfix(Rect rect, Pawn colonist)
        {
            DrawColonistBarEntryPreview(rect, colonist);
        }
    }

    #endregion

    #region 工具

    private static bool CtrlIsHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

    #endregion
}
