using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Vanilla Skills Expanded（packageId：vanillaexpanded.skills）的激情定义（PassionDef），
/// 不硬引用该模组 DLL。VSE 用 PassionManager.Passions 数组把原版 Passion（byte）扩展到更多种类
/// （额外三种：VSE_Apathy 冷漠 / VSE_Natural 天赋 / VSE_Critical 狂热），并提供：
///   - PassionManager.PassionToDef(Passion) → PassionDef（公开静态方法，按数组下标映射）；
///   - PassionDef.Icon / PassionDef.Indicator（公开属性：激情图标 / 文本指示符，如 ★ / ★★ / ☆ / Ø / ★★★）。
/// 本模组技能条的激情图标与文本后缀优先取 VSE 的定义，从而支持其额外激情种类；
/// 未装 VSE / 类型或成员未解析到位 / 调用失败时返回 false，由调用方回退原版 SkillUI 图标与 + / ++ 文本。
/// 类型按「名称 + 单参数（原版 Passion）」解析，兼容 VSE 各版本构建。
/// </summary>
public static class VSEPassionReflection
{
    private const string ModPackageId = "vanillaexpanded.skills";
    private const string PassionManagerTypeFullName = "VSE.Passions.PassionManager";

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);

    // PassionManager.PassionToDef(Passion) 的解析结果。
    private static readonly MethodInfo? PassionToDefMethod;
    // PassionDef.Icon / PassionDef.Indicator 属性（在 PassionToDef 的返回类型上解析）。
    private static readonly PropertyInfo? IconProp;
    private static readonly PropertyInfo? IndicatorProp;

    static VSEPassionReflection()
    {
        if (!ModActive)
        {
            return;
        }
        Type? managerType = GenTypes.GetTypeInAnyAssembly(PassionManagerTypeFullName);
        if (managerType == null)
        {
            return;
        }
        foreach (MethodInfo method in managerType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name != "PassionToDef")
            {
                continue;
            }
            ParameterInfo[] parameters = method.GetParameters();
            // 只认单参数重载，且参数为原版 Passion。
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Passion))
            {
                PassionToDefMethod = method;
                break;
            }
        }
        if (PassionToDefMethod == null)
        {
            return;
        }
        Type defType = PassionToDefMethod.ReturnType;
        IconProp = defType.GetProperty("Icon", BindingFlags.Public | BindingFlags.Instance);
        IndicatorProp = defType.GetProperty("Indicator", BindingFlags.Public | BindingFlags.Instance);
    }

    /// <summary>是否已接入 VSE（模组激活且 PassionToDef 解析到位）。</summary>
    internal static bool Ready => PassionToDefMethod != null;

    /// <summary>
    /// 激情的 VSE 图标（PassionDef.Icon）；未接入 VSE / 未解析到位 / 取值为空 / 调用失败时返回 false。
    /// </summary>
    internal static bool TryGetIcon(Passion passion, out Texture2D? icon)
    {
        icon = null;
        object? def = ResolveDef(passion);
        if (def == null || IconProp == null)
        {
            return false;
        }
        icon = IconProp.GetValue(def) as Texture2D;
        return icon != null;
    }

    /// <summary>
    /// 激情的 VSE 文本指示符（PassionDef.Indicator，自带前导空格，如 " ★★★"）；
    /// 未接入 VSE / 未解析到位 / 指示符为空（如无激情）/ 调用失败时返回 false。
    /// </summary>
    internal static bool TryGetIndicator(Passion passion, out string? indicator)
    {
        indicator = null;
        object? def = ResolveDef(passion);
        if (def == null || IndicatorProp == null)
        {
            return false;
        }
        indicator = IndicatorProp.GetValue(def) as string;
        return !string.IsNullOrEmpty(indicator);
    }

    /// <summary>把原版 Passion 映射为 VSE 的 PassionDef（PassionToDef）；调用失败时返回 null 并记一次性警告。</summary>
    private static object? ResolveDef(Passion passion)
    {
        if (PassionToDefMethod == null)
        {
            return null;
        }
        try
        {
            return PassionToDefMethod.Invoke(null, new object[] { passion });
        }
        catch (Exception ex)
        {
            Log.WarningOnce("ASQBetterInspectPane: 调用 Vanilla Skills Expanded 激情判定失败，本次回退原版图标/文本。ex=" + ex.Message, 81752344);
            return null;
        }
    }
}