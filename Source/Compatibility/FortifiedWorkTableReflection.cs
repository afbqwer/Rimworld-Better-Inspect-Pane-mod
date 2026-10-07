using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Fortified Feature Framework 的「自主工作台」建筑类型
/// （Fortified.Building_WorkTableAutonomous，如 The Dead Man's Switch 的 Machinery Printer / Machine Printer），
/// 不硬引用该框架 DLL。模组未激活 / 类型或字段未解析到位时全部安全返回 false / 0。
/// 该自主工作台按配方生产，剩余工作量 = curWorkAmount + totalWorkAmount（在整个配方周期内单调递减）。
/// </summary>
public static class FortifiedWorkTableReflection
{
    private const string ModPackageId = "AOBA.Framework";
    private const string BuildingTypeFullName = "Fortified.Building_WorkTableAutonomous";

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);
    private static readonly Type? BuildingType;
    private static readonly FieldInfo? ActiveBillField;
    private static readonly FieldInfo? CurWorkAmountField;
    private static readonly FieldInfo? TotalWorkAmountField;

    static FortifiedWorkTableReflection()
    {
        if (!ModActive)
        {
            return;
        }
        BuildingType = GenTypes.GetTypeInAnyAssembly(BuildingTypeFullName);
        if (BuildingType == null)
        {
            return;
        }
        ActiveBillField = BuildingType.GetField("activeBill");
        CurWorkAmountField = BuildingType.GetField("curWorkAmount");
        TotalWorkAmountField = BuildingType.GetField("totalWorkAmount");
    }

    /// <summary>是否适配（框架已激活且类型、字段均已解析到位）。</summary>
    private static bool Ready => ModActive && BuildingType != null
        && ActiveBillField != null && CurWorkAmountField != null && TotalWorkAmountField != null;

    /// <summary>给定建筑是否为 Fortified 自主工作台（如 Machine Printer）。</summary>
    public static bool IsAutonomousWorkTable(Thing? thing)
        => Ready && thing != null && BuildingType!.IsInstanceOfType(thing);

    /// <summary>
    /// 读取当前配方的剩余工作量：读不到（类型不匹配）或建筑未在工作时返回 false。
    /// 剩余量 = curWorkAmount + totalWorkAmount，整个配方周期内单调递减，与工作条的递减口径一致。
    /// </summary>
    public static bool TryGetWorkRemaining(Thing thing, out float remaining)
    {
        remaining = 0f;
        if (!IsAutonomousWorkTable(thing))
        {
            return false;
        }
        object cur = CurWorkAmountField!.GetValue(thing);
        object total = TotalWorkAmountField!.GetValue(thing);
        if (cur is float c && total is float t)
        {
            remaining = Mathf.Max(0f, c + t);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 读取当前配方的理论剩余刻数：自主工作台每 tickRare（每约 250 刻）推进一次，
    /// 相邻两刻采样无法测得真实速度，因此直接按其剩余工作量换算为游戏刻（1 工作量 ≈ 1 刻，
    /// 与原版 GetInspectString 把 curWorkAmount 直接当刻数显示的语义一致）。
    /// 读不到或未在工作时返回 false。
    /// </summary>
    public static bool TryGetRemainingTicks(Thing thing, out int ticksLeft)
    {
        ticksLeft = 0;
        if (!TryGetWorkRemaining(thing, out float remaining))
        {
            return false;
        }
        if (remaining >= int.MaxValue)
        {
            return false;
        }
        ticksLeft = Mathf.Max(0, Mathf.RoundToInt(remaining));
        return true;
    }
}