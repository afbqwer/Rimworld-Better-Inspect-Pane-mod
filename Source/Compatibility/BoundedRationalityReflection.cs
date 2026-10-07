using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Bounded Rationality（packageId：Lakuna.BoundedRationality）的「信息是否已知」判定，
/// 不硬引用该模组 DLL。BR 会按 Pawn 类型 × 信息类别（KnowledgeUtility.IsInformationKnownFor）自动隐藏
/// 原版检查面板的健康、心情、饥饿等区块与信息卡按钮；本模组接管了面板绘制（InspectPanePatch /
/// InspectPaneOnGUIPatch），BR 的补丁不再作用于自定义内容，因此用与 BR 各补丁相同的判定口径同步隐藏：
///   - 健康 / 血液 / 疼痛条：Health（原版 DrawHealth 口径）；
///   - 心情 / 食物 / 休息 / 娱乐 / 机械能量 / 模组需求条：Needs（原版 DrawMood / DrawHunger / DrawMechEnergy 口径）；
///   - 产物 / 产毛 / 繁殖条与策略行的食物 / 管制 / 着装：Basic（原版 DrawTimetableSetting 与组件检查文字口径）；
///   - 区域策略组件：Basic + Control（原版 DrawAreaAllowed 口径，可点击时跟随 BR「不隐藏管制」设置）；
///   - 技能区：Skills（BR 隐藏技能页口径）；且逐条技能按 KnowledgeUtility.IsSkillKnown 过滤
///     （技能与特性一样按等级从高到低、随在殖民地时间逐个解锁），全部未知时整区不显示；
///   - Pawn 额外信息按钮（异种类型 / 阵营 / 文化）：Meta + Control（原版 Widgets.InfoCardButton 补丁口径，
///     与原版信息卡按钮同步显隐）；
///   - 观察文字的逐条特性：KnowledgeUtility.IsTraitKnown（含特性学习解锁条件），全部未知时整行不显示。
/// 判定结果按 Pawn 缓存 60 帧：需要隐藏信息的单选面板总是只显示单个 Pawn 的信息，
/// 选中 Pawn 未变且未超间隔时直接复用上次结果（六项口径一次算齐），避免每帧多次反射调用；
/// BR 设置改动最迟约 1 秒后生效。类别 / 控制类别枚举按成员顺序镜像 BR 的 InformationCategory /
/// ControlCategory，调用时用 Enum.ToObject 还原为 BR 枚举实例；方法按「名称 + 3 参数 + 参数类型」解析，
/// 兼容 BR 各版本构建。模组未激活 / 类型或成员未解析到位 / 调用失败时一律返回 true（照常显示），
/// 保持未装 BR 时的原行为。
/// BR 源码：https://github.com/Lakuna/RimWorld-Bounded-Rationality（KnowledgeUtility.IsInformationKnownFor）。
/// </summary>
public static class BoundedRationalityReflection
{
    private const string ModPackageId = "Lakuna.BoundedRationality";
    private const string KnowledgeUtilityTypeFullName = "Lakuna.BoundedRationality.Utility.KnowledgeUtility";

    /// <summary>BR 信息类别（镜像 Lakuna.BoundedRationality.Utility.InformationCategory，成员顺序一致）。</summary>
    internal enum BRInfoCategory
    {
        Basic, Health, Needs, Gear, Skills, Traits, Abilities, Backstory, Social, Ideoligion, Personal, Meta
    }

    /// <summary>BR 控制类别（镜像 Lakuna.BoundedRationality.Utility.ControlCategory，成员顺序一致）。</summary>
    internal enum BRControlCategory
    {
        Default, Control, TextMote, Message, Letter, Alert
    }

