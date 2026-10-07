using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Vanilla Expanded Framework 的「处理系统」组件（PipeSystem.CompAdvancedResourceProcessor），
/// 不硬引用该框架 DLL。框架未激活 / 类型或属性未解析到位时全部安全返回 null / false。
/// 用于 VFE Factory 等「配方」工厂建筑的生产进度与理论剩余时间读取。
/// </summary>
public static class VEFProcessorReflection
{
    private const string ModPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";
    private const string CompTypeFullName = "PipeSystem.CompAdvancedResourceProcessor";

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);
    private static readonly Type? CompType;
    private static readonly PropertyInfo? ProcessProperty;
    private static readonly PropertyInfo? ProgressProperty;
    private static readonly PropertyInfo? TickLeftProperty;

    static VEFProcessorReflection()
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
        ProcessProperty = CompType.GetProperty("Process");
        if (ProcessProperty == null)
        {
            return;
        }
        ProgressProperty = ProcessProperty.PropertyType?.GetProperty("Progress");
        TickLeftProperty = ProcessProperty.PropertyType?.GetProperty("TickLeft");
    }

    /// <summary>是否适配（框架已激活且类型、属性均已解析到位）。</summary>
    private static bool Ready => ModActive && CompType != null && ProcessProperty != null
        && ProgressProperty != null && TickLeftProperty != null;

    /// <summary>从物体组件列表中按类型匹配处理系统组件；无则返回 null。</summary>
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

    /// <summary>
    /// 读取当前生产进度（0..1）：无进行中的配方（Process 为 null）或读取失败时返回 false。
    /// </summary>
    public static bool TryGetProgress(ThingComp comp, out float progress)
    {
        progress = 0f;
        if (!Ready || comp == null)
        {
            return false;
        }
        object? process = ProcessProperty!.GetValue(comp);
        if (process == null)
        {
            return false;
        }
        object? p = ProgressProperty!.GetValue(process);
        if (p is float f)
        {
            progress = f;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 读取当前配方流程的理论剩余刻数（Process.TickLeft，游戏刻）：
    /// 无进行中的配方（Process 为 null）或读取失败时返回 false。
    /// 工厂为脉冲式推进（CompTickRare 每 250 刻调用 Tick(250)），采样估算不稳定，直接使用此理论值。
    /// </summary>
    public static bool TryGetRemainingTicks(ThingComp comp, out int ticksLeft)
    {
        ticksLeft = 0;
        if (!Ready || comp == null)
        {
            return false;
        }
        object? process = ProcessProperty!.GetValue(comp);
        if (process == null)
        {
            return false;
        }
        object? t = TickLeftProperty!.GetValue(process);
        if (t is int i)
        {
            ticksLeft = i;
            return true;
        }
        return false;
    }
}
