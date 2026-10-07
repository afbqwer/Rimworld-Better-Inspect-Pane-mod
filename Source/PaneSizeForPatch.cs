using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using HarmonyLib;

namespace ASQBetterInspectPane;

/// <summary>
/// 调整检查面板窗口尺寸。
/// 原版：宽度按可见 Tab 数量动态计算（72 × max(6, tab 数)），高度固定 165f。
/// 宽度与高度均取原版值与设置最小尺寸中的较大者（Tab 数量多时宽度仍随原版动态加宽）。
/// 多选网格激活时（地图 Things/Zones 场景，或世界地图对象网格场景）改用「多选检查面板」的独立最小尺寸：
/// 覆盖最小宽/高（未开启则沿用全局），自适应高度开启时最小高度提升到能容纳配置行数网格的高度。
/// </summary>
[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.PaneSizeFor))]
public static class PaneSizeForPatch
{
    static void Postfix(ref Vector2 __result, IInspectPane pane)
    {
        bool mapGrid = pane is MainTabWindow_Inspect && InspectPanePatch.IsMultiSelectGridActive();
        bool worldGrid = pane is WorldInspectPane && InspectPanePatch.IsWorldMultiSelectGridActive();
        if (mapGrid || worldGrid)
        {
            if (__result.x < MyModTemplateSettings.MultiSelectEffectiveMinWidth)
            {
                __result.x = MyModTemplateSettings.MultiSelectEffectiveMinWidth;
            }
            if (__result.y < MyModTemplateSettings.MultiSelectEffectiveMinHeight)
            {
                __result.y = MyModTemplateSettings.MultiSelectEffectiveMinHeight;
            }
            return;
        }
        // 单选 / 存储组多选 / 世界地图面板（非网格）：沿用全局最小尺寸。
        // 先校正自适应高度状态：当前选择不是自适应路径正在维护的对象（如 Pawn，
        // 或其它 mod 覆盖了检查面板、根本不走 DrawInspectString）时立即复位，
        // 否则 requiredPaneHeight 保留上一个对象被撑高的值，面板无法缩回。
        InspectPanePatch.SyncAdaptivePaneHeight(InspectPanePatch.GetCurrentSelected());
        if (__result.x < MyModTemplateSettings.paneWidth)
        {
            __result.x = MyModTemplateSettings.paneWidth;
        }
        if (__result.y < MyModTemplateSettings.EffectivePaneHeight)
        {
            __result.y = MyModTemplateSettings.EffectivePaneHeight;
        }
    }
}