    /// <summary>判定结果缓存间隔帧数（60 帧约 1 秒）。</summary>
    private const int CacheIntervalFrames = 60;

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);

    // KnowledgeUtility.IsInformationKnownFor(InformationCategory, Pawn, ControlCategory) 的解析结果。
    private static readonly MethodInfo? PawnKnownMethod;
    private static readonly Type? InfoCategoryType;
    private static readonly Type? ControlCategoryType;

    // KnowledgeUtility.IsTraitKnown(Trait) 的解析结果（特性逐条判定，含特性学习解锁条件）。
    private static readonly MethodInfo? TraitKnownMethod;

    // KnowledgeUtility.IsSkillKnown(SkillRecord) 的解析结果（技能逐条判定，含按等级逐个解锁的学习机制）。
    private static readonly MethodInfo? SkillKnownMethod;

    // 判定结果缓存：键 = 单选面板当前显示的 Pawn，间隔 60 帧；六项口径一次算齐后按项复用。
    private static Pawn? cachedPawn;
    private static int cachedFrame = -1000;
    private static bool cachedHealth = true;
    private static bool cachedNeeds = true;
    private static bool cachedBasic = true;
    private static bool cachedArea = true;
    private static bool cachedSkills = true;
    private static bool cachedInfoCard = true;

    // 技能逐条判定缓存：键 = 已判定的 SkillRecord（所属 Pawn 记录在键内），随 Pawn 缓存一起失效
    //（RefreshCache 时清空），技能解锁以天为单位变化，60 帧内的陈旧值无感知差异。
    private static readonly Dictionary<SkillRecord, bool> cachedSkillKnown = new Dictionary<SkillRecord, bool>();

    static BoundedRationalityReflection()
    {
        if (!ModActive)
        {
            return;
        }
        Type? utilityType = GenTypes.GetTypeInAnyAssembly(KnowledgeUtilityTypeFullName);
        if (utilityType == null)
        {
            return;
        }
        foreach (MethodInfo method in utilityType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            ParameterInfo[] parameters = method.GetParameters();
            // 只认 3 参数重载（类别, Pawn, 控制类别）：显式传控制类别即可覆盖 BR 全部判定分支。
            if (method.Name == "IsInformationKnownFor"
                && PawnKnownMethod == null
                && parameters.Length == 3
                && parameters[0].ParameterType.IsEnum
                && parameters[2].ParameterType.IsEnum
                && parameters[1].ParameterType == typeof(Pawn))
            {
                PawnKnownMethod = method;
                InfoCategoryType = parameters[0].ParameterType;
                ControlCategoryType = parameters[2].ParameterType;
            }
            else if (method.Name == "IsTraitKnown"
                && TraitKnownMethod == null
                && parameters.Length == 1
                && parameters[0].ParameterType == typeof(Trait))
            {
                TraitKnownMethod = method;
            }
            else if (method.Name == "IsSkillKnown"
                && SkillKnownMethod == null
                && parameters.Length == 1
                && parameters[0].ParameterType == typeof(SkillRecord))
            {
                SkillKnownMethod = method;
            }
        }
    }

    /// <summary>是否已接入 BR（模组激活且 KnowledgeUtility 判定方法解析到位）。</summary>
    internal static bool Ready => PawnKnownMethod != null;

    /// <summary>
    /// 单个特性是否已知：BR 同步判定（KnowledgeUtility.IsTraitKnown，含 BR 的特性学习解锁条件）；
    /// 未接入 BR / 方法未解析到位 / 调用失败时返回 true（照常显示）。
    /// 观察文字的特性行由调用方逐条过滤：全部未知时 names 为空、整行不显示。
    /// </summary>
    internal static bool IsTraitKnown(Trait? trait)
    {
        if (trait == null || TraitKnownMethod == null)
        {
            return true;
        }
        try
        {
            return TraitKnownMethod.Invoke(null, new object?[] { trait }) is bool known && known;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("ASQBetterInspectPane: 调用 Bounded Rationality 特性判定失败，本次按已知处理。ex=" + ex.Message, 81752342);
            return true;
        }
    }

    /// <summary>
    /// 单个技能是否已知：BR 同步判定（KnowledgeUtility.IsSkillKnown —— Skills 类别未知时恒为未知；
    /// 学习未开启时直接已知，学习开启时按等级从高到低、随在殖民地时间逐个解锁）。
    /// 结果记入技能缓存（随 Pawn 缓存失效：换 Pawn 或超 60 帧时清空重判），
    /// 未接入 BR / 方法未解析到位 / 调用失败时返回 true（照常显示）。
    /// </summary>
    internal static bool IsSkillKnown(SkillRecord? skill)
    {
        if (skill == null || SkillKnownMethod == null)
        {
            return true;
        }
        // 判定挂在 Pawn 缓存上：缓存不是该技能所属 Pawn 或超过间隔时先刷新（同时清空技能缓存）。
        Pawn? owner = skill.Pawn;
        if (owner == null || cachedPawn != owner || Time.frameCount - cachedFrame >= CacheIntervalFrames)
        {
            if (owner != null)
            {
                RefreshCache(owner);
            }
            // 无所属 Pawn 的异常记录不做缓存，直接判定。
            return IsSkillKnownDirect(skill);
        }
        if (cachedSkillKnown.TryGetValue(skill, out bool known))
        {
            return known;
        }
        known = IsSkillKnownDirect(skill);
        cachedSkillKnown[skill] = known;
        return known;
    }

    /// <summary>单次反射判定技能：调用失败时返回 true（照常显示）并记一次性警告。</summary>
    private static bool IsSkillKnownDirect(SkillRecord skill)
    {
        try
        {
            return SkillKnownMethod!.Invoke(null, new object?[] { skill }) is bool known && known;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("ASQBetterInspectPane: 调用 Bounded Rationality 技能判定失败，本次按已知处理。ex=" + ex.Message, 81752343);
            return true;
        }
    }

    // ---------- 各面板区块判定（口径与 BR 对原版面板的补丁一致，未接入 BR 时恒为 true） ----------

    /// <summary>健康信息是否已知：健康 / 血液 / 疼痛条（原版 DrawHealth 口径）。</summary>
    internal static bool IsHealthKnown(Pawn? pawn) => IsKnownCached(BRInfoCategory.Health, pawn);

    /// <summary>需求信息是否已知：心情 / 食物 / 休息 / 娱乐 / 机械能量 / 模组需求条（原版 DrawMood / DrawHunger / DrawMechEnergy 口径）。</summary>
    internal static bool IsNeedsKnown(Pawn? pawn) => IsKnownCached(BRInfoCategory.Needs, pawn);

    /// <summary>基础信息是否已知：产物 / 产毛 / 繁殖条与策略行的食物 / 管制 / 着装（原版 DrawTimetableSetting 与组件检查文字口径）。</summary>
    internal static bool IsBasicKnown(Pawn? pawn) => IsKnownCached(BRInfoCategory.Basic, pawn);

    /// <summary>区域信息是否已知：策略行区域组件（原版 DrawAreaAllowed 的 Basic + Control 口径）。</summary>
    internal static bool IsAreaKnown(Pawn? pawn) => IsKnownCached(BRInfoCategory.Basic, pawn, BRControlCategory.Control);

    /// <summary>技能信息是否已知：技能区（BR 隐藏技能页口径）。</summary>
    internal static bool IsSkillsKnown(Pawn? pawn) => IsKnownCached(BRInfoCategory.Skills, pawn);

    /// <summary>
    /// Pawn 额外信息按钮（异种类型 / 阵营 / 文化）是否显示：与原版信息卡按钮同一口径（Meta + Control），
    /// BR 隐藏原版信息卡按钮时一并隐藏额外按钮，保持面板按钮区同步。
    /// </summary>
    internal static bool IsInfoCardInfoKnown(Pawn? pawn) => IsKnownCached(BRInfoCategory.Meta, pawn, BRControlCategory.Control);

    /// <summary>Pawn 进度条是否随 BR 隐藏：按条类型映射 BR 信息类别（未接入 BR / 未知条类型时恒显示）。</summary>
    internal static bool IsPawnBarKnown(MyModTemplateSettings.BarType type, Pawn pawn) => type switch
    {
        MyModTemplateSettings.BarType.Health
            or MyModTemplateSettings.BarType.Bleed
            or MyModTemplateSettings.BarType.Pain => IsHealthKnown(pawn),
        MyModTemplateSettings.BarType.Mood
            or MyModTemplateSettings.BarType.Food
            or MyModTemplateSettings.BarType.Rest
            or MyModTemplateSettings.BarType.Joy
            or MyModTemplateSettings.BarType.MechEnergy
            or MyModTemplateSettings.BarType.Bladder
            or MyModTemplateSettings.BarType.Hygiene
            or MyModTemplateSettings.BarType.Thirst => IsNeedsKnown(pawn),
        MyModTemplateSettings.BarType.Milk
            or MyModTemplateSettings.BarType.Wool
            or MyModTemplateSettings.BarType.Breeding => IsBasicKnown(pawn),
        _ => true
    };

    /// <summary>
    /// 带缓存的判定：pawn 为空 / 未接入 BR 时返回 true（照常显示）；否则按 Pawn 缓存 60 帧内复用
    /// （单选面板同一时间只显示一个 Pawn 的信息，切换 Pawn 或超间隔时六项口径一次算齐）。
    /// </summary>
    private static bool IsKnownCached(BRInfoCategory category, Pawn? pawn, BRControlCategory control = BRControlCategory.Default)
    {
        if (pawn == null || PawnKnownMethod == null)
        {
            return true;
        }
        if (cachedPawn != pawn || Time.frameCount - cachedFrame >= CacheIntervalFrames)
        {
            RefreshCache(pawn);
        }
        return category switch
        {
            BRInfoCategory.Health => cachedHealth,
            BRInfoCategory.Needs => cachedNeeds,
            // Basic 同时承担「基础信息」（Default）与「区域」（Control，可点击口径）两种控制类别。
            BRInfoCategory.Basic => control == BRControlCategory.Control ? cachedArea : cachedBasic,
            BRInfoCategory.Skills => cachedSkills,
            // Meta 仅用于额外信息按钮（Control 口径）。
            BRInfoCategory.Meta => cachedInfoCard,
            _ => true
        };
    }

    /// <summary>刷新缓存：按当前 Pawn 一次算齐面板用到的六项判定口径，并清空技能逐条缓存。</summary>
    private static void RefreshCache(Pawn pawn)
    {
        cachedPawn = pawn;
        cachedFrame = Time.frameCount;
        cachedHealth = IsKnownDirect(BRInfoCategory.Health, pawn, BRControlCategory.Default);
        cachedNeeds = IsKnownDirect(BRInfoCategory.Needs, pawn, BRControlCategory.Default);
        cachedBasic = IsKnownDirect(BRInfoCategory.Basic, pawn, BRControlCategory.Default);
        cachedArea = IsKnownDirect(BRInfoCategory.Basic, pawn, BRControlCategory.Control);
        cachedSkills = IsKnownDirect(BRInfoCategory.Skills, pawn, BRControlCategory.Default);
        cachedInfoCard = IsKnownDirect(BRInfoCategory.Meta, pawn, BRControlCategory.Control);
        cachedSkillKnown.Clear();
    }

    /// <summary>单次反射判定：调用失败时返回 true（照常显示）并记一次性警告。</summary>
    private static bool IsKnownDirect(BRInfoCategory category, Pawn pawn, BRControlCategory control)
    {
        try
        {
            object categoryValue = Enum.ToObject(InfoCategoryType!, (int)category);
            object controlValue = Enum.ToObject(ControlCategoryType!, (int)control);
            return PawnKnownMethod!.Invoke(null, new[] { categoryValue, (object)pawn, controlValue }) is bool known && known;
        }
        catch (Exception ex)
        {
            Log.WarningOnce("ASQBetterInspectPane: 调用 Bounded Rationality 信息判定失败，本次按已知处理。ex=" + ex.Message, 81752341);
            return true;
        }
    }
}
