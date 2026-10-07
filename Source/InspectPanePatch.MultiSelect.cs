using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using HarmonyLib;
using static ASQBetterInspectPane.MyModTemplateSettings;


namespace ASQBetterInspectPane;

/// <summary>
/// InspectPanePatch 的多选网格部分：接管多选时（>1 个对象被选中，非存储组）的检查面板正文，
/// 绘制 RTS 风格的对象选择网格。
/// 原版 MainTabWindow_Inspect.ShouldShowPaneContents 在多选（>1 且非存储组）时返回 false，
/// 因此 DoPaneContents（进而 DoPaneContentsFor）根本不会被调用，正文为空、仅显示标题。
/// 本文件用补丁接管：先把 ShouldShowPaneContents 在命中网格场景时翻为 true（使正文绘制被触发），
/// 再在 DoPaneContents 命中网格场景时绘制网格并 return false 跳过原版 —— 原版方法体（非存储组分支）
/// 会把 FirstSelectedObject 当作单选内容分发到 DoPaneContentsFor，若不拦截会显示第一个对象的单选面板。
///
/// 网格引擎是**类型无关**的（MultiSelectScene）：支持三类被选对象：
///   - Things：殖民者（Pawn.IsColonist）单列立绘，其余 Pawn 按 PawnKindDef、非 Pawn 按 ThingDef 分组；
///   - Zones：殖民地图内的区域，按具体 Zone 类型分组；
///   - World：世界地图对象（WorldInspectPane 独立接管），按 WorldObjectDef 分组。
/// 三者的交互/分组完全一致。默认映射（可用「使用 Shift 键多选 / 使用 Ctrl 键多重取消」设置互换）：
/// 左键单击/拖拽多选（黄，= 保留被选中的项、移除未被选择的项）、Shift 单击单选、
/// 右键单击/拖拽多重取消（橙）、Ctrl 单击取消。
///
/// 场景判定唯一事实来源为 GetMultiSelectScene()（地图）与本文件的世界判定 IsWorldMultiSelectGridActive()。
/// 各职责按 region 划分：常量与数据结构 / Harmony 补丁 / 场景判定 / 面板尺寸 / 分组 / 绘制 / 点击交互。
/// </summary>
public static partial class InspectPanePatch
{
    #region 常量与数据结构

    // 溢出提示格字号（数量角标字号为可调设置，见 Settings.cs 的 multiSelectBadgeFontSize）。
    private const int MultiSelectMoreFontSize = 12;
    // 数量角标的最小宽度尺寸
    private const float MultiSelectBadgeWidth = 8f;
    // 数量角标的基础尺寸（实际绘制时乘以缩放比例设置 multiSelectBadgeScale）。
    private const float MultiSelectBadgeHeight = 14f;
    // 单元格间距、边框宽度与数量角标（字号/缩放）均为可调设置（Settings.cs）。

    // 多选（>1 且非存储组）时原版 DoInspectPaneButtons 不画任何按钮（lineEndWidth 保持 0），
    // 因此设置按钮直接从面板右缘右对齐排布，与网格底框右缘齐平，无需为原版按钮簇让位。
    // 仅多选使用此标题行按钮；普通单选面板的设置按钮仍留在正文右下角（见 InspectPanePatch.Prefix 步骤 9）。

    /// <summary>多选网格命中的场景（决定分组/绘制/点选走哪套逻辑）。World 仅由世界面板路径使用。</summary>
    private enum MultiSelectScene { None, Things, Zones, World }

    /// <summary>多选网格中的一个单元格：殖民者单列，其余按 PawnKindDef / ThingDef / Zone 类型 / WorldObjectDef 分组。</summary>
    private struct GridCell
    {
        public object representative; // 该组首个实例（立绘/图标/色块来源；Thing / Zone / WorldObject）
        public List<object> members;  // 该组全部实例（殖民者 / 单组展开 = 仅自身）
        public bool isColonist;       // 殖民者单列单元（仅 Things 使用）
        public bool isSingle;         // 单个项单元格（殖民者单列或数量为 1 的组）：提示显示个人标签
        public int count;             // 预计算数量：Thing = 组内各实例 stackCount 之和；Zone/World = 组内对象数。
        public float healthFill;      // 健康目标值缓存（仅 Things：Pawn 总体健康 / 分组均值，每 20 帧重算一次）。
    }

    // 「构建选区」实例集合（多选待应用）：黄色高亮这些单元格（提示本次将保留哪些）。
    // 默认由左键单击/拖拽加入；开启「使用 Shift 键多选」后改由 Shift 触发（恢复旧行为）。
    // 同一组可在「加入 / 移出」间切换（再点一次则移出）；放开触发键后应用「移除未被选择的项」：
    // 保留这些实例、把当前选择中未被加入的其余对象全部取消选中。
    // 单选/取消点击会清空选择及各类标记。
    private static readonly HashSet<object> shiftSelectedThings = new HashSet<object>();

    // 「待移除」实例集合（多重取消待应用）：橙色高亮这些单元格。
    // 默认由右键单击/拖拽标记；开启「使用 Ctrl 键多重取消」后改由 Ctrl 触发（恢复旧行为）。
    // 同一组可在「标记 / 取消标记」间切换（再点一次则取消移除）；放开触发键后从选择中移除（不影响其它选中）。
    private static readonly HashSet<object> ctrlMarkedForRemoval = new HashSet<object>();

    // 「额外选中指示」：当前帧网格中鼠标悬停的单元格成员（供地图上的原版指示箭头使用，仅 Things 场景有效）。
    // 每帧在绘制网格时按鼠标位置更新（见 UpdateHoveredGridCell）；网格不再显示时由场景判定清空。
    private static readonly List<object> hoveredGridMembers = new List<object>();

    // Shift 构建选区（多选 = 移除未被选择的项）的单元格高亮配色：淡黄色背景 + 黄色边框。
    private static readonly Color MultiSelectShiftFill = new Color(1f, 0.92f, 0.45f, 0.4f);
    private static readonly Color MultiSelectShiftBorder = new Color(1f, 0.85f, 0f, 1f);

    // Ctrl 待移除的单元格高亮配色：橙色背景 + 橙色边框。
    private static readonly Color MultiSelectCtrlFill = new Color(1f, 0.6f, 0.1f, 0.4f);
    private static readonly Color MultiSelectCtrlBorder = new Color(1f, 0.4f, 0f, 1f);

    // 边框纹理：预烘焙「白色边框、内部透明」的纹理，用 GUI.color 染色后一次绘制即出边框，
    // 替代每格四段 DrawBoxSolid 的开销。烘焙判断在设置处（Settings.cs 的单元尺寸/边框宽度变化或加载时触发），
    // 绘制路径只使用该纹理、不按绘制参数判断（避免绘制参数不一致导致反复烘焙）。
    private static Texture2D? multiSelectBorderTex;
    private static int multiSelectBorderTexSize = -1;
    private static int multiSelectBorderTexWidth = -1;

    // 角标文本宽度按「×」之后的字符数缓存：假定 '.' 与 'k' 与数字等宽，同长度只测量一次，
    // 避免每帧重复 Text.CalcSize（k 缩写后实际字符数很小，数组长度足够；字号变化时见下方缓存失效逻辑）。
    private static readonly float[] multiSelectBadgeWidthCache = new float[12];
    private static readonly bool[] multiSelectBadgeWidthCached = new bool[12];
    // 角标宽度缓存对应的字号：字号设置变化时缓存失效（宽度随字号缩放，旧测量值不再适用）。
    private static int multiSelectBadgeWidthCacheFontSize = -1;
    // 「×」前缀字形宽度缓存：仅随字号变化失效，避免热路径（每帧每格）重复 Text.CalcSize。
    private static float multiSelectBadgeXWidth;
    private static int multiSelectBadgeXWidthFontSize = -1;

    // Things 网格内容缓存：把上次构建的单元格与「选中序列快照」（thingIDNumber 有序 + 各实例 stackCount）绑定。
    // 每帧先做 O(n) 整数比对，内容未变则复用 cells，避免每帧重建 List/Dictionary 造成 GC 垃圾。
    // 1.6 的 Selector 无 selectedVersion 字段（SelectedObjects 返回同一列表引用、原地增删），
    // 因此用快照比对判定选中内容是否变化：任一销毁/增减/换序都会反映到 thingIDNumber 或 stackCount 上。
    // 仅 Things 选择可能较大，Region/World 选择量小，每帧直接重建不做缓存。
    private static int[] cachedGridSnapshotThingIds = new int[0];
    private static int[] cachedGridSnapshotStacks = new int[0];
    private static int cachedGridSnapshotCount = -1;
    private static List<GridCell>? cachedGridCells;

    // 多选健康条每格缓动状态：按代表物 thingIDNumber 索引（跨网格重建/重排持续过渡，仅 Things）。
    // EaseState / easeSpeed / easingStyle 复用单选框的缓动实现。
    private static readonly Dictionary<int, EaseState> cellHealthEase = new Dictionary<int, EaseState>();

    // 健康目标值按帧缓存：健康计算（Pawn 总体健康 / 分组求均值）每 20 帧重算一次，
    // 避免每帧遍历各格成员计算健康（分组含大量成员时开销显著；仅 Things 使用）。
    private const int CellHealthCacheFrames = 20;
    private static int cellHealthRefreshFrame = -1000;

    // 当前拖拽手势（一次「按下→移动→松开」的完整状态）。
    private static MultiSelectDragGesture multiSelectDrag;

    #endregion

    #region 对象选择分发

    /// <summary>对象是否已销毁（Thing / WorldObject；Zone 无 Destroyed 概念，恒视为存活）。</summary>
    private static bool ObjDestroyed(object o)
        => o is Thing t ? t.Destroyed : o is WorldObject w && w.Destroyed;

    /// <summary>对象是否在对应 Selector 中被选中（Thing/Zone→Find.Selector，WorldObject→Find.WorldSelector）。</summary>
    private static bool ObjSelected(object o) => o switch
    {
        Thing t => Find.Selector.IsSelected(t),
        Zone z => Find.Selector.IsSelected(z),
        WorldObject w => Find.WorldSelector.IsSelected(w),
        _ => false
    };

    /// <summary>按对象类型把其选中到对应 Selector（含选择音与设计器取消参数）。</summary>
    private static void SelectObj(object o, bool playSound, bool forceDeselect)
    {
        if (o is Thing t) Find.Selector.Select(t, playSound, forceDeselect);
        else if (o is Zone z) Find.Selector.Select(z, playSound, forceDeselect);
        else if (o is WorldObject w) Find.WorldSelector.Select(w, playSound);
    }

    /// <summary>按对象类型把其从对应 Selector 取消选中。</summary>
    private static void DeselectObj(object o)
    {
        if (o is Thing t) Find.Selector.Deselect(t);
        else if (o is Zone z) Find.Selector.Deselect(z);
        else if (o is WorldObject w) Find.WorldSelector.Deselect(w);
    }

    /// <summary>清空当前场景的选择：Things/Zones 用 Find.Selector，World 用 Find.WorldSelector。</summary>
    private static void ClearSceneSelection(MultiSelectScene scene)
    {
        if (scene == MultiSelectScene.World)
        {
            Find.WorldSelector.ClearSelection();
        }
        else
        {
            Find.Selector.ClearSelection();
        }
    }

    /// <summary>对象在 tooltip 缓存/健康缓动中使用的稳定整型键。</summary>
    private static int ObjUniqueId(object o) => o switch
    {
        Thing t => t.thingIDNumber,
        Zone z => z.ID,
        WorldObject w => w.ID,
        _ => o.GetHashCode()
    };

