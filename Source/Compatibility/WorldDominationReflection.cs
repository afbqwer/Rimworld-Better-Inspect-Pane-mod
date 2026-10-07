using System;
using System.Reflection;
using RimWorld.Planet;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 World Domination 2 的世界对象强度，不硬引用该模组 DLL。两类强度体系：
/// 1) CompViralSpread 组件（由 Settlement_Patch.xml 挂到据点 Settlement 与前哨站 WorldObject_WD_Outpost 等世界对象）：
///    总强度 = 进攻（offensiveStrength）+ 防御（defensiveStrength），
///    上限 = GetMaxOffensiveStrength() + GetBaseDefensiveStrength()（两个只读计算方法，无副作用）；
///    玩家地图殖民地上限为 0（WD2 检查文字同样不显示强度）、未知对象类型上限为 float.MaxValue，均视为不可显示。
/// 2) 移动商队（WorldObject_Traveler 及其任务子类）：当前强度 = travelerStrength，基数 = initialStrength（出发时强度），
///    口径与其检查文字「Strength: X / Y (P%)」一致；基数 ≤ 0（WD2 按 100% 处理）时视为不可显示。
/// 模组未激活 / 类型或成员未解析到位时全部安全返回 null / false。
/// dll位置：D:\SteamLibrary\steamapps\workshop\content\294100\3680501610\Assemblies
/// </summary>
public static class WorldDominationReflection
{
    private const string ModPackageId = "TSA.WorldDominationExperimental";
    private const string CompTypeFullName = "TSA_WorldDomination.CompViralSpread";
    private const string TravelerTypeFullName = "TSA_WorldDomination.WorldObject_Traveler";

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);
    private static readonly Type? CompType;
    private static readonly FieldInfo? OffensiveStrengthField;
    private static readonly FieldInfo? DefensiveStrengthField;
    private static readonly MethodInfo? GetMaxOffensiveStrengthMethod;
    private static readonly MethodInfo? GetBaseDefensiveStrengthMethod;
    private static readonly Type? TravelerType;
    private static readonly FieldInfo? TravelerStrengthField;
    private static readonly FieldInfo? TravelerInitialStrengthField;
    private static readonly MethodInfo? MaxGoodwillCapMethod;

    static WorldDominationReflection()
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
        const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        OffensiveStrengthField = CompType.GetField("offensiveStrength", InstanceFlags);
        DefensiveStrengthField = CompType.GetField("defensiveStrength", InstanceFlags);
        GetMaxOffensiveStrengthMethod = CompType.GetMethod("GetMaxOffensiveStrength", InstanceFlags, null, Type.EmptyTypes, null);
        GetBaseDefensiveStrengthMethod = CompType.GetMethod("GetBaseDefensiveStrength", InstanceFlags, null, Type.EmptyTypes, null);
        // 移动商队体系（可选：解析失败只影响商队强度条，不影响据点 / 前哨站）。
        TravelerType = GenTypes.GetTypeInAnyAssembly(TravelerTypeFullName);
        TravelerStrengthField = TravelerType?.GetField("travelerStrength", InstanceFlags);
        TravelerInitialStrengthField = TravelerType?.GetField("initialStrength", InstanceFlags);
        // 关系上限（可选）：WD2 用 Transpiler 把原版好感上限 100 替换为 MaxGoodwillCap() = max(100, 设置.maxGoodwill)。
        // 注意 MaxGoodwillCap 是静态方法，查找必须带 BindingFlags.Static，否则解析为 null（上限将永远回退 100）。
        Type? goodwillCapType = GenTypes.GetTypeInAnyAssembly("TSA_WorldDomination.GoodwillCapUtility");
        MaxGoodwillCapMethod = goodwillCapType?.GetMethod("MaxGoodwillCap",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
    }

    /// <summary>是否适配（模组已激活且类型、成员均已解析到位）。</summary>
    private static bool Ready => ModActive && CompType != null && OffensiveStrengthField != null
        && DefensiveStrengthField != null && GetMaxOffensiveStrengthMethod != null && GetBaseDefensiveStrengthMethod != null;

    /// <summary>World Domination 2 是否已安装并激活（设置界面据此置灰相关选项）。</summary>
    internal static bool IsActive => ModActive && CompType != null;

    /// <summary>从世界对象中取 CompViralSpread 组件（WorldObject.GetComponent(Type) 公开方法）；无则返回 null。</summary>
    public static WorldObjectComp? FindComp(WorldObject? wo)
    {
        if (!Ready || wo == null)
        {
            return null;
        }
        try
        {
            return wo.GetComponent(CompType!);
        }
        catch (Exception ex)
        {
            Log.WarningOnce("ASQBetterInspectPane: 查找 World Domination 2 组件失败，已忽略。ex=" + ex.Message, 61802389);
            return null;
        }
    }

    /// <summary>
    /// 读取总强度（进攻 + 防御）与强度上限：上限无效（玩家殖民地上限 0、未知对象类型 float.MaxValue）
    /// 或读取失败时返回 false。两值均已取整口径由调用方处理，此处仅保证非负。
    /// </summary>
    public static bool TryGetStrength(WorldObjectComp comp, out float current, out float max)
    {
        current = 0f;
        max = 0f;
        if (!Ready || comp == null || comp.GetType() != CompType)
        {
            return false;
        }
        try
        {
            float offensive = OffensiveStrengthField!.GetValue(comp) is float o ? o : 0f;
            float defensive = DefensiveStrengthField!.GetValue(comp) is float d ? d : 0f;
            float maxOffensive = GetMaxOffensiveStrengthMethod!.Invoke(comp, null) is float mo ? mo : 0f;
            float baseDefensive = GetBaseDefensiveStrengthMethod!.Invoke(comp, null) is float bd ? bd : 0f;
            current = Math.Max(0f, offensive + defensive);
            max = Math.Max(0f, maxOffensive + baseDefensive);
            // float.MaxValue（该组件挂在不支持强度体系的世界对象上）或非有限值：无有效上限，不显示。
            if (max <= 0f || !float.IsFinite(max) || max > 1e8f)
            {
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("ASQBetterInspectPane: 读取 World Domination 2 强度失败，已忽略。ex=" + ex.Message, 61802390);
            return false;
        }
    }

    /// <summary>
    /// 读取移动商队（WorldObject_Traveler 及其任务子类）的强度：当前 = travelerStrength，
    /// 基数 = initialStrength（出发时强度，与检查文字「Strength: X / Y」口径一致）。
    /// 基数 ≤ 0 / 非有限值（WD2 检查文字按 100% 处理的场景）或读取失败时返回 false。
    /// </summary>
    public static bool TryGetTravelerStrength(WorldObject wo, out float current, out float max)
    {
        current = 0f;
        max = 0f;
        if (!ModActive || TravelerType == null || TravelerStrengthField == null
            || TravelerInitialStrengthField == null || wo == null || !TravelerType.IsInstanceOfType(wo))
        {
            return false;
        }
        try
        {
            current = Math.Max(0f, TravelerStrengthField.GetValue(wo) is float c ? c : 0f);
            max = TravelerInitialStrengthField.GetValue(wo) is float m ? m : 0f;
            if (max <= 0f || !float.IsFinite(max))
            {
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("ASQBetterInspectPane: 读取 World Domination 2 商队强度失败，已忽略。ex=" + ex.Message, 61802391);
            return false;
        }
    }

    /// <summary>关系上限是否可用（模组已激活且 GoodwillCapUtility.MaxGoodwillCap 已解析到位）。</summary>
    internal static bool GoodwillCapReady => ModActive && MaxGoodwillCapMethod != null;

    /// <summary>
    /// 读取 WD2 设置中的关系上限（GoodwillCapUtility.MaxGoodwillCap = max(100, 设置.maxGoodwill，默认 200)）。
    /// WD2 用该值替换原版好感上限 100；每次调用实时读取其设置实例（WD2 设置页可随时改动），失败时返回 false。
    /// </summary>
    internal static bool TryGetGoodwillCap(out int cap)
    {
        cap = 0;
        if (!GoodwillCapReady)
        {
            return false;
        }
        try
        {
            cap = MaxGoodwillCapMethod!.Invoke(null, null) is int c ? c : 0;
            return cap > 0;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("ASQBetterInspectPane: 读取 World Domination 2 关系上限失败，已忽略。ex=" + ex.Message, 61802392);
            return false;
        }
    }
}
