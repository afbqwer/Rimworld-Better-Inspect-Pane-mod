using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Vanilla Gravship Expanded 的维护组件（CompGravMaintainable），
/// 不硬引用该模组 DLL。模组未激活 / 类型未解析到位时全部安全返回 null / false。
/// </summary>
public static class GravMaintenanceReflection
{
    private const string ModPackageId = "vanillaexpanded.gravship";
    private const string CompTypeFullName = "VanillaGravshipExpanded.CompGravMaintainable";

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);
    private static readonly Type? CompType;
    private static readonly FieldInfo? MaintenanceField;
    private static readonly FieldInfo? MaintenanceFallsField;

    static GravMaintenanceReflection()
    {
        if (!ModActive)
        {
            return;
        }
        CompType = GenTypes.GetTypeInAnyAssembly(CompTypeFullName);
        if (CompType == null)
        {
            return;
        }
        MaintenanceField = CompType.GetField("maintenance");
        MaintenanceFallsField = CompType.GetField("maintenanceFalls");
    }

    /// <summary>是否适配（模组已激活且类型、字段均已解析到位）。</summary>
    private static bool Ready => ModActive && CompType != null && MaintenanceField != null && MaintenanceFallsField != null;

    /// <summary>从物体组件列表中按类型匹配维护组件；无则返回 null。</summary>
    public static ThingComp? FindComp(ThingWithComps? twc)
    {
        if (!Ready || twc == null)
        {
            return null;
        }
        List<ThingComp> comps = twc.AllComps;
        for (int i = 0; i < comps.Count; i++)
        {
            if (comps[i].GetType() == CompType)
            {
                return comps[i];
            }
        }
        return null;
    }

    /// <summary>读取维护剩余量与「是否需维护」；组件或字段无效时返回 false。</summary>
    public static bool TryGetMaintenance(ThingComp comp, out float maintenance, out bool maintenanceFalls)
    {
        maintenance = 0f;
        maintenanceFalls = false;
        if (!Ready || comp == null)
        {
            return false;
        }
        object m = MaintenanceField!.GetValue(comp);
        object f = MaintenanceFallsField!.GetValue(comp);
        if (m is float mf && f is bool bf)
        {
            maintenance = mf;
            maintenanceFalls = bf;
            return true;
        }
        return false;
    }
}