    /// <summary>清空本文件的全部多选交互状态（构建选区 / 待移除 / 悬停 / 拖拽）。</summary>
    private static void ClearInteractionState()
    {
        ctrlMarkedForRemoval.Clear();
        shiftSelectedThings.Clear();
        hoveredGridMembers.Clear();
        multiSelectDrag.Reset();
    }

    #endregion

    #region Harmony 补丁

    /// <summary>让多选（非存储组）时正文绘制被启用，从而触发 DoPaneContents 的网格绘制。</summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.ShouldShowPaneContents), MethodType.Getter)]
    public static class MultiSelectShouldShowPaneContentsPatch
    {
        static void Postfix(ref bool __result)
        {
            if (__result)
            {
                return; // 单选 / 存储组多选已为 true，不干预。
            }
            if (GetMultiSelectScene() != MultiSelectScene.None)
            {
                __result = true;
            }
            else
            {
                ClearInteractionState(); // 非网格场景：清掉可能遗留的交互状态。
            }
        }
    }

    /// <summary>多选时接管正文绘制：命中 Things/Zones 网格场景则绘制网格并跳过原版
    /// （原版方法体在非存储组分支会把 FirstSelectedObject 当作单选内容分发给 DoPaneContentsFor）。</summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.DoPaneContents))]
    public static class MultiSelectDoPaneContentsPatch
    {
        static bool Prefix(Rect rect)
        {
            MultiSelectScene scene = GetMultiSelectScene();
            if (scene == MultiSelectScene.None)
            {
                ClearInteractionState();
                return true; // 单选 / 存储组多选 / 未启用：走原版。
            }
            if (scene == MultiSelectScene.Zones)
            {
                if (TryGetMultiSelectZones(out List<Zone> zones))
                {
                    DrawSceneGrid(rect, MultiSelectScene.Zones, BuildZoneCells(zones));
                    return false;
                }
                return true;
            }
            // Things：用快照缓存构建（数量可能较大）。
            if (TryGetMultiSelectThings(out List<Thing> things))
            {
                DrawSceneGrid(rect, MultiSelectScene.Things, GetOrBuildThingCells(things));
                return false;
            }
            ClearInteractionState();
            return true;
        }
    }

    /// <summary>标题行是否显示设置按钮：仅当多选网格（地图 Things/Zones 场景）时显示。</summary>
    private static bool ShouldShowTitleLineSettingsButton()
    {
        return enabled && GetMultiSelectScene() != MultiSelectScene.None;
    }

    /// <summary>整个世界检查面板窗口在当前 GUI 群组局部坐标系下的矩形。
    /// DoInspectPaneButtons 收到的 rect 只是内容区（四周收缩边距后），且其 x/y 是群组原点对应的屏幕坐标，
    /// 直接 Mouse.IsOver(rect) 会因坐标系不符而失效；悬停判定需要的是覆盖整个面板（含边距/边框区）的矩形，
    /// 否则鼠标移到面板右上角（按钮所在角落）时按钮不出现。做法：把内容区还原为群组局部坐标
    /// （即 (0,0,w,h)）后各边外扩边距。原版边距固定 12f，被覆盖的 InspectPaneOnGUI 用可调 paneMargin；
    /// 两路径的纵向微调差异（原版 yMin-4/yMax+6、覆盖路径按钮组下移 buttonY）由 6f 安全余量覆盖
    /// （titleFontSize 上限 32 时 buttonY ≤ 6）。</summary>
    private static Rect FullPaneHoverRect(Rect rect)
    {
        float m = overrideInspectPane ? paneMargin : 12f;
        return new Rect(-m, -m - 6f, rect.width + 2f * m, rect.height + 2f * m + 6f);
    }

    /// <summary>把设置按钮放到标题行右上角：与标题同一行、与信息按钮同列区，不预留正文。
    /// 同时适用于原版 InspectPaneOnGUI 与被覆盖的 InspectPaneOnGUI（两者都会调用 DoInspectPaneButtons）。</summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.DoInspectPaneButtons))]
    public static class PaneTitleSettingsButtonPatch
    {
        static void Postfix(Rect rect, ref float lineEndWidth)
        {
            // 仅鼠标悬停在面板上时显示（与单选面板右下角设置按钮一致）。
            // 悬停判定用还原后的完整面板矩形，覆盖面板右上角（含边距区）。
            if (!Mouse.IsOver(FullPaneHoverRect(rect)))
            {
                return;
            }
            if (!ShouldShowTitleLineSettingsButton())
            {
                return;
            }
            // 安装InfoCardPlus后向左偏移
            var icpOffset = InfoCardPlusReflection.IsActive ? 30f : 0f;
            // 与面板右缘右对齐：right = width - 边距，紧邻网格底框右缘。
            Rect settingsBtn = new Rect(
                rect.width - SettingsButtonHeight - SettingsButtonMargin - icpOffset,
                0f,
                SettingsButtonHeight, SettingsButtonHeight);
            if (Widgets.ButtonImage(settingsBtn, SettingsIconTex))
            {
                Find.WindowStack.Add(new Dialog_MyModSettings());
            }
            TooltipHandler.TipRegion(settingsBtn, "BetterInspectPane.OpenSettingsTip".Translate());
            lineEndWidth += SettingsButtonHeight;
        }
    }

