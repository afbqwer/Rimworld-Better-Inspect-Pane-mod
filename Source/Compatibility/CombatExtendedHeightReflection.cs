using System;
using System.Reflection;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Combat Extended 的竖直碰撞高度（以「米」为单位），不硬引用该模组 DLL。
/// CE 的高度口径与 CE 自己的 UI（信息卡「掩护高度」、攻击提示 CE_CoverHeight）保持一致：
///   米数 = CollisionVertical(thing).Max * CE_Utility.MetersPerCellHeight（1.75 米/格）。
/// 其中 Max 是 CE 的抽象碰撞高度（完整墙壁 = WallCollisionHeight = 2，站立人类 ≈ 1，植物为其图形高度），
/// 乘上 1.75 后墙壁即 CE 官方显示的 3.5 米。
/// CE 未激活 / 类型或成员未解析到位时全部安全返回 false / 0。
/// </summary>
public static class CombatExtendedHeightReflection
{
    private const string ModPackageId = "CETeam.CombatExtended";

    private const string CollisionVerticalTypeFullName = "CombatExtended.CollisionVertical";

    private const string CE_UtilityTypeFullName = "CombatExtended.CE_Utility";

    /// <summary>CE 的「米 / 格」系数（CE_Utility.MetersPerCellHeight），反射失败时的兜底值。</summary>
    private const float MetersPerCellHeightFallback = 1.75f;

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);

    private static readonly Type? CollisionVerticalType;
    private static readonly PropertyInfo? MaxProp;
    private static readonly FieldInfo? MetersPerCellHeightField;

    static CombatExtendedHeightReflection()
    {
        if (!ModActive)
        {
            return;
        }
        CollisionVerticalType = GenTypes.GetTypeInAnyAssembly(CollisionVerticalTypeFullName);
        if (CollisionVerticalType == null)
        {
            return;
        }
        MaxProp = CollisionVerticalType.GetProperty("Max"); // 抽象竖直碰撞高度上限（格）
        Type? ceUtility = GenTypes.GetTypeInAnyAssembly(CE_UtilityTypeFullName);
        MetersPerCellHeightField = ceUtility?.GetField("MetersPerCellHeight",
            BindingFlags.Public | BindingFlags.Static);
    }

    /// <summary>是否适配（CE 激活且 CollisionVertical / Max 均已解析到位）。</summary>
    private static bool Ready => ModActive && CollisionVerticalType != null && MaxProp != null;

    /// <summary>CE 的「米 / 格」系数；反射读取失败时用兜底值 1.75。</summary>
    private static float MetersPerCellHeight
    {
        get
        {
            try
            {
                return MetersPerCellHeightField != null
                    && MetersPerCellHeightField.GetValue(null) is float f
                    && f > 0f
                        ? f
                        : MetersPerCellHeightFallback;
            }
            catch (Exception)
            {
                return MetersPerCellHeightFallback;
            }
        }
    }

    /// <summary>
    /// 读取给定物体的 CE 高度（米）。CE 未激活、类型未就绪、物体为空或高度无效（如无碰撞体积）时返回 false。
    /// 通过公共构造函数 new CollisionVertical(thing) 触发 CE 自身的高度计算，再乘 CE 的米/格系数，
    /// 与 CE 信息卡「掩护高度」显示一致（完整墙壁 = 2 * 1.75 = 3.5 米）。
    /// </summary>
    public static bool TryGetHeight(Thing? thing, out float height)
    {
        height = 0f;
        if (!Ready || thing == null)
        {
            return false;
        }
        try
        {
            object? cv = Activator.CreateInstance(CollisionVerticalType, thing);
            if (cv == null || MaxProp!.GetValue(cv) is not float f || f <= 0f)
            {
                return false;
            }
            height = f * MetersPerCellHeight;
            return true;
        }
        catch (Exception ex)
        {
            // 个别 mod 的定义可能在高度计算时抛异常；按对象记录一次即可，随后按无高度处理。
            Log.WarningOnce("ASQBetterInspectPane: 读取 Combat Extended 高度失败，已忽略。ex=" + ex.Message, 51239077);
            return false;
        }
    }
}