using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using HarmonyLib;

namespace ASQBetterInspectPane;

/// <summary>
/// 检查面板尺寸被设置调大后，让原版其它 UI 区域让出空间，避免显示区域重合：
/// 1. 宽度调大 → gizmo 区域向右移动：原版 GizmoGridDrawer.DrawGizmoGridFor 以 14 + PaneWidthFor 作为 gizmo 起始 X，
///    InspectPaneUtility.DoTabs 以 PaneWidthFor 作为标签页起始 X 基准。把 PaneWidthFor 提升到设置宽度，
///    二者同步右移、让出加宽的面板区域。
/// 2. 高度调大 → 标签页区域跟随面板顶部移动：DoTabs 以 pane.PaneTopY - 30 定位标签页，
///    而 MainTabWindow_Inspect.PaneTopY 原版固定按 165 高计算（screenHeight - 165 - 35），
///    面板变高后标签页会被面板覆盖，改为按实际面板高度计算顶部 Y。
/// 3. 高度运行时变化 → 世界面板窗口矩形跟随：WorldInspectPane 原版只在打开/分辨率变化时
///    设置窗口矩形（原版高度恒为 165，无需跟随），运行时增高后窗口本体不会移动或变高，
///    需按请求尺寸重设窗口矩形（见 WorldPaneResizePatch）。
/// </summary>
public static class PaneLayoutPatch
{
    /// <summary>
    /// gizmo 与标签页的横向基准：取 max(原版按 Tab 数量计算的宽度, 设置的最小宽度)。
    /// 原版宽度基于可见 Tab 数（72 × max(6, tab 数)），设置只增不减，Tab 多时仍随原版动态加宽。
    /// 多选网格激活时（仅地图面板）改用「多选检查面板」的覆盖最小宽度（未开启则沿用全局）。
    /// </summary>
    [HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.PaneWidthFor))]
    public static class PaneWidthForPatch
    {
        static void Postfix(ref float __result, IInspectPane pane)
        {
            if (pane is MainTabWindow_Inspect && InspectPanePatch.IsMultiSelectGridActive())
            {
                if (__result < MyModTemplateSettings.MultiSelectEffectiveMinWidth)
                {
                    __result = MyModTemplateSettings.MultiSelectEffectiveMinWidth;
                }
                return;
            }
            if (__result < MyModTemplateSettings.paneWidth)
            {
                __result = MyModTemplateSettings.paneWidth;
            }
        }
    }

    /// <summary>
    /// 标签页的纵向基准：按实际面板高度计算面板顶部 Y（原版固定按 165 高计算）。
    /// 与 PaneSizeForPatch 一致，取 max(原版固定 165f, 设置最小高度)（设置只增不减），
    /// 使 DoTabs（PaneTopY - 30）与 ITab.PaneTopY、设计指示器控件跟随面板顶部，不被加高的面板覆盖。
    /// 多选网格激活时用「多选检查面板」的有效最小高度（含自适应高度）。
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.PaneTopY), MethodType.Getter)]
    public static class PaneTopYPatch
    {
        static bool Prefix(ref float __result)
        {
            float height = Mathf.Max(InspectPaneUtility.PaneHeight, MyModTemplateSettings.CurrentEffectivePaneHeight);
            __result = UI.screenHeight - height - MainButtonDef.ButtonHeight;
            return false;
        }
    }

    /// <summary>
    /// 世界地图检查面板的顶部 Y：按实际面板高度计算（原版固定按 165f 高计算）。
    /// 世界面板 SetInitialSizeAndPosition 用 windowRect.y = PaneTopY 顶部锚定面板，
    /// PaneSizeForPatch 把高度调大后，若仍按 165f 算顶部，面板会从顶部向下溢出屏幕、
    /// 底部越过被查看对象，出现错位。这里与 MainTabWindow_Inspect.PaneTopY 一致，
    /// 用 max(原版 165f, 设置最小高度) 计算，使面板向上生长、底部保持正常位置。
    /// </summary>
    [HarmonyPatch(typeof(WorldInspectPane), nameof(WorldInspectPane.PaneTopY), MethodType.Getter)]
    public static class WorldPaneTopYPatch
    {
        static bool Prefix(ref float __result)
        {
            // 世界多选对象网格激活时用「多选检查面板」高度（含自适应高度），否则用全局高度。
            float effectiveHeight = InspectPanePatch.IsWorldMultiSelectGridActive()
                ? MyModTemplateSettings.MultiSelectEffectiveMinHeight
                : MyModTemplateSettings.EffectivePaneHeight;
            float height = Mathf.Max(InspectPaneUtility.PaneHeight, effectiveHeight);
            float num = UI.screenHeight - height;
            if (Current.ProgramState == ProgramState.Playing)
            {
                num -= MainButtonDef.ButtonHeight;
            }
            __result = num;
            return false;
        }
    }

    /// <summary>
    /// 世界地图检查面板的窗口矩形跟随尺寸变化：原版 WorldInspectPane 只在打开（与分辨率变化）时
    /// 执行 SetInitialSizeAndPosition，此后 windowRect 固定不变（原版高度恒为 165，无需跟随）。
    /// 面板运行时增高（自适应面板高度 / 面板最小高度设置）后，顶部 Y（PaneTopY，供标签页等定位）
    /// 上移而窗口本体不动，表现为面板高度不生效、标签页与面板之间出现空隙。
    /// 与 MainTabWindow_Inspect 的做法一致（其 DoWindowContents 在 RequestedTabSize 变化时
    /// 重设窗口矩形），在 DoWindowContents 后按 PaneSizeFor 的请求尺寸重设窗口矩形：
    /// 左缘贴屏幕左端（沿用原版 SetInitialSizeAndPosition 的 x=0）、顶部锚定 PaneTopY、向上生长；
    /// 因 DoWindowContents 在 GUI.Window 回调内执行，重设于下一帧生效（与原版地图面板自适应的时序一致）。
    /// </summary>
    [HarmonyPatch(typeof(WorldInspectPane), nameof(WorldInspectPane.DoWindowContents))]
    public static class WorldPaneResizePatch
    {
        static void Postfix(WorldInspectPane __instance)
        {
            Vector2 size = InspectPaneUtility.PaneSizeFor(__instance);
            Rect rect = __instance.windowRect;
            if (Mathf.Abs(rect.width - size.x) < 0.5f && Mathf.Abs(rect.height - size.y) < 0.5f)
            {
                return;
            }
            rect.width = size.x;
            rect.height = size.y;
            rect.x = 0f;
            rect.y = __instance.PaneTopY;
            __instance.windowRect = rect.Rounded();
        }
    }
}