    /// <summary>
    /// 「额外选中指示」：多选网格中鼠标悬停 / 「构建选区」 / 「待移除」的当前项，
    /// 在地图上用原版指示箭头（GenDraw.DrawArrowPointingAt）标出，便于从地图上定位当前项。
    /// 仅对 Thing 项生效（Zone/World 无地图箭头指示）。
    /// 后置在 SelectionDrawer.DrawSelectionOverlays 之后执行，不改变原版选择框绘制。
    /// </summary>
    [HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionOverlays))]
    public static class MultiSelectMapIndicatorPatch
    {
        static void Postfix()
        {
            if (!enabled || !enableMultiSelectMapIndicator)
            {
                return;
            }
            // 截图模式与地图未绘制时原版直接返回（本后置仍会触发），这里一并跳过保持一致。
            if (Find.ScreenshotModeHandler.Active || !IsMultiSelectGridActive())
            {
                return;
            }
            for (int i = 0; i < hoveredGridMembers.Count; i++)
            {
                if (hoveredGridMembers[i] is Thing t)
                {
                    DrawMapIndicator(t);
                }
            }
            foreach (object o in shiftSelectedThings)
            {
                if (o is Thing t2)
                {
                    DrawMapIndicator(t2);
                }
            }
            foreach (object o in ctrlMarkedForRemoval)
            {
                if (o is Thing t3)
                {
                    DrawMapIndicator(t3);
                }
            }
        }
    }

    /// <summary>在指定实体的地图位置绘制原版指示箭头（GenDraw.DrawArrowPointingAt，白色箭头）。
    /// 仅在实体已生成且位于当前地图时绘制；销毁/离图直接跳过。</summary>
    private static void DrawMapIndicator(Thing t)
    {
        if (t == null || t.Destroyed || !t.Spawned || t.MapHeld != Find.CurrentMap)
        {
            return;
        }
        GenDraw.DrawArrowPointingAt(t.DrawPos);
    }

    /// <summary>让世界地图多选对象时正文绘制被启用（WorldInspectPane 与地图共用 InspectPaneUtility 的机制）。</summary>
    [HarmonyPatch(typeof(WorldInspectPane), nameof(WorldInspectPane.ShouldShowPaneContents), MethodType.Getter)]
    public static class WorldMultiSelectShouldShowPaneContentsPatch
    {
        static void Postfix(ref bool __result)
        {
            if (__result)
            {
                return; // 单选 / 选中单个格已为 true，不干预。
            }
            if (IsWorldMultiSelectGridActive())
            {
                __result = true;
            }
            else
            {
                ClearInteractionState();
            }
        }
    }

    /// <summary>世界地图多选时接管世界检查面板正文：命中网格场景则绘制对象网格并跳过原版。</summary>
    [HarmonyPatch(typeof(WorldInspectPane), nameof(WorldInspectPane.DoPaneContents))]
    public static class WorldMultiSelectDoPaneContentsPatch
    {
        static bool Prefix(Rect rect)
        {
            if (!IsWorldMultiSelectGridActive())
            {
                ClearInteractionState();
                return true; // 单选 / 选中单个格 / 未启用：走原版。
            }
            List<WorldObject> wos = Find.WorldSelector.SelectedObjects;
            DrawSceneGrid(rect, MultiSelectScene.World, BuildWorldCells(wos));
            return false;
        }
    }

    #endregion

    #region 场景判定

    // 每帧场景判定缓存：同帧内尺寸/布局/绘制多个补丁都会调用 GetMultiSelectScene()，
    // 每次又遍历并分配 SelectedObjects，大组选择时造成额外 GC。以帧号缓存，帧内首次计算后复用。
    // 缓存的场景值可能与实时列表短暂脱节（选择在本帧被原地增删），但下游 TryGetMultiSelect* 均有逐项
    // 类型校验兜底，只会瞬时回退到原版，不会强转崩溃；下一帧按帧号重算自愈。
    private static int sceneCacheFrame = -1;
    private static MultiSelectScene sceneCacheValue = MultiSelectScene.None;

    /// <summary>
    /// 地图检查面板的多选网格场景（单一事实来源）：主开关 + 多选(>1) + 全部为同类对象。
    /// 地图 Selector 的选择可能混入 Thing / Zone（同一 SelectedObjects 列表原地增删），故逐项按类型判定；
    /// 全部同类才命中对应场景，混选（含 Plan 等）一律归返 None 走原版。
    /// 返回 None / Things / Zones（World 由 IsWorldMultiSelectGridActive 单独判定）。
    /// </summary>
    private static MultiSelectScene GetMultiSelectScene()
    {
        if (!enabled)
        {
            sceneCacheFrame = Time.frameCount;
            sceneCacheValue = MultiSelectScene.None;
            return MultiSelectScene.None;
        }
        int frame = Time.frameCount;
        if (sceneCacheFrame == frame)
        {
            return sceneCacheValue;
        }
        List<object> selected = Find.Selector.SelectedObjects;
        if (selected.Count <= 1)
        {
            sceneCacheFrame = frame;
            sceneCacheValue = MultiSelectScene.None;
            return MultiSelectScene.None;
        }
        bool allThings = true;
        bool allZones = true;
        for (int i = 0; i < selected.Count; i++)
        {
            object o = selected[i];
            if (!(o is Thing))
            {
                allThings = false;
            }
            if (!(o is Zone))
            {
                allZones = false;
            }
            if (!allThings && !allZones)
            {
                break;
            }
        }
        MultiSelectScene scene;
        if (allThings && enableMultiSelectGrid && !IsStorageGroupMultiSelect(selected))
        {
            scene = MultiSelectScene.Things;
        }
        else if (allZones && enableMultiSelectGrid && enableMultiSelectGridZones)
        {
            scene = MultiSelectScene.Zones;
        }
        else
        {
            scene = MultiSelectScene.None;
        }
        sceneCacheFrame = frame;
        sceneCacheValue = scene;
        return scene;
    }

    /// <summary>地图多选网格是否激活（轻量判定）：主开关 + 场景命中。</summary>
    internal static bool IsMultiSelectGridActive()
    {
        return GetMultiSelectScene() != MultiSelectScene.None;
    }

    /// <summary>世界地图多选对象网格是否激活（独立于地图场景；走 WorldSelector 与 WorldInspectPane）。</summary>
    internal static bool IsWorldMultiSelectGridActive()
    {
        if (!enabled || !enableMultiSelectGrid
            || !enableMultiSelectGridWorldObjects)
        {
            return false;
        }
        return Find.WorldSelector.NumSelectedObjects > 1;
    }

    /// <summary>取出多选 Things 列表（每项都强校验为 Thing；混入非 Thing 则回退，避免强转崩溃）。</summary>
    private static bool TryGetMultiSelectThings(out List<Thing> things)
    {
        things = null!;
        if (GetMultiSelectScene() != MultiSelectScene.Things)
        {
            return false;
        }
        List<object> selected = Find.Selector.SelectedObjects;
        List<Thing> list = new List<Thing>(selected.Count);
        for (int i = 0; i < selected.Count; i++)
        {
            if (selected[i] is not Thing t)
            {
                return false; // 混入 Zone/Plan 等 → 走原版，不强转。
            }
            list.Add(t);
        }
        things = list;
        return true;
    }

    /// <summary>取出多选 Zones 列表（每项都强校验为 Zone；混入非 Zone 则回退，避免强转崩溃）。</summary>
    private static bool TryGetMultiSelectZones(out List<Zone> zones)
    {
        zones = null!;
        if (GetMultiSelectScene() != MultiSelectScene.Zones)
        {
            return false;
        }
        List<object> selected = Find.Selector.SelectedObjects;
        List<Zone> list = new List<Zone>(selected.Count);
        for (int i = 0; i < selected.Count; i++)
        {
            if (selected[i] is not Zone z)
            {
                return false; // 混入 Thing/Plan 等 → 走原版，不强转。
            }
            list.Add(z);
        }
        zones = list;
        return true;
    }

    /// <summary>复刻 MainTabWindow_Inspect.TryGetSelectedStorageGroup：全部为同组且组非空的 IStorageGroupMember。</summary>
    private static bool IsStorageGroupMultiSelect(List<object> selected)
    {
        StorageGroup? group = null;
        for (int i = 0; i < selected.Count; i++)
        {
            if (selected[i] is IStorageGroupMember m)
            {
                if (group == null)
                {
                    group = m.Group;
                }
                if (m.Group != group || m.Group == null)
                {
                    return false;
                }
                continue;
            }
            return false;
        }
        return group != null;
    }

    #endregion

    #region 面板尺寸

    /// <summary>
    /// 自适应高度所需的最小面板窗口高度：按当前绘制管线（InspectPaneOnGUI）计算，使内容区能完整容纳
    /// 「配置行数」的网格（分隔线/行距 + 网格）。标题行设置按钮由下方的 titleH 项覆盖，不作为内容区消耗。
    /// 按 overrideInspectPane 开/关两种情况计算标题区与边距消耗。该值只做下限，
    /// 由 MultiSelectEffectiveMinHeight 与基础最小高度取 max 使用。
    /// </summary>
    internal static float MultiSelectGridRequiredPaneHeight()
    {
        float sep = showSeparators ? separatorHeight : 0f;
        // 与 DrawSceneGrid 完全一致：分隔线从 y=rowSpacing 起，再叠加下行距（无末端 gap）。
        float gridY = rowSpacing + sep + rowSpacing;
        // 容纳「配置行数」所需的净网格高 = R*(cell+gap)。DrawSceneGrid 的
        // effectiveRows = floor((availableHeight+gap)/(cell+gap))，当 availableHeight = R*(cell+gap) 时恰好为 R。
        float gridH = multiSelectGridRows
            * (multiSelectGridCellSize + multiSelectCellGap);
        float contentH = gridY + gridH;
        if (overrideInspectPane)
        {
            // 本模组接管绘制：内容高 = 窗口高 - 2*边距 - (标题高 - 24)。
            float titleH = Mathf.Max(50f, LineHeightForSize(titleFontSize) + 24f);
            return contentH + (titleH - 24f) + 2f * paneMargin;
        }
        // 原版绘制：内容高 = 窗口高 - 40（12*2 边距 -4/+6 微调 + 标题 26 下移）。
        return contentH + 40f;
    }

    #endregion

    #region 分组

    /// <summary>
    /// 按场景构建网格单元格：
    ///  - Things：殖民者按选中顺序单列在前；其余 Pawn 按 PawnKindDef、非 Pawn 按 ThingDef 分组（含单组展开），
    ///    count = 组内 stackCount 之和，并走快照缓存（见 GetOrBuildThingCells）。
    ///  - Zones：按具体 Zone 类型（GetType）分组，count = 组内对象数。
    ///  - World：先按 WorldObjectDef、其次按 Faction 分组，count = 组内对象数。
    /// Zones/World 都受单组展开（expandSingleGroupNonThing）控制：同类（世界对象为同类 + 同阵营）
    /// 恰构成单个组时逐项平铺。
    /// </summary>
    private static List<GridCell> BuildZoneCells(List<Zone> zones)
    {
        // 非 Thing 单组展开：所有区域同类型（本会聚合成单个 ×N 格）时逐项平铺。
        if (expandSingleGroupNonThing && zones.Count > 1 && AllSameZoneType(zones))
        {
            List<GridCell> expanded = new List<GridCell>(zones.Count);
            for (int i = 0; i < zones.Count; i++)
            {
                expanded.Add(MakeSingleEntityCell(zones[i]));
            }
            return expanded;
        }

        List<GridCell> cells = new List<GridCell>();
        Dictionary<Type, GridCell> groups = new Dictionary<Type, GridCell>();
        for (int i = 0; i < zones.Count; i++)
        {
            Zone zone = zones[i];
            Type key = zone.GetType();
            if (groups.TryGetValue(key, out GridCell cell))
            {
                cell.members.Add(zone);
                groups[key] = cell;
            }
            else
            {
                GridCell newCell = new GridCell
                {
                    representative = zone,
                    members = new List<object> { zone },
                    isColonist = false
                };
                groups.Add(key, newCell);
                cells.Add(newCell);
            }
        }
        FinishGroupCounts(cells);
        return cells;
    }

    /// <summary>所选区域是否全部为同一具体类型（GetType）。</summary>
    private static bool AllSameZoneType(List<Zone> zones)
    {
        Type first = zones[0].GetType();
        for (int i = 1; i < zones.Count; i++)
        {
            if (zones[i].GetType() != first)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>世界对象网格单元格：先按 WorldObjectDef、其次按 Faction 分组（组内保持选中顺序）。</summary>
    private static List<GridCell> BuildWorldCells(List<WorldObject> wos)
    {
        // 非 Thing 单组展开：所有世界对象同类型且同阵营（本会聚合成单个 ×N 格）时逐项平铺。
        if (expandSingleGroupNonThing && wos.Count > 1 && AllSameWorldGroup(wos))
        {
            List<GridCell> expanded = new List<GridCell>(wos.Count);
            for (int i = 0; i < wos.Count; i++)
            {
                expanded.Add(MakeSingleEntityCell(wos[i]));
            }
            return expanded;
        }

        List<GridCell> cells = new List<GridCell>();
        Dictionary<(WorldObjectDef def, Faction? faction), GridCell> groups = new Dictionary<(WorldObjectDef, Faction?), GridCell>();
        for (int i = 0; i < wos.Count; i++)
        {
            WorldObject wo = wos[i];
            (WorldObjectDef, Faction?) key = (wo.def, wo.Faction);
            if (groups.TryGetValue(key, out GridCell cell))
            {
                cell.members.Add(wo);
                groups[key] = cell;
            }
            else
            {
                GridCell newCell = new GridCell
                {
                    representative = wo,
                    members = new List<object> { wo },
                    isColonist = false
                };
                groups.Add(key, newCell);
                cells.Add(newCell);
            }
        }
        FinishGroupCounts(cells);
        return cells;
    }

    /// <summary>所选世界对象是否全部为同一 (WorldObjectDef, Faction) 组合。</summary>
    private static bool AllSameWorldGroup(List<WorldObject> wos)
    {
        (WorldObjectDef, Faction?) first = (wos[0].def, (Faction?)wos[0].Faction);
        for (int i = 1; i < wos.Count; i++)
        {
            if ((wos[i].def, (Faction?)wos[i].Faction) != first)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>构建单成员实体单元格（区域 / 世界对象的单组展开平铺用：仅自身、数量 1）。</summary>
    private static GridCell MakeSingleEntityCell(object rep)
    {
        return new GridCell
        {
            representative = rep,
            members = new List<object> { rep },
            isColonist = false,
            isSingle = true,
            count = 1
        };
    }

    /// <summary>统计各组数量（count = 组内对象数）并标记单项目（isSingle = 组内仅 1 个）。</summary>
    private static void FinishGroupCounts(List<GridCell> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            GridCell cell = cells[i];
            int n = cell.members.Count;
            cell.count = n;
            cell.isSingle = n == 1;
            cells[i] = cell;
        }
    }

    /// <summary>取网格单元格（Things 专用）：选中内容未变化时复用缓存，否则重建并刷新快照。</summary>
    private static List<GridCell> GetOrBuildThingCells(List<Thing> things)
    {
        if (GridSnapshotMatches(things))
        {
            return cachedGridCells!;
        }
        cachedGridCells = BuildThingCells(things);
        UpdateGridSnapshot(things);
        // 选中内容变化触发的重建：重置健康缓动与缓存刷新帧，避免新一批单元复用旧的缓动/健康值
        // （否则新单元的 healthFill 会停留在 0 直到 20 帧缓存刷新，导致健康条从 0 缓动到实际值）。
        cellHealthEase.Clear();
        cellHealthRefreshFrame = -1000;
        return cachedGridCells;
    }

    /// <summary>
    /// Thing 分组：殖民者（Pawn.IsColonist）按选中顺序单列在前；
    /// 其余 Pawn 按 PawnKindDef、非 Pawn 按 ThingDef 分组（每组按首次出现顺序）。
    /// cells 与字典共用同一份 members 列表（引用类型），数量在分组时统一预计算。
    /// </summary>
    private static List<GridCell> BuildThingCells(List<Thing> things)
    {
        List<GridCell> cells = new List<GridCell>();

        // 第一遍：殖民者单列（排最前）。
        for (int i = 0; i < things.Count; i++)
        {
            if (things[i] is Pawn p && p.IsColonist)
            {
                cells.Add(MakeSingleCell(things[i], isColonist: true));
            }
        }

        // 统计非殖民者项的分类键：Pawn 按 PawnKindDef、非 Pawn 按 ThingDef 分组。
        // 用于「单组展开」：当某类别的非殖民者项恰好构成一个组（且组内有多项）时，
        // 对该类别逐项平铺（每个项目各自一格，不再聚合为 ×N）。
        // 两个类别独立判定、可独立开关（Pawn 默认开，非 Pawn 默认关）。
        HashSet<object> pawnKeys = new HashSet<object>();
        HashSet<object> thingKeys = new HashSet<object>();
        int nonColonistPawnCount = 0;
        int nonColonistThingCount = 0;
        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t is Pawn pawn && pawn.IsColonist)
            {
                continue;
            }
            if (t is Pawn p2)
            {
                pawnKeys.Add(p2.kindDef);
                nonColonistPawnCount++;
            }
            else
            {
                thingKeys.Add(t.def);
                nonColonistThingCount++;
            }
        }
        bool expandPawn = expandSingleGroupPawn
            && pawnKeys.Count == 1 && nonColonistPawnCount > 1;
        bool expandThing = expandSingleGroupNonPawn
            && thingKeys.Count == 1 && nonColonistThingCount > 1;

        // 第二遍：其余按 PawnKindDef / ThingDef 分组；命中的类别逐项平铺，否则保持聚合分组。
        Dictionary<object, GridCell> groups = new Dictionary<object, GridCell>();
        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t is Pawn pawn && pawn.IsColonist)
            {
                continue;
            }
            bool isPawn = t is Pawn;
            object key = isPawn ? ((Pawn)t).kindDef : t.def;
            // 该类别命中「单组展开」时只有这一个键：各自格。仍保留遇到分组键的分组分支未用到。
            if (isPawn ? expandPawn : expandThing)
            {
                cells.Add(MakeSingleCell(t, isColonist: false));
                continue;
            }
            if (groups.TryGetValue(key, out GridCell cell))
            {
                cell.members.Add(t);
                groups[key] = cell;
            }
            else
            {
                GridCell newCell = new GridCell
                {
                    representative = t,
                    members = new List<object> { t },
                    isColonist = false
                };
                groups.Add(key, newCell);
                cells.Add(newCell);
            }
        }

        // 预计算各单元格数量（组内 stackCount 之和）：分组时统一算一次，避免绘制时每帧反复遍历求和。
        for (int i = 0; i < cells.Count; i++)
        {
            GridCell cell = cells[i];
            int n = 0;
            for (int j = 0; j < cell.members.Count; j++)
            {
                if (cell.members[j] is Thing mt)
                {
                    n += mt.stackCount;
                }
            }
            cell.count = n;
            cells[i] = cell;
        }
        return cells;
    }

    /// <summary>快照比对：选中序列（thingIDNumber 有序）与各实例 stackCount 均未变才算未变。</summary>
    private static bool GridSnapshotMatches(List<Thing> things)
    {
        int n = things.Count;
        if (cachedGridCells == null || cachedGridSnapshotCount != n)
        {
            return false;
        }
        for (int i = 0; i < n; i++)
        {
            if (cachedGridSnapshotThingIds[i] != things[i].thingIDNumber
                || cachedGridSnapshotStacks[i] != things[i].stackCount)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>用当前选中序列刷新快照（数组不足时扩容，避免每帧分配新数组）。</summary>
    private static void UpdateGridSnapshot(List<Thing> things)
    {
        int n = things.Count;
        if (cachedGridSnapshotThingIds.Length < n)
        {
            int newLength = Mathf.Max(8, n);
            cachedGridSnapshotThingIds = new int[newLength];
            cachedGridSnapshotStacks = new int[newLength];
        }
        for (int i = 0; i < n; i++)
        {
            cachedGridSnapshotThingIds[i] = things[i].thingIDNumber;
            cachedGridSnapshotStacks[i] = things[i].stackCount;
        }
        cachedGridSnapshotCount = n;
    }

    /// <summary>构建单成员单元格（Things：殖民者单列、或「单组展开」后的单项目：仅自身）。</summary>
    private static GridCell MakeSingleCell(Thing thing, bool isColonist)
    {
        return new GridCell
        {
            representative = thing,
            members = new List<object> { thing },
            isColonist = isColonist,
            isSingle = true
        };
    }

    #endregion

    #region 绘制

    /// <summary>
    /// 类型无关的统一绘制引擎：绘制多选对象网格（固定行数，超出容量时末尾一格显示 +N 提示）。
    /// 场景相关：
    ///  - Things：额外执行健康目标缓存刷新 / 每格健康条缓动 / 悬停地图指示（UpdateHoveredGridCell）。
    ///  - Zones / World：无健康、无地图指示，只绘制内容（色块/图标）+ 交互。
    /// 设置按钮在标题行右上角（见 PaneTitleSettingsButtonPatch），正文不预留页脚，网格铺满可用高度。
    /// </summary>
    private static void DrawSceneGrid(Rect rect, MultiSelectScene scene, List<GridCell> cells)
    {
        // 放开 Ctrl / Shift 后应用各自的待处理动作（网格每帧绘制，此处每帧检查一次）。
        ApplyPendingCtrlRemoval(scene);
        ApplyPendingShiftSelection(scene);

        Widgets.BeginGroup(rect);
        try
        {
            // (1) 名称分隔线：位于名称（标题）下方，与 InspectPanePatch 单选面板一致（可被「显示分隔条」开关控制）。
            float gridY = DrawSeparator(rect.width, rowSpacing) + rowSpacing;

            // 主内容可用高度：分隔线下方到面板底部，不预留任何按钮页脚，网格可铺满整段高度。
            float availableHeight = Mathf.Max(0f, rect.height - gridY);

            float cellSize = multiSelectGridCellSize;
            float gap = multiSelectCellGap;
            int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width + gap) / (cellSize + gap)));
            int effectiveRows = Mathf.Min(multiSelectGridRows,
                Mathf.Max(0, Mathf.FloorToInt((availableHeight + gap) / (cellSize + gap))));
            if (effectiveRows < 1)
            {
                effectiveRows = 1;
            }
            int capacity = columns * effectiveRows;

            // 网格背景：半透明深色底。默认铺满整个内容区（宽度到面板右缘、高度到内容区底部），
            // 与右上角按钮簇齐平，消除「列/行按 cellSize+gap 取整」在右侧与下侧遗留的空隙；
            // 「以实际网格绘制」开启后仅按 columns × effectiveRows 的实际网格范围绘制底框。
            Rect gridAreaRect = new Rect(0f, gridY, rect.width, availableHeight);
            Rect backgroundRect = gridAreaRect;
            if (multiSelectGridBackgroundExact)
            {
                float gridWidth = columns * (cellSize + gap) + gap;
                float gridHeight = effectiveRows * (cellSize + gap) + gap;
                backgroundRect = new Rect(0f, gridY, gridWidth, gridHeight);
            }
            Widgets.DrawBoxSolid(backgroundRect, new Color(0f, 0f, 0f, 0.25f));

            // 溢出：最后一个单元格让位给 +N 提示。
            bool overflow = cells.Count > capacity;
            int shown = overflow ? capacity - 1 : Mathf.Min(capacity, cells.Count);

            // 「额外选中指示」：每帧记录鼠标悬停的单元格成员（供地图指示箭头使用）。仅 Things 场景。
            if (scene == MultiSelectScene.Things)
            {
                UpdateHoveredGridCell(cells, shown, columns, cellSize, gap, gridY);
            }

            // 健康目标值按帧缓存：每 20 帧重算一次可见格的健康（仅 Things）。
            if (scene == MultiSelectScene.Things && Time.frameCount - cellHealthRefreshFrame >= CellHealthCacheFrames)
            {
                for (int i = 0; i < shown; i++)
                {
                    GridCell cell = cells[i];
                    cell.healthFill = GetCellHealthFill(cell);
                    cells[i] = cell;
                }
                cellHealthRefreshFrame = Time.frameCount;
            }

            // 单格异常隔离：一格绘制失败只跳过该格并记录日志，避免拖垮整个网格（也防止 GUI 状态残留）。
            for (int i = 0; i < shown; i++)
            {
                try
                {
                    DrawGridCell(CellRect(gridY, columns, i, cellSize, gap), cells[i], scene);
                }
                catch (Exception ex)
                {
                    Log.Error("BetterInspectPane: 绘制多选单元格失败，已跳过该格：" + ex);
                }
            }

            if (overflow)
            {
                try
                {
                    DrawMoreCell(CellRect(gridY, columns, capacity - 1, cellSize, gap), cells.Count - shown);
                }
                catch (Exception ex)
                {
                    Log.Error("BetterInspectPane: 绘制「+N 更多」溢出格失败：" + ex);
                }
            }

            // 清理多选健康条缓动：只保留当前可见单元格的代表物，避免切换选择后字典无限增长（仅 Things）。
            if (scene == MultiSelectScene.Things && cellHealthEase.Count > shown)
            {
                List<int>? stale = null;
                foreach (KeyValuePair<int, EaseState> kvp in cellHealthEase)
                {
                    bool keep = false;
                    for (int i = 0; i < shown; i++)
                    {
                        if (ObjUniqueId(cells[i].representative) == kvp.Key)
                        {
                            keep = true;
                            break;
                        }
                    }
                    if (!keep)
                    {
                        stale ??= new List<int>();
                        stale.Add(kvp.Key);
                    }
                }
                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        cellHealthEase.Remove(stale[i]);
                    }
                }
            }

            // 拖拽选择：按住 Shift / Ctrl 在网格内拖拽时批量框选 / 标记待移除（须在单元格绘制后调用）。
            try
            {
                HandleGridDrag(gridAreaRect, cells, shown, columns, cellSize, gap, gridY);
            }
            catch (Exception ex)
            {
                Log.Error("BetterInspectPane: 处理多选拖拽选择失败：" + ex);
            }
        }
        finally
        {
            Widgets.EndGroup();
        }
    }

    /// <summary>计算网格中第 index 格的位置（左右各内缩 gap，使背景框内右侧与左侧边距一致）。</summary>
    private static Rect CellRect(float gridY, int columns, int index, float cellSize, float gap)
    {
        int col = index % columns;
        int row = index / columns;
        return new Rect(gap + col * (cellSize + gap), gridY + row * (cellSize + gap), cellSize, cellSize);
    }

    /// <summary>每帧更新「鼠标悬停单元格」的成员列表（供地图指示箭头使用，仅 Things 场景）。
    /// 须在网格的 BeginGroup 内调用（Event.current.mousePosition 为群组局部坐标，与 CellRect 同坐标系）；
    /// 悬停到空白/非单元格区域时清空，「+N 更多」溢出格不参与。关闭该设置时也清空，避免残留。</summary>
    private static void UpdateHoveredGridCell(List<GridCell> cells, int shown,
        int columns, float cellSize, float gap, float gridY)
    {
        hoveredGridMembers.Clear();
        if (!enableMultiSelectMapIndicator)
        {
            return;
        }
        Vector2 mouse = Event.current.mousePosition;
        int idx = GridHitTest(mouse, shown, columns, cellSize, gap, gridY);
        if (idx < 0)
        {
            return; // 悬停在空白/非单元格区域（空隙/边距）：保持清空。
        }
        GridCell gc = cells[idx];
        for (int j = 0; j < gc.members.Count; j++)
        {
            object o = gc.members[j];
            if (o is Thing t && !t.Destroyed)
            {
                hoveredGridMembers.Add(t);
            }
        }
    }

    /// <summary>绘制单个单元格：按类型绘制内容 + shift/ctrl 高亮 + 边框 + 健康条（仅 Things）+ 数量角标 + 提示 + 点击热区。</summary>
    private static void DrawGridCell(Rect cell, GridCell gc, MultiSelectScene scene)
    {
        object rep = gc.representative;
        bool isShiftSelected = shiftSelectedThings.Contains(rep);
        bool isCtrlMarked = ctrlMarkedForRemoval.Contains(rep);

        // 健康值 + 每格缓动（仅 Things）：目标值 gc.healthFill 来自 20 帧缓存。
        // 提前到内容绘制之前计算：健康背景需画在内容之下，必须先拿到缓动值。
        float easedHealth = 0f;
        bool showHealth = false;
        if (scene == MultiSelectScene.Things && IsThingCell(gc))
        {
            float healthFill = gc.healthFill;
            int healthKey = ObjUniqueId(rep);
            if (!cellHealthEase.TryGetValue(healthKey, out EaseState ease))
            {
                ease.Set(healthFill);
            }
            else if (enableEaseEffect)
            {
                ease.DoEase(healthFill);
            }
            else
            {
                ease.Set(healthFill);
            }
            cellHealthEase[healthKey] = ease;
            easedHealth = ease.easedValue;
            showHealth = ShouldShowCellHealthBar(gc, healthFill);
        }

        // 健康背景（替代健康条时）：内容之下的最底层背景。
        // 默认按健康比例自下而上填充；反转后按受伤比例（1-健康）自上而下填充。
        if (showHealth && useHealthBackgroundInsteadOfBar)
        {
            DrawCellHealthBackground(cell, easedHealth);
        }

        if (Mouse.IsOver(cell))
        {
            Widgets.DrawBoxSolid(cell, new Color(1f, 1f, 1f, 0.4f));
        }

        // 背景高亮（置于内容之下，不遮挡立绘/图标）：shift 构建选区 = 淡黄，ctrl 待移除 = 橙色。
        if (isShiftSelected)
        {
            Widgets.DrawBoxSolid(cell, MultiSelectShiftFill);
        }
        else if (isCtrlMarked)
        {
            Widgets.DrawBoxSolid(cell, MultiSelectCtrlFill);
        }

        // 内容：Thing 立绘/图标；Zone 色块 + 标签；WorldObject 图标。代表物可能恰在被销毁的那一帧
        // （选择通常随后即被移除，这里仅防御绘制窗口）：销毁则跳过内容绘制，只保留边框/提示/点击。
        DrawCellContent(cell, gc);

        // 边框：shift 构建选区用黄色、ctrl 待移除用橙色，否则用默认色（颜色与粗细均为可调设置；
        // 粗细由设置处烘焙进边框纹理，绘制时不再传参）。
        int borderWidth = multiSelectCellBorderWidth;
        if (isShiftSelected)
        {
            DrawRectOutline(cell, MultiSelectShiftBorder);
        }
        else if (isCtrlMarked)
        {
            DrawRectOutline(cell, MultiSelectCtrlBorder);
        }
        else
        {
            DrawRectOutline(cell, multiSelectCellBorderColor);
        }

        // 健康条（仅 Things）：顶部横条，未启用「健康背景替代」时绘制（缓动值已在方法开头计算）。
        if (showHealth && !useHealthBackgroundInsteadOfBar)
        {
            DrawCellHealthBar(cell, easedHealth);
        }

        // 数量角标：非殖民者单元格右下角 ×N（带半透明底，保证可读性；可关闭「×」前缀只显示数字）。
        // 数量 ≥ 1000 时以 k 缩写显示（如 ×1.2k），宽度按角标文本实测并缓存，保持右对齐并关闭换行。
        if (!gc.isColonist && gc.count > 1)
        {
            bool showX = multiSelectBadgeShowX;
            string digits = FormatCountText(gc.count);
            string countText = showX ? "×" + digits : digits;
            // WordWrap / Anchor / Font 均用作用域对象管理，异常时也能还原，防止 GUI 文本状态泄漏。
            using (new WordWrapScope(false))
            using (new FontSizeScope(multiSelectBadgeFontSize))
            using (new AnchorScope(TextAnchor.MiddleRight))
            {
                // 宽度按数字字符数查表（假定 '.' 与 'k' 与数字等宽，同长度宽度一致），
                // 每帧无需测量实际文本；开启「×」前缀时再把「×」字形宽度加回。角标框整体再乘以缩放比例设置。
                float scale = multiSelectBadgeScale;
                float badgeWidth = MultiSelectBadgeWidthFor(digits.Length) * scale;
                if (showX)
                {
                    badgeWidth += MultiSelectBadgeXWidth() * scale;
                }
                float badgeHeight = MultiSelectBadgeHeight * scale;
                // 右、下两侧各向格内收缩一个边框宽度，避免灰色底盖住单元格边框。
                Rect badge = new Rect(cell.xMax - borderWidth - badgeWidth, cell.yMax - borderWidth - badgeHeight,
                    badgeWidth, badgeHeight);
                Widgets.DrawBoxSolid(badge, new Color(0f, 0f, 0f, 0.6f));
                Widgets.Label(badge, countText);
            }
        }

        // 工具提示：单个项单元格（殖民者单列 / 单项目）→ 个人标签；分组 → 种类名 ×数量。
        // 用 Func 重载延迟求值：仅悬停显示提示时才拼接文本，避免每帧为所有单元格计算。
        // uniqueId 取代表物的稳定键，跨帧稳定，提示不会因网格重建而闪烁。
        TooltipHandler.TipRegion(cell,
            gc.isSingle
                ? (Func<string>)(() => PersonalLabel(gc.representative))
                : () => "BetterInspectPane.MultiSelectGroupTip".Translate(GetGroupLabel(gc), gc.count),
            ObjUniqueId(gc.representative));

        // 点击（须在绘制内容之后调用）。拖拽进行中/本次手势已发生拖拽时忽略松手的 MouseUp，
        // 避免拖拽结束恰好落在起始格（或某格）上被误判为一次切换点击：
        //   - GestureIsDrag()：已进入拖拽状态（dragging / dragPerformed）；
        //   - 位置判定：本次在网格内按下且松手时相对起点已超过拖拽阈值（覆盖快速拖拽未被逐帧识别）。
        // 左键与右键统一用 Event 判定（MouseUp），不再依赖不可见按钮热区；
        // mousePosition 为群组局部坐标，须与 CellRect 同坐标系；左键/右键的具体功能（多选/单选/多重取消/取消）
        // 由输入键映射（「使用 Shift 键多选 / 使用 Ctrl 键多重取消」设置）决定，见 HandleCellClick / HandleCellRightClick。
        bool gestureIsDrag = multiSelectDrag.GestureIsDrag(Event.current.mousePosition);
        if (!gestureIsDrag && Event.current.type == EventType.MouseUp
            && cell.Contains(Event.current.mousePosition))
        {
            if (Event.current.button == 0)
            {
                HandleCellClick(scene, gc);
            }
            else if (Event.current.button == 1)
            {
                // shift/ctrl 拖拽进行中，右键作为反向移出/拖拽的一部分，不触发单选移除（如右键取消该组）。
                if (!MultiSelectModifierDragActive())
                {
                    HandleCellRightClick(scene, gc);
                }
            }
        }
    }

    /// <summary>该格是否为 Thing 单元格（代表物为 Thing；决定是否绘制健康条）。</summary>
    private static bool IsThingCell(GridCell gc) => gc.representative is Thing;

    /// <summary>单元格内容分发：Thing 立绘/图标；Zone 色块 + 居中标签；WorldObject 图标（null 回退标签）。</summary>
    private static void DrawCellContent(Rect cell, GridCell gc)
    {
        object rep = gc.representative;
        if (ObjDestroyed(rep))
        {
            return; // 销毁则跳过内容（边框/提示/点击保留）。
        }
        if (rep is Thing t)
        {
            if (t is Pawn pawn)
            {
                RenderTexture tex = PortraitsCache.Get(pawn, new Vector2(cell.width, cell.height), Rot4.South);
                if (tex != null)
                {
                    GUI.DrawTexture(cell, tex);
                }
            }
            else
            {
                Widgets.ThingIcon(cell, t);
            }
        }
        else if (rep is Zone zone)
        {
            // 用区域自身颜色填充底衬 + 居中显示区域标签的前三个字（字号沿用数量角标字号）。
            Color c = zone.color;
            Widgets.DrawBoxSolid(cell, new Color(c.r, c.g, c.b, 0.45f));
            string initial = zone.InspectLabel;
            if (string.IsNullOrEmpty(initial))
            {
                return; // 空标签无内容可画（并防止下方取末位越界）。
            }
            char last = initial[initial.Length - 1];
            bool singleDigitEnd = last >= '0' && last <= '9'
                                  && (initial.Length < 2 || initial[initial.Length - 2] < '0' || initial[initial.Length - 2] > '9');
            bool twoDigitEnd = !singleDigitEnd
                               && initial.Length >= 2
                               && last >= '0' && last <= '9'
                               && initial[initial.Length - 2] >= '0' && initial[initial.Length - 2] <= '9'
                               && (initial.Length < 3 || initial[initial.Length - 3] < '0' || initial[initial.Length - 3] > '9');
            int labelLen = twoDigitEnd ? 2 : singleDigitEnd ? 1 : 0;
            if (labelLen > 0)
            {
                // 单数字结尾（如“种植区 1”）取前二字+数字（种植1）；双数字结尾（如“种植区 11”）取前一字+两数字（种11）。
                int keep = Math.Max(0, 3 - labelLen);
                if (keep > 0 && keep >= initial.Length - labelLen) keep = initial.Length - labelLen;
                initial = initial.Substring(0, keep) + initial.Substring(initial.Length - labelLen, labelLen);
            }
            else if (initial.Length > 3)
            {
                initial = initial.Substring(0, 3);
            }
            using (new WordWrapScope(false))
            using (new FontSizeScope(multiSelectBadgeFontSize))
            using (new AnchorScope(TextAnchor.MiddleCenter))
            {
                Widgets.Label(cell, initial);
            }
        }
        else if (rep is WorldObject wo)
        {
            Texture2D icon = wo.ExpandingIcon;
            if (icon != null)
            {
                Color prev = GUI.color;
                GUI.color = wo.ExpandingIconColor;
                GUI.DrawTexture(cell, icon);
                GUI.color = prev;
            }
            else
            {
                using (new WordWrapScope(false))
                using (new FontSizeScope(multiSelectBadgeFontSize))
                using (new AnchorScope(TextAnchor.MiddleCenter))
                {
                    Widgets.Label(cell, wo.LabelShortCap);
                }
            }
        }
    }

    /// <summary>绘制指定颜色的边框：从设置处烘焙好的「白色边框、内部透明」纹理按 GUI.color 染色一次绘制完成
    /// （替代每格四段 DrawBoxSolid 的开销）。尺寸/边框宽度由设置决定，本方法不参与烘焙判断。</summary>
    private static void DrawRectOutline(Rect r, Color color)
    {
        if (multiSelectBorderTex == null)
        {
            RebakeMultiSelectBorderTexture(); // 首次兜底（正常情况设置加载/更改时已烘焙）
        }
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(r, multiSelectBorderTex, ScaleMode.StretchToFill);
        GUI.color = prev;
    }

    /// <summary>按当前设置（单元格尺寸 / 边框宽度）重新烘焙「白色边框、内部透明」的纹理；设置处变化时调用，未变化则跳过。</summary>
    public static void RebakeMultiSelectBorderTexture()
    {
        int size = Mathf.Max(1, Mathf.RoundToInt(multiSelectGridCellSize));
        int thickness = Mathf.Clamp(multiSelectCellBorderWidth, 1, size / 2);
        if (multiSelectBorderTex != null && multiSelectBorderTexSize == size && multiSelectBorderTexWidth == thickness)
        {
            return;
        }
        if (multiSelectBorderTex != null)
        {
            UnityEngine.Object.Destroy(multiSelectBorderTex);
        }
        multiSelectBorderTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[size * size];
        Color white = Color.white;
        // 默认全透明，再填充四边为白色（thickness 为边框宽度）。
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(0f, 0f, 0f, 0f);
        }
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < thickness; y++)
            {
                pixels[y * size + x] = white;               // 上边
                pixels[(size - 1 - y) * size + x] = white;  // 下边
            }
        }
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < thickness; x++)
            {
                pixels[y * size + x] = white;               // 左边
                pixels[y * size + (size - 1 - x)] = white;  // 右边
            }
        }
        multiSelectBorderTex.SetPixels(pixels);
        multiSelectBorderTex.Apply();
        multiSelectBorderTexSize = size;
        multiSelectBorderTexWidth = thickness;
    }

    /// <summary>分组单元格的提示名（或单个单元格的标签）：Thing 用 PawnKindDef/ThingDef.label，Zone 用 InspectLabel，World 用 LabelCap。</summary>
    private static string GetGroupLabel(GridCell gc)
    {
        object rep = gc.representative;
        if (rep is Pawn pawn)
        {
            return pawn.kindDef.label.CapitalizeFirst();
        }
        if (rep is Thing t)
        {
            return t.def.label.CapitalizeFirst();
        }
        if (rep is Zone z)
        {
            return z.InspectLabel.CapitalizeFirst();
        }
        if (rep is WorldObject w)
        {
            return w.LabelCap;
        }
        return "";
    }

    /// <summary>单个项单元格的个人标签（殖民者立绘 / 单区域 / 单世界对象）。</summary>
    private static string PersonalLabel(object o) => o switch
    {
        Thing t => t.LabelCap,
        Zone z => z.InspectLabel.CapitalizeFirst(),
        WorldObject w => w.LabelCap,
        _ => o?.ToString() ?? ""
    };

    /// <summary>取角标纯数字文本宽度：按数字字符数缓存（假定 '.' 与 'k' 与数字等宽，同长度宽度一致），
    /// 同长度只测量一次，避免每帧重复 Text.CalcSize。不包含「×」前缀宽度（由调用方按 multiSelectBadgeShowX 决定是否加回）。
    /// 字号设置变化时清空缓存重新测量。</summary>
    private static float MultiSelectBadgeWidthFor(int charCount)
    {
        int fontSize = multiSelectBadgeFontSize;
        if (fontSize != multiSelectBadgeWidthCacheFontSize)
        {
            Array.Clear(multiSelectBadgeWidthCached, 0, multiSelectBadgeWidthCached.Length);
            multiSelectBadgeWidthCacheFontSize = fontSize;
        }
        if (charCount < 0 || charCount >= multiSelectBadgeWidthCache.Length)
        {
            // 超长兜底：直接测量（k 缩写后实际字符数 ≤ 10，正常不会走到这里）。
            return Mathf.Max(MultiSelectBadgeWidth, Text.CalcSize(new string('0', charCount)).x);
        }
        if (!multiSelectBadgeWidthCached[charCount])
        {
            multiSelectBadgeWidthCache[charCount] = Text.CalcSize(new string('0', charCount)).x;
            multiSelectBadgeWidthCached[charCount] = true;
        }
        return Mathf.Max(MultiSelectBadgeWidth, multiSelectBadgeWidthCache[charCount]);
    }

    /// <summary>取「×」前缀字形宽度：仅随字号变化失效，避免每帧每格对同一字形重复 Text.CalcSize。</summary>
    private static float MultiSelectBadgeXWidth()
    {
        int fontSize = multiSelectBadgeFontSize;
        if (multiSelectBadgeXWidthFontSize != fontSize)
        {
            multiSelectBadgeXWidth = Text.CalcSize("×").x;
            multiSelectBadgeXWidthFontSize = fontSize;
        }
        return multiSelectBadgeXWidth;
    }

    /// <summary>数量文本：开启 k 缩写且 ≥ 1000 时以 k 缩写（1000→"1k"，1234→"1.2k"，12345→"12.3k"），
    /// 否则显示完整数字。</summary>
    private static string FormatCountText(int count)
    {
        if (count < 1000 || !multiSelectCountAbbreviation)
        {
            return count.ToString();
        }
        return (count / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
    }

    /// <summary>绘制「+N」溢出提示格：灰化、不响应点击。</summary>
    private static void DrawMoreCell(Rect cell, int moreCount)
    {
        if (Mouse.IsOver(cell))
        {
            Widgets.DrawHighlight(cell);
        }
        Widgets.DrawBoxSolid(cell, new Color(0f, 0f, 0f, 0.4f));
        Widgets.DrawBox(cell, multiSelectCellBorderWidth);
        Color prevColor = GUI.color;
        GUI.color = Color.gray;
        try
        {
            using (new FontSizeScope(MultiSelectMoreFontSize))
            using (new AnchorScope(TextAnchor.MiddleCenter))
            {
                Widgets.Label(cell, "+" + moreCount);
            }
        }
        finally
        {
            GUI.color = prevColor;
        }
        TooltipHandler.TipRegion(cell, "BetterInspectPane.MultiSelectMore".Translate(moreCount));
    }

    /// <summary>单个 Thing 的健康填充（0..1）：Pawn 用总体健康比例，非 Pawn 用 HitPoints / MaxHitPoints。</summary>
    private static float GetThingHealthFill(Thing t)
    {
        if (t is Pawn pawn)
        {
            return Mathf.Clamp01(pawn.health.summaryHealth.SummaryHealthPercent);
        }
        if (t is { def: { useHitPoints: true } } && t.MaxHitPoints > 0)
        {
            return Mathf.Clamp01(t.HitPoints / (float)t.MaxHitPoints);
        }
        return 0f;
    }

    /// <summary>单元格健康填充：单格取自身，分组取成员平均（仅 Things 使用）。</summary>
    private static float GetCellHealthFill(GridCell gc)
    {
        if (gc.isSingle)
        {
            return gc.representative is Thing repThing ? GetThingHealthFill(repThing) : 0f;
        }
        float sum = 0f;
        int n = 0;
        for (int i = 0; i < gc.members.Count; i++)
        {
            if (gc.members[i] is Thing t && !t.Destroyed)
            {
                sum += GetThingHealthFill(t);
                n++;
            }
        }
        return n > 0 ? Mathf.Clamp01(sum / n) : 0f;
    }

    /// <summary>该物是否使用健康值：Pawn 恒为真，非 Pawn 需 def.useHitPoints 且存在上限。</summary>
    private static bool ThingUsesHealth(Thing t)
    {
        if (t is Pawn)
        {
            return true;
        }
        return t.def.useHitPoints && t.MaxHitPoints > 0;
    }

    /// <summary>该格是否绘制健康条：对象使用健康值 + 总开关开启，且未被「满血隐藏」规则隐藏（按目标健康而非缓动值判定）。</summary>
    private static bool ShouldShowCellHealthBar(GridCell gc, float targetFill)
    {
        if (gc.representative is not Thing thing || !ThingUsesHealth(thing))
        {
            return false; // 不使用健康值的物品（如纯标志物）不显示健康条。
        }
        if (!enableMultiSelectHealthBar)
        {
            return false;
        }
        bool isPawn = gc.representative is Pawn;
        bool hideWhenFull = isPawn
            ? hideMultiSelectFullHealthBarPawn
            : hideMultiSelectFullHealthBarNonPawn;
        if (hideWhenFull && targetFill >= 0.999f)
        {
            return false;
        }
        return true;
    }

    /// <summary>绘制单元格顶部健康条：四边内缩边框宽度（不与边框或底部 ×N 角标重合），复用通用健康分级色。</summary>
    private static void DrawCellHealthBar(Rect cell, float easedFill)
    {
        int border = Mathf.Max(1, multiSelectCellBorderWidth);
        float h = multiSelectHealthBarHeight;
        if (h <= 0f)
        {
            return;
        }
        // 顶部、左右下内缩边框，避免与单元格边框或底部 ×N 数量角标重合。
        Rect bar = new Rect(cell.x + border, cell.y + border, cell.width - 2f * border, h);

        Widgets.DrawBoxSolid(bar, emptyColor);
        DrawBarFill(bar, easedFill, GetHealthColor(easedFill));
    }

    /// <summary>绘制单元格健康背景：在内容之下按比例填充半透明色块（类似垂直健康条但不绘制背景轨道）。
    /// 默认锚定下端、按健康比例自下而上填充；开启「反转健康背景比例」后锚定上端、按受伤比例（1-健康）自上而下填充。
    /// 颜色用 healthBackgroundColor。</summary>
    private static void DrawCellHealthBackground(Rect cell, float easedFill)
    {
        int border = Mathf.Max(1, multiSelectCellBorderWidth);
        // 四周内缩边框宽度，避免与单元格边框重合（与顶部健康条一致）。
        Rect area = new Rect(cell.x + border, cell.y + border, cell.width - 2f * border, cell.height - 2f * border);
        float fill = Mathf.Clamp01(easedFill);
        if (invertHealthBackground)
        {
            fill = 1f - fill; // 反转：按受伤比例填充（满血无背景，受伤越重填充越多）。
        }
        float fillPx = area.height * fill;
        if (fillPx <= 0f)
        {
            return;
        }
        Rect fillRect = invertHealthBackground
            ? new Rect(area.x, area.y, area.width, fillPx)              // 反转：锚定上端，自上而下扩展
            : new Rect(area.x, area.yMax - fillPx, area.width, fillPx); // 默认：锚定下端，自下而上扩展
        Widgets.DrawBoxSolid(fillRect, healthBackgroundColor);
    }

    #endregion

    #region 点击交互

    // 拖拽选择状态：默认左键拖拽批量加入构建选区（多选 = 移除未被选择的项）、右键拖拽批量标记待移除；
    // 开启「使用 Shift 键多选 / 使用 Ctrl 键多重取消」后改由 Shift / Ctrl + 左键拖拽（恢复旧行为）。
    // 用「移动超过阈值」区分点击（原地释放 → 走切换）与拖拽（跨过阈值 → 只累加）；拖拽期间忽略点击。
    // 拖拽方向由起始单元格决定：若起始单元已在目标集合中，本次拖拽从集合中移除；否则加入。
    private const float MultiSelectDragThreshold = 4f;

    // 主拖拽类型：拖拽多选与拖拽多重取消不能同时进行，只以最先触发的为主拖拽。
    // 一旦选定，后续按住另一触发键也不再启动各自的拖拽，只作为「反向移出」修饰（见 HandleGridDrag）。
    private enum MultiSelectDragKind { None, Build, Removal }

    /// <summary>
    /// 一次「按下→移动→松开」手势中的拖拽状态机（鼠标拖拽选择用）。
    /// 以结构体打包原先散落的 8 个静态字段：Reset 一键清理、Begin 统一手势开端、GestureIsDrag 收敛点击判定。
    /// </summary>
    private struct MultiSelectDragGesture
    {
        // 主拖拽类型：拖拽多选与拖拽多重取消不能同时进行，只以最先触发的为主拖拽；
        // 一旦选定，后续按住另一触发键也不再启动各自的拖拽，只作为「反向移出」修饰（见 HandleGridDrag）。
        public MultiSelectDragKind primary;
        // 已跨过移动阈值进入拖拽（跨过阈值 → 只累加，拖拽期间忽略点击）。
        public bool dragging;
        // 本次按下手势内是否已发生拖拽：跨过阈值即置位，直到下一次按下才清除。
        // 用于抑制拖拽松手那帧的 MouseUp 被误判为一次点击（dragging 可能在点击判定前被重置，
        // 例如同一帧内网格被多次绘制时），否则松手若落在某格上会产生一次意外的单选/单独取消。
        public bool dragPerformed;
        // 本次按下手势是否在网格内容区内按下（此时 startPos 有效）：用于在松手帧按
        // 「起点与当前鼠标距离是否超过阈值」判定本次手势是点击还是拖拽，覆盖快速拖拽（未经过按住帧、
        // 阈值跨越未被逐帧检测到）被误判为点击的情况。
        public bool gestureActive;
        // 手势起点（网格内容区内按下时记录）。
        public Vector2 startPos;
        // 本次拖拽中刚被「反向移出」的单元格代表物：在鼠标按键按住期间、光标仍停留该格时，
        // 禁止该格被重新加入集合，直到光标移出该格（防止「移出后立刻又被加回」的闪烁）。
        public object? lockedRep;
        // 本次拖拽方向（多选 / 多重取消）：由起始单元格的集合归属决定，true=加入，false=移除。
        public bool shiftAdds;
        public bool ctrlAdds;

        public void Reset()
        {
            primary = MultiSelectDragKind.None;
            dragging = false;
            dragPerformed = false;
            gestureActive = false;
            startPos = default;
            lockedRep = null;
            shiftAdds = true;
            ctrlAdds = true;
        }

        /// <summary>手势开端：重置本次手势状态；仅当按下点在网格内容区内且任一拖拽键激活时记录起点。
        /// 主拖拽进行中再按另一键不重置，避免打断拖拽（调用方只在无主拖拽时调用）。</summary>
        public void Begin(Vector2 pos, bool active)
        {
            dragging = false;
            dragPerformed = false;
            gestureActive = active;
            lockedRep = null;
            if (active)
            {
                startPos = pos;
            }
        }

        /// <summary>本次手势当前是否应视为拖拽（用于抑制拖拽松手那帧的 MouseUp 被误判为一次点击）：
        /// dragging / dragPerformed 表示已进入拖拽状态；gestureActive 兜底快速拖拽未被逐帧识别
        /// （按下在网格内且移动超过阈值即算拖拽，覆盖未经过按住帧的情况）。</summary>
        public bool GestureIsDrag(Vector2 mouse) => dragging
            || dragPerformed
            || (gestureActive && (mouse - startPos).magnitude > MultiSelectDragThreshold);
    }

    /// <summary>
    /// 处理网格内拖拽选择：把扫过的单元格批量加入「构建选区」（多选 = 放开后保留被选中的、移除未被选择的项）
    /// 或标记「待移除」（多重取消），放开触发输入后由 ApplyPending* 统一应用。拖拽输入跟随输入键映射（默认）：
    ///   多选：开启「使用 Shift 键多选」= 按住 Shift 拖左键或右键；关闭（默认）= 普通左键拖拽。
    ///   多重取消：开启「使用 Ctrl 键多重取消」= 按住 Ctrl 拖左键或右键；关闭（默认）= 右键拖拽。
    /// 拖拽多选与拖拽多重取消不能同时进行，只以最先触发的为主拖拽（multiSelectDrag.primary）。
    /// 先按下的拖拽键抢先成为唯一主拖拽；此后即便按住另一触发键，也不再启动它的独立拖拽。
    /// 用「鼠标移动超过阈值」区分点击（原地释放 → DrawGridCell 走切换）与拖拽（进入后不再触发点击切换）。
    /// 拖拽方向由起始单元格决定：若起始单元已在目标集合中，本次拖拽把这些单元从集合中移除；否则加入。
    /// 主拖拽期间同时按住另一个物理鼠标键会成为「反向移出」：扫过的项目不再加入、而是移出主集合
    /// （默认与 shift/ctrl 布局一致，如用左键触发 ctrl 拖拽时按右键 → 移出该选择集合），
    /// 被移出的格子会在按键按住、光标未移离时锁定（multiSelectDrag.lockedRep），阻止其立刻被重新加入。
    /// 鼠标位置取群组局部坐标，与单元格矩形同坐标系（见 Mouse.IsOver 的实现）。
    /// </summary>
    private static void HandleGridDrag(Rect gridRect, List<GridCell> cells, int shown,
        int columns, float cellSize, float gap, float gridY)
    {
        Vector2 mouse = Event.current.mousePosition;
        DragInputs inputs = ComputeDragInputs();
        bool anyDown = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);

        // 手势开端重置：仅在当前尚无主拖拽的手势时（上一手势全部松开后）才重新开始，
        // 记录手势起点供「移动超过阈值即视为拖拽」判定；主拖拽进行中再按另一键不重置，避免打断拖拽。
        if (multiSelectDrag.primary == MultiSelectDragKind.None && anyDown)
        {
            multiSelectDrag.Begin(mouse, gridRect.Contains(mouse) && inputs.Any);
        }

        // 选定/释放主拖拽：触发键松开即回落 None，可重新选择（正常松开全部键后自然回落）。
        UpdatePrimaryDrag(inputs);

        // 主拖拽期间同时按住另一物理按键 → 反向「移出」主集合（默认与 shift/ctrl 布局行为一致）。
        bool bothButtons = Input.GetMouseButton(0) && Input.GetMouseButton(1);
        bool eraseDuringBuild = multiSelectDrag.primary == MultiSelectDragKind.Build && bothButtons;
        bool eraseDuringRemoval = multiSelectDrag.primary == MultiSelectDragKind.Removal && bothButtons;

        // 无任何拖拽触发键按住 → 结束本轮拖拽（顺带释放「反向移出」锁定）。
        if (!inputs.Any)
        {
            multiSelectDrag.dragging = false;
            multiSelectDrag.lockedRep = null;
            return;
        }

        // 跨过移动阈值进入拖拽；仍由起始单元格的标记状态决定本次拖拽「加入」还是「移除」（反向移出时忽略该方向、直接移除）。
        if (!multiSelectDrag.dragging && (mouse - multiSelectDrag.startPos).magnitude > MultiSelectDragThreshold)
        {
            multiSelectDrag.dragging = true;
            multiSelectDrag.dragPerformed = true;
            object? startRep = FindCellRepresentative(cells, shown, columns, cellSize, gap, gridY, multiSelectDrag.startPos);
            multiSelectDrag.shiftAdds = startRep == null || !shiftSelectedThings.Contains(startRep);
            multiSelectDrag.ctrlAdds = startRep == null || !ctrlMarkedForRemoval.Contains(startRep);
        }
        if (!multiSelectDrag.dragging)
        {
            return;
        }

        // 逐格累加 / 反向移出。
        ApplyDragToCell(mouse, cells, shown, columns, cellSize, gap, gridY, eraseDuringBuild, eraseDuringRemoval);
    }

    /// <summary>本次拖拽触发的按键输入快照（Build/Removal 各自激活态），由 ComputeDragInputs 一次算出。</summary>
    private readonly struct DragInputs
    {
        public readonly bool buildActive;
        public readonly bool removalActive;

        public DragInputs(bool buildActive, bool removalActive)
        {
            this.buildActive = buildActive;
            this.removalActive = removalActive;
        }

        /// <summary>是否有任一拖拽键激活。</summary>
        public bool Any => buildActive || removalActive;
    }

    /// <summary>计算本次拖拽的按键输入（跟随输入键映射，默认）。
    /// 多选：shift 布局可用 Shift+左键或 Shift+右键；默认用普通左键（排除 Shift/Ctrl）。
    /// 多重取消：ctrl 布局可用 Ctrl+左键或 Ctrl+右键；默认用普通右键。</summary>
    private static DragInputs ComputeDragInputs()
    {
        bool useShift = MultiSelectTriggerIsShift;
        bool useCtrl = MultiCancelTriggerIsCtrl;
        bool leftHeld = Input.GetMouseButton(0);
        bool rightHeld = Input.GetMouseButton(1);
        bool buildActive = useShift
            ? (leftHeld || rightHeld) && MultiSelectShiftHeld()
            : leftHeld && !MultiSelectShiftHeld() && !MultiSelectCtrlHeld();
        bool removalActive = useCtrl
            ? (leftHeld || rightHeld) && MultiSelectCtrlHeld()
            : rightHeld;
        return new DragInputs(buildActive, removalActive);
    }

    /// <summary>主拖拽选定/释放：拖拽多选与拖拽多重取消不并排进行，只以最先触发者为主；
    /// 触发键松开即回到 None，可重新选择（同帧同时成立时以多选优先）。</summary>
    private static void UpdatePrimaryDrag(DragInputs inputs)
    {
        if (multiSelectDrag.primary == MultiSelectDragKind.Build && !inputs.buildActive)
        {
            multiSelectDrag.primary = MultiSelectDragKind.None;
        }
        else if (multiSelectDrag.primary == MultiSelectDragKind.Removal && !inputs.removalActive)
        {
            multiSelectDrag.primary = MultiSelectDragKind.None;
        }
        if (multiSelectDrag.primary != MultiSelectDragKind.None)
        {
            return;
        }
        if (inputs.buildActive && !inputs.removalActive)
        {
            multiSelectDrag.primary = MultiSelectDragKind.Build;
        }
        else if (inputs.removalActive && !inputs.buildActive)
        {
            multiSelectDrag.primary = MultiSelectDragKind.Removal;
        }
        else if (inputs.buildActive && inputs.removalActive)
        {
            multiSelectDrag.primary = MultiSelectDragKind.Build; // 极端：同帧同时成立，以多选优先。
        }
    }

    /// <summary>把当前鼠标所在的单元格应用到主拖拽集合：
    /// 按本次拖拽方向加入/移除；主拖拽期间另一键按住（反向移出）则直接移出主集合并锁定该格
    /// （光标未移离前禁止重新加入）。光标不在任何单元格内（空隙/边距）则无操作。</summary>
    private static void ApplyDragToCell(Vector2 mouse, List<GridCell> cells, int shown,
        int columns, float cellSize, float gap, float gridY, bool eraseDuringBuild, bool eraseDuringRemoval)
    {
        int idx = GridHitTest(mouse, shown, columns, cellSize, gap, gridY);
        if (idx < 0)
        {
            return;
        }
        GridCell cell = cells[idx];
        object curRep = cell.representative;

        // 光标已移出此前被「反向移出」锁定的格子 → 释放锁定（下次扫到可再次加入）。
        if (curRep != multiSelectDrag.lockedRep)
        {
            multiSelectDrag.lockedRep = null;
        }
        // 该格是否为刚被反向移出、且按键按住期间光标仍未移离：若是则禁止重新加入集合。
        bool lockedOut = curRep == multiSelectDrag.lockedRep;

        // 反向移出与常规累加互斥：主拖拽期间另一键按住时，扫过即移出主集合，不再累加。
        if (eraseDuringBuild)
        {
            RemoveSetMembers(shiftSelectedThings, cell);
            multiSelectDrag.lockedRep = curRep; // 记录本格被移出：光标未离开前勿重新加入
        }
        else if (multiSelectDrag.primary == MultiSelectDragKind.Build)
        {
            ApplyDragDirection(multiSelectDrag.shiftAdds, lockedOut, shiftSelectedThings, cell);
        }
        if (eraseDuringRemoval)
        {
            RemoveSetMembers(ctrlMarkedForRemoval, cell);
            multiSelectDrag.lockedRep = curRep; // 记录本格被移出：光标未离开前勿重新加入
        }
        else if (multiSelectDrag.primary == MultiSelectDragKind.Removal)
        {
            ApplyDragDirection(multiSelectDrag.ctrlAdds, lockedOut, ctrlMarkedForRemoval, cell);
        }
    }

    /// <summary>按本次拖拽方向把整组加入/移出目标集合；反向移出锁定中的格禁止重新加入（移除方向幂等放行）。</summary>
    private static void ApplyDragDirection(bool adds, bool lockedOut, HashSet<object> set, GridCell cell)
    {
        if (adds)
        {
            if (!lockedOut)
            {
                AddSetMembers(set, cell);
            }
        }
        else
        {
            RemoveSetMembers(set, cell);
        }
    }

    /// <summary>网格命中测试：返回包含 pos 的可见单元格下标；不在任何单元格内（如空隙/边距）返回 -1。
    /// 统一悬停指示 / 拖拽累加 / 起点定位处的逐格 Contains 遍历。</summary>
    private static int GridHitTest(Vector2 pos, int shown, int columns, float cellSize, float gap, float gridY)
    {
        for (int i = 0; i < shown; i++)
        {
            if (CellRect(gridY, columns, i, cellSize, gap).Contains(pos))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>找到包含指定位置的可见单元格的代表物；不在任何单元格内（如空隙/边距）返回 null。</summary>
    private static object? FindCellRepresentative(List<GridCell> cells, int shown,
        int columns, float cellSize, float gap, float gridY, Vector2 pos)
    {
        int idx = GridHitTest(pos, shown, columns, cellSize, gap, gridY);
        return idx >= 0 ? cells[idx].representative : null;
    }

    /// <summary>Ctrl 是否按住（原版 Selector 仅暴露 ShiftIsHeld，Ctrl 用输入键判定）。</summary>
    private static bool MultiSelectCtrlHeld()
    {
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }

    /// <summary>Shift 是否按住（与 Selector.ShiftIsHeld 等价，正文内统一起见用输入键判定）。</summary>
    private static bool MultiSelectShiftHeld()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    /// <summary>多选当前由哪个输入触发：开启「使用 Shift 键多选」= Shift 键；关闭（默认）= 普通左键。</summary>
    private static bool MultiSelectTriggerIsShift => useShiftKeyForMultiSelect;

    /// <summary>多重取消当前由哪个输入触发：开启「使用 Ctrl 键多重取消」= Ctrl 键；关闭（默认）= 普通右键。</summary>
    private static bool MultiCancelTriggerIsCtrl => useCtrlKeyForMultiCancel;

    /// <summary>
    /// 当前是否正处于 shift/ctrl 拖拽（换键布局中的修饰键拖拽）过程。
    /// shift 布局多选 → 按住 Shift 确立为主拖拽；ctrl 布局多重取消 → 按住 Ctrl 确立为主拖拽。
    /// 此时右键作为反向移出/拖拽触发的一部分，不再触发「单选移除」（如 ctrl 布局下的右键取消该组）。
    /// 判定同时覆盖两类时机：
    ///  - 主拖拽已确立（multiSelectDrag.primary == Build/Removal）：拖拽进行中，即使修饰键先松开也不误触；
    ///  - 修饰键按住：拖拽正要开始 / 同帧快速按下松开时 primary 尚未在 HandleGridDrag 建立，
    ///    若不拦截右键 MouseUp 会误触「单选移除」。
    /// </summary>
    private static bool MultiSelectModifierDragActive()
    {
        // shift 布局：主拖拽为 Build，或 Shift 按住期间（右键属于 shift 拖拽手势）都视为 shift 拖拽。
        if (useShiftKeyForMultiSelect)
        {
            return multiSelectDrag.primary == MultiSelectDragKind.Build || MultiSelectShiftHeld();
        }
        // ctrl 布局：主拖拽为 Removal，或 Ctrl 按住期间（右键属于 ctrl 拖拽手势）都视为 ctrl 拖拽。
        if (useCtrlKeyForMultiCancel)
        {
            return multiSelectDrag.primary == MultiSelectDragKind.Removal || MultiSelectCtrlHeld();
        }
        return false;
    }

    /// <summary>
    /// 多选待应用集合当前是否可应用：开启「使用 Shift 键多选」时放开 Shift 应用（旧行为）；
    /// 关闭（默认）时由左键触发多选，放开鼠标左键即应用（单击 = 单选效果，拖拽 = 多选）。
    /// </summary>
    private static bool MultiSelectApplyReady()
    {
        return MultiSelectTriggerIsShift ? !MultiSelectShiftHeld() : !Input.GetMouseButton(0);
    }

    /// <summary>
    /// 多重取消待应用集合当前是否可应用：开启「使用 Ctrl 键多重取消」时放开 Ctrl 应用（旧行为）；
    /// 关闭（默认）时由右键触发多重取消，放开鼠标右键即应用（单击 = 取消该组，拖拽 = 多重取消）。
    /// </summary>
    private static bool MultiCancelApplyReady()
    {
        return MultiCancelTriggerIsCtrl ? !MultiSelectCtrlHeld() : !Input.GetMouseButton(1);
    }

    /// <summary>把组内未销毁成员全部加入目标集合（拖拽累加用，幂等，不会移除已存在的成员）。</summary>
    private static void AddSetMembers(HashSet<object> set, GridCell gc)
    {
        for (int i = 0; i < gc.members.Count; i++)
        {
            object o = gc.members[i];
            if (!ObjDestroyed(o))
            {
                set.Add(o);
            }
        }
    }

    /// <summary>把组内未销毁成员全部从目标集合中移除（拖拽反向用，幂等）。</summary>
    private static void RemoveSetMembers(HashSet<object> set, GridCell gc)
    {
        for (int i = 0; i < gc.members.Count; i++)
        {
            object o = gc.members[i];
            if (!ObjDestroyed(o))
            {
                set.Remove(o);
            }
        }
    }

    /// <summary>取消选中该组：把组内全部未销毁成员从当前选择中移除，不影响其余选中的对象。</summary>
    private static void CancelGroup(MultiSelectScene scene, GridCell gc)
    {
        for (int i = 0; i < gc.members.Count; i++)
        {
            object o = gc.members[i];
            if (!ObjDestroyed(o) && ObjSelected(o))
            {
                DeselectObj(o);
            }
        }
    }

    /// <summary>单选该组：清空全部待移除/构建选区标记，清空当前选择并仅选中该组。</summary>
    private static void SingleSelectGroup(MultiSelectScene scene, GridCell gc)
    {
        ctrlMarkedForRemoval.Clear();
        shiftSelectedThings.Clear();
        ClearSceneSelection(scene);
        if (gc.isColonist)
        {
            // 殖民者：仅选中该殖民者。
            SelectObj(gc.representative, true, false);
        }
        else
        {
            // 分组：仅选中该组全部成员（首个播放选择音，其余静默避免连响）。
            for (int i = 0; i < gc.members.Count; i++)
            {
                object o = gc.members[i];
                if (!ObjDestroyed(o))
                {
                    SelectObj(o, i == 0, false);
                }
            }
        }
    }

    /// <summary>右键点击。意图由 GetClickIntent 统一映射（默认与换键布局的具体行为见其注释）。</summary>
    private static void HandleCellRightClick(MultiSelectScene scene, GridCell gc)
    {
        ApplyClickIntent(scene, gc, GetClickIntent(isRightButton: true));
    }

    /// <summary>单次点击（左键/右键）的意图，由「当前修饰键 + 输入映射设置」唯一确定。</summary>
    private enum MultiSelectClickIntent
    {
        MultiSelectToggle,  // 把该组加入/移出「构建选区」（黄色高亮，放开触发后保留这些单位、移除其余）
        SingleSelect,       // 清空当前选择并仅选中该组
        CancelGroup,        // 把该组从当前选择中取消选中（不影响其余选中）
        MarkRemovalToggle,  // 把该组标记/取消标记「待移除」（橙色高亮，放开触发后统一移除）
    }

    /// <summary>
    /// 由「鼠标按键 + 当前修饰键 + 输入映射设置」计算本次点击意图（单一事实来源）。
    /// 输入键映射（默认，可由设置互换）：
    ///   默认：左键 = 多选、Shift+左键 = 单选、Ctrl+左键 = 取消；右键 = 多重取消、Ctrl+右键 = 取消。
    ///   开启「使用 Shift 键多选 / 使用 Ctrl 键多重取消」后恢复旧行为：
    ///   左键 = 单选、Shift+左键 = 多选、Ctrl+左键 = 多重取消；右键 = 取消该组。
    /// 修饰键优先级：Ctrl 先于 Shift。
    /// </summary>
    private static MultiSelectClickIntent GetClickIntent(bool isRightButton)
    {
        bool useShiftForMultiSelect = MultiSelectTriggerIsShift;
        bool useCtrlForMultiCancel = MultiCancelTriggerIsCtrl;

        if (isRightButton)
        {
            // 右键：Ctrl 布局（或按住 Ctrl）→ 取消该组；否则（默认）= 多重取消。
            return (useCtrlForMultiCancel || MultiSelectCtrlHeld())
                ? MultiSelectClickIntent.CancelGroup
                : MultiSelectClickIntent.MarkRemovalToggle;
        }
        if (MultiSelectCtrlHeld())
        {
            return useCtrlForMultiCancel
                ? MultiSelectClickIntent.MarkRemovalToggle // 开启 ctrl 布局：Ctrl+左键 = 多重取消
                : MultiSelectClickIntent.CancelGroup;      // 默认：Ctrl+左键 = 取消该组
        }
        if (MultiSelectShiftHeld())
        {
            return useShiftForMultiSelect
                ? MultiSelectClickIntent.MultiSelectToggle // 开启 shift 布局：Shift+左键 = 构建选区
                : MultiSelectClickIntent.SingleSelect;     // 默认：Shift+左键 = 单选
        }
        return useShiftForMultiSelect
            ? MultiSelectClickIntent.SingleSelect        // 开启 shift 布局：普通左键 = 单选
            : MultiSelectClickIntent.MultiSelectToggle;  // 默认：普通左键 = 多选
    }

    /// <summary>按意图分发点击动作。</summary>
    private static void ApplyClickIntent(MultiSelectScene scene, GridCell gc, MultiSelectClickIntent intent)
    {
        switch (intent)
        {
            case MultiSelectClickIntent.MultiSelectToggle:
                ToggleSetMembers(shiftSelectedThings, gc.members);
                break;
            case MultiSelectClickIntent.SingleSelect:
                SingleSelectGroup(scene, gc);
                break;
            case MultiSelectClickIntent.CancelGroup:
                CancelGroup(scene, gc);
                break;
            case MultiSelectClickIntent.MarkRemovalToggle:
                ToggleSetMembers(ctrlMarkedForRemoval, gc.members);
                break;
        }
    }

    /// <summary>左键点击。意图由 GetClickIntent 统一映射（默认与换键布局的具体行为见其注释）。</summary>
    private static void HandleCellClick(MultiSelectScene scene, GridCell gc)
    {
        ApplyClickIntent(scene, gc, GetClickIntent(isRightButton: false));
    }

    /// <summary>把组内未销毁成员整体加入/移出目标集合（以首个成员是否已在集合中为准做统一开关）。</summary>
    private static void ToggleSetMembers(HashSet<object> set, List<object> members)
    {
        bool add = !set.Contains(members[0]);
        for (int i = 0; i < members.Count; i++)
        {
            object o = members[i];
            if (ObjDestroyed(o))
            {
                continue;
            }
            if (add)
            {
                set.Add(o);
            }
            else
            {
                set.Remove(o);
            }
        }
    }

    /// <summary>
    /// 应用「待移除」标记：把这些对象从当前选择中移除（即取消选中），且不影响其余选中，
    /// 随后清空标记。应用时机跟随输入键映射（见 MultiCancelApplyReady）。组内成员可能已销毁
    /// （防御性跳过，避免 Deselect 报错）。
    /// </summary>
    private static void ApplyPendingCtrlRemoval(MultiSelectScene scene)
    {
        if (ctrlMarkedForRemoval.Count == 0 || !MultiCancelApplyReady())
        {
            return;
        }
        foreach (object o in ctrlMarkedForRemoval)
        {
            if (!ObjDestroyed(o) && ObjSelected(o))
            {
                DeselectObj(o);
            }
        }
        ctrlMarkedForRemoval.Clear();
    }

    /// <summary>
    /// 应用「构建选区」：shift 多选 = 移除未被 shift 选择的项——把当前选择中不在
    /// shiftSelectedThings 里的对象全部取消选中，保留 shift 选中的项并维持其原始加入顺序。
    /// 只做移除、不做整批清空重选（否则顺序会被 HashSet 枚举顺序打乱）。随后清空标记。
    /// 应用时机跟随输入键映射（见 MultiSelectApplyReady）。
    /// </summary>
    private static void ApplyPendingShiftSelection(MultiSelectScene scene)
    {
        if (shiftSelectedThings.Count == 0 || !MultiSelectApplyReady())
        {
            return;
        }
        if (scene == MultiSelectScene.World)
        {
            List<WorldObject> wos = Find.WorldSelector.SelectedObjects;
            for (int i = wos.Count - 1; i >= 0; i--)
            {
                if (!shiftSelectedThings.Contains(wos[i]))
                {
                    Find.WorldSelector.Deselect(wos[i]);
                }
            }
        }
        else
        {
            // SelectedObjects 为实时列表（原地增删），倒序遍历避免移除时下标偏移。
            List<object> selected = Find.Selector.SelectedObjects;
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                object o = selected[i];
                if (!shiftSelectedThings.Contains(o))
                {
                    DeselectObj(o);
                }
            }
        }
        shiftSelectedThings.Clear();
    }

    #endregion
}