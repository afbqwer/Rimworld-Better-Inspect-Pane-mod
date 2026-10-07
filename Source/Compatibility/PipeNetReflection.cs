using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Vanilla Expanded Framework 的「管道网络」系统（PipeSystem.dll），
/// 不硬引用该框架 DLL。框架未激活 / 类型或成员未解析到位时全部安全返回 null / false。
/// 用于 VFE Core（含 VChemfuelE 燃料管道等）管道网络的整网内容物/容量条，
/// 以及 CompResourceStorage 容器自身的内容物/容量条，并读取 PipeSystem.Resource.color 作为条颜色。
/// </summary>
public static class PipeNetReflection
{
    private const string ModPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

    private const string CompResourceTypeFullName = "PipeSystem.CompResource";
    private const string PipeNetTypeFullName = "PipeSystem.PipeNet";
    private const string CompResourceStorageTypeFullName = "PipeSystem.CompResourceStorage";
    private const string CompPropsResourceStorageTypeFullName = "PipeSystem.CompProperties_ResourceStorage";
    private const string ResourceTypeFullName = "PipeSystem.Resource";

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);

    private static readonly Type? CompResourceType;
    private static readonly PropertyInfo? PipeNetProp;
    private static readonly Type? PipeNetType;
    private static readonly PropertyInfo? StoredProp;
    private static readonly PropertyInfo? AvailableCapacityProp;
    private static readonly Type? CompResourceStorageType;
    private static readonly PropertyInfo? AmountStoredProp;
    private static readonly PropertyInfo? PropsProp;
    private static readonly Type? CompPropsResourceStorageType;
    private static readonly FieldInfo? StorageCapacityField;
    private static readonly Type? ResourceType;
    private static readonly PropertyInfo? ResourceProp;
    private static readonly FieldInfo? ColorField;

    static PipeNetReflection()
    {
        if (!ModActive)
        {
            return;
        }
        CompResourceType = GenTypes.GetTypeInAnyAssembly(CompResourceTypeFullName);
        if (CompResourceType == null)
        {
            return;
        }
        PipeNetProp = CompResourceType.GetProperty("PipeNet");
        if (PipeNetProp == null)
        {
            return;
        }
        PipeNetType = PipeNetProp.PropertyType;
        if (PipeNetType == null)
        {
            return;
        }
        StoredProp = PipeNetType.GetProperty("Stored");
        AvailableCapacityProp = PipeNetType.GetProperty("AvailableCapacity");
        if (StoredProp == null || AvailableCapacityProp == null)
        {
            return;
        }
        ResourceProp = CompResourceType.GetProperty("Resource");

        CompResourceStorageType = GenTypes.GetTypeInAnyAssembly(CompResourceStorageTypeFullName);
        if (CompResourceStorageType != null)
        {
            AmountStoredProp = CompResourceStorageType.GetProperty("AmountStored");
            // Props 在 CompResourceStorage 用 new 遮蔽基类同名属性，须只取 DeclaredOnly 以避免歧义。
            PropsProp = CompResourceStorageType.GetProperty("Props",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }
        CompPropsResourceStorageType = GenTypes.GetTypeInAnyAssembly(CompPropsResourceStorageTypeFullName);
        if (CompPropsResourceStorageType != null)
        {
            StorageCapacityField = CompPropsResourceStorageType.GetField("storageCapacity");
        }

        ResourceType = GenTypes.GetTypeInAnyAssembly(ResourceTypeFullName);
        if (ResourceType != null)
        {
            ColorField = ResourceType.GetField("color");
        }
    }

    /// <summary>是否适配（框架已激活且广度侧 CompResource / PipeNet / Stored / AvailableCapacity 均已解析到位）。</summary>
    private static bool Ready => ModActive
        && CompResourceType != null
        && PipeNetProp != null
        && StoredProp != null
        && AvailableCapacityProp != null;

    /// <summary>容器侧是否适配（CompResourceStorage 及其容量读取成员均已解析到位）。</summary>
    private static bool ContainerReady => Ready
        && CompResourceStorageType != null
        && AmountStoredProp != null
        && PropsProp != null
        && CompPropsResourceStorageType != null
        && StorageCapacityField != null;

    /// <summary>资源颜色侧是否适配（CompResource.Resource / PipeSystem.Resource.color 均已解析到位）。</summary>
    private static bool ColorReady => Ready && ResourceProp != null && ResourceType != null && ColorField != null;

    /// <summary>按类型匹配管道网络组件（CompResource 及其全部派生，如储存容器 / 工厂 / 抽气机）；无则返回 null。</summary>
    public static ThingComp? FindComp(ThingWithComps? twc)
    {
        if (!Ready || twc == null)
        {
            return null;
        }
        List<ThingComp> comps = twc.AllComps;
        for (int i = 0; i < comps.Count; i++)
        {
            if (CompResourceType!.IsAssignableFrom(comps[i].GetType()))
            {
                return comps[i];
            }
        }
        return null;
    }

    /// <summary>
    /// 读取所在管道网络的整网内容物 / 总容量：stored = PipeNet.Stored，总容量 = Stored + AvailableCapacity。
    /// 未连接（PipeNet 为 null）或读取失败时返回 false。
    /// </summary>
    public static bool TryGetNetworkData(ThingComp comp, out float stored, out float totalCapacity)
    {
        stored = 0f;
        totalCapacity = 0f;
        if (!Ready || comp == null)
        {
            return false;
        }
        object? pipeNet = PipeNetProp!.GetValue(comp);
        if (pipeNet == null)
        {
            return false;
        }
        object? s = StoredProp!.GetValue(pipeNet);
        object? a = AvailableCapacityProp!.GetValue(pipeNet);
        if (s is float storedF && a is float availableF)
        {
            stored = storedF;
            totalCapacity = storedF + availableF;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 读取管道网络储存容器（CompResourceStorage）自身的内容物 / 容量：
    /// stored = AmountStored，capacity = Props.storageCapacity。非储存容器或读取失败时返回 false。
    /// </summary>
    public static bool TryGetContainerData(ThingComp comp, out float stored, out float capacity)
    {
        stored = 0f;
        capacity = 0f;
        if (!ContainerReady || comp == null)
        {
            return false;
        }
        if (!CompResourceStorageType!.IsAssignableFrom(comp.GetType()))
        {
            return false;
        }
        object? amount = AmountStoredProp!.GetValue(comp);
        object? props = PropsProp!.GetValue(comp);
        if (amount is float amountF && props != null && StorageCapacityField!.GetValue(props) is float capF)
        {
            stored = amountF;
            capacity = capF;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 读取该组件所属资源的颜色（PipeSystem.Resource.color）。解析失败或资源无颜色时返回 false。
    /// </summary>
    public static bool TryGetResourceColor(ThingComp comp, out Color color)
    {
        color = default;
        if (!ColorReady || comp == null)
        {
            return false;
        }
        object? resource = ResourceProp!.GetValue(comp);
        if (resource == null || ColorField!.GetValue(resource) is not Color c)
        {
            return false;
        }
        color = c;
        return true;
    }
}