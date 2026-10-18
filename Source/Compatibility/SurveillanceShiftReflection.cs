using System;
using System.Reflection;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Surveillance Shift（packageId：ferny.SurveillanceShift）的「本帧是否画额外图标」判定，
/// 不硬引用该模组 DLL。SS 在 MainTabWindow_Inspect.DoInspectPaneButtons 的 Postfix
/// （SurveillanceShift.InspectStripButtons.VanillaPostfix）里，于原版按钮链左侧再画 1~2 枚 24px 图标
/// （人类殖民者：威胁上报方式；动物 / 机械体：警报响应方式），其矩形公式为
///   x = rect.width - 24f - lineEndWidth - 24f，随后 lineEndWidth += 24f。
/// 问题在于：该图标链与原版按钮链之间留了 24px 间隔（其 x 比原版口径的下一枚按钮再左移 24），
/// 但 lineEndWidth 只按图标宽度推进、没把这 24px 间隔计入 —— 即 SS 画完后 lineEndWidth 比
/// 「右侧实际已占用宽度」少 24。本模组接管面板绘制（InspectPaneOnGUIPatch）时沿用原版口径
/// 「下一枚按钮 x = rect.width - lineEndWidth - 24f」排布额外图标（异种 / 阵营 / 文化），
/// 于是恰好落在 SS 最左那枚图标上（24px 完全重合）—— 即两模组图标叠在一起的观感。
/// 做法：调用 SS 自身的两个绘制判定（ThreatButtonWanted / AlertButtonWanted，私有静态 bool(Pawn)），
/// 任一为真即说明本帧 SS 确实画了图标，把缺失的 24px 间隔补进 lineEndWidth，
/// 使额外图标接在 SS 图标左侧（与 SS 图标彼此紧贴的排布保持一致）。
/// 补的是「固定 24px 接缝」而非「每枚图标 24px」：SS 自己的多枚图标彼此紧贴，间隔只出现在与原版按钮链的接缝处，
/// 因此图标数量变化不影响补偿量。
/// SS 在 RimHUD 激活时改画到 RimHUD 的按钮条（本面板不画），此处同步跳过。
/// 模组未激活 / 类型或成员未解析到位 / 判定调用失败时一律返回 0（保持未装 SS 时的行为）。
/// dll位置：D:\SteamLibrary\steamapps\workshop\content\294100\3819508584\1.6\Assemblies
/// </summary>
public static class SurveillanceShiftReflection
{
    private const string ModPackageId = "ferny.SurveillanceShift";
    private const string StripButtonsTypeFullName = "SurveillanceShift.InspectStripButtons";
    private const string RimHudPackageId = "jaxe.rimhud";

    /// <summary>SS 图标与原版按钮链接缝处的间隔（等于其 ButtonSize），也是需要补进 lineEndWidth 的宽度。</summary>
    internal const float StripGapWidth = 24f;

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);

    // InspectStripButtons.ThreatButtonWanted / AlertButtonWanted 的解析结果（均为私有静态 bool(Pawn)）。
    private static readonly MethodInfo? ThreatButtonWantedMethod;
    private static readonly MethodInfo? AlertButtonWantedMethod;

    static SurveillanceShiftReflection()
    {
        if (!ModActive)
        {
            return;
        }
        Type? stripButtonsType = GenTypes.GetTypeInAnyAssembly(StripButtonsTypeFullName);
        if (stripButtonsType == null)
        {
            return;
        }
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        ThreatButtonWantedMethod = stripButtonsType.GetMethod("ThreatButtonWanted", Flags, null, new[] { typeof(Pawn) }, null);
        AlertButtonWantedMethod = stripButtonsType.GetMethod("AlertButtonWanted", Flags, null, new[] { typeof(Pawn) }, null);
    }

    /// <summary>Surveillance Shift 是否已安装并激活。</summary>
    internal static bool IsActive => ModActive;

    /// <summary>
    /// 本帧 SS 会在地图检查面板上为 pawn 画额外图标时，返回需要补进 lineEndWidth 的宽度（否则 0）。
    /// 只在地图检查面板（MainTabWindow_Inspect）调用：SS 的补丁只挂在该面板的 DoInspectPaneButtons 上。
    /// </summary>
    internal static float ExtraStripGapWidth(Pawn? pawn)
    {
        if (pawn == null || ThreatButtonWantedMethod == null || AlertButtonWantedMethod == null)
        {
            return 0f;
        }
        // RimHUD 激活时 SS 整体改走 RimHUD 的按钮条，本面板不再画图标，无需补偿。
        if (ModsConfig.IsActive(RimHudPackageId))
        {
            return 0f;
        }
        try
        {
            // 判定为纯读取（不绘制、不改状态），每帧调用一次的成本可忽略。
            if (Wanted(ThreatButtonWantedMethod, pawn) || Wanted(AlertButtonWantedMethod, pawn))
            {
                return StripGapWidth;
            }
            return 0f;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("ASQBetterInspectPane: 调用 Surveillance Shift 图标判定失败，本次按无额外图标处理。ex=" + ex.Message, 81752344);
            return 0f;
        }
    }

    private static bool Wanted(MethodInfo method, Pawn pawn)
    {
        return method.Invoke(null, new object[] { pawn }) is bool wanted && wanted;
    }
}
