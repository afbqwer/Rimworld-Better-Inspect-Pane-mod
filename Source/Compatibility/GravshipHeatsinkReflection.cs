using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Vanilla Gravship Expanded 的散热器组件（CompHeatsink）的蓄热状态，
/// 不硬引用该模组 DLL。模组未激活 / 类型未解析到位时全部安全返回 null / false。
/// 口径与游戏散热器检视字符串一致：ActualStoredHeat（已蓄热）/ CachedStats.maxHeat（最大蓄热）。
/// </summary>
public static class GravshipHeatsinkReflection
{
    private const string ModPackageId = "vanillaexpanded.gravship";
    private const string CompTypeFullName = "VanillaGravshipExpanded.CompHeatsink";

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);
    private static readonly Type? CompType;
    private static readonly PropertyInfo? ActualStoredHeatProp;
    private static readonly PropertyInfo? CachedStatsProp;
    private static readonly FieldInfo? MaxHeatField;

    static GravshipHeatsinkReflection()
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
        ActualStoredHeatProp = CompType.GetProperty("ActualStoredHeat");
        CachedStatsProp = CompType.GetProperty("CachedStats");
        MaxHeatField = CachedStatsProp?.PropertyType.GetField("maxHeat");
    }

    /// <summary>是否适配（模组已激活且类型、成员均已解析到位）。</summary>
    private static bool Ready =>
        ModActive && CompType != null && ActualStoredHeatProp != null
        && CachedStatsProp != null && MaxHeatField != null;

    /// <summary>从物体组件列表中按类型匹配散热器组件；无则返回 null。</summary>
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

    /// <summary>读取蓄热 / 最大蓄热；组件或成员无效时返回 false。</summary>
    public static bool TryGetHeat(ThingComp comp, out float stored, out float max)
    {
        stored = 0f;
        max = 0f;
        if (!Ready || comp == null)
        {
            return false;
        }
        object stats = CachedStatsProp!.GetValue(comp);
        if (stats == null)
        {
            return false;
        }
        object s = ActualStoredHeatProp!.GetValue(comp);
        object m = MaxHeatField!.GetValue(stats);
        if (s is float sf && m is float mf && mf > 0f)
        {
            stored = sf;
            max = mf;
            return true;
        }
        return false;
    }
}