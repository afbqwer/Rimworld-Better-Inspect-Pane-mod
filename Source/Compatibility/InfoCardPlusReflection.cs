using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 通过反射读取 Info Card Plus 的「收藏（pin）」统计，不硬引用该模组 DLL。
/// Info Card Plus 的收藏键格式（StatsPanel.KeyOf）为「StatCategoryDef.defName + "/" + StatDrawEntry.LabelCap」；
/// 为与信息卡显示完全一致（含其 WeaponDps / BuildingYield / RecipeYield / FactionInfo 改写），
/// 经由该模组自身的 StatSubject.Entries 生成条目列表，再按收藏键过滤。
/// 仅处理 Thing；Pawn 是否纳入由 enableExtraPawnText + enableInfoCardPlusPinsPawn 控制（ICP 的 StatSubject.Entries
/// 对 Pawn 走 thing 分支并调用原版 StatsReportUtility.StatsToDraw(Thing)，统计口径与其信息卡一致）。
/// Bounded Rationality 同步：Pawn 的 Meta 信息未知（Meta + Control 口径）时收藏文字整体隐藏。
/// Info Card Plus 未激活 / 类型或成员未解析到位时全部安全返回 false / 空。
/// dll位置：D:\SteamLibrary\steamapps\workshop\content\294100\3776660810\1.6\Assemblies
/// </summary>
public static class InfoCardPlusReflection
{
    private const string ModPackageId = "kaamalauppias.infocardplus";

    private const string BetterInfoUIModTypeFullName = "BetterInfoUI.BetterInfoUIMod";
    private const string BetterInfoUISettingsTypeFullName = "BetterInfoUI.BetterInfoUISettings";
    private const string StatSubjectTypeFullName = "BetterInfoUI.StatSubject";

    // 廉价检查收藏集合(Pins)是否变化的间隔帧数；仅用于感知用户在 ICP 里增删收藏。
    // StatSubject.Entries 的全量重算不再按帧进行（事件驱动：换选 / Pins 变化时才重建）。
    private const int PinTextCacheIntervalFrames = 30;

    private static readonly bool ModActive = ModsConfig.IsActive(ModPackageId);

    private static readonly Type? BetterInfoUIModType;
    private static readonly PropertyInfo? SettingsProp;
    private static readonly FieldInfo? PinsField;
    private static readonly Type? StatSubjectType;
    private static readonly FieldInfo? ThingField;
    private static readonly PropertyInfo? EntriesProp;

    static InfoCardPlusReflection()
    {
        if (!ModActive)
        {
            return;
        }
        BetterInfoUIModType = GenTypes.GetTypeInAnyAssembly(BetterInfoUIModTypeFullName);
        if (BetterInfoUIModType == null)
        {
            return;
        }
        SettingsProp = BetterInfoUIModType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
        Type? settingsType = GenTypes.GetTypeInAnyAssembly(BetterInfoUISettingsTypeFullName);
        PinsField = settingsType?.GetField("Pins", BindingFlags.Public | BindingFlags.Instance);
        StatSubjectType = GenTypes.GetTypeInAnyAssembly(StatSubjectTypeFullName);
        ThingField = StatSubjectType?.GetField("thing", BindingFlags.Public | BindingFlags.Instance);
        EntriesProp = StatSubjectType?.GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance);
    }

    /// <summary>是否适配（Info Card Plus 激活且所需成员均已解析到位）。</summary>
    private static bool Ready => ModActive && SettingsProp != null && PinsField != null
        && StatSubjectType != null && ThingField != null && EntriesProp != null;

    /// <summary>Info Card Plus 是否已安装并激活（设置界面据此置灰相关选项）。</summary>
    internal static bool IsActive => ModActive;

    // 收藏文字与去重用 StatDef 集合缓存。
    // 失效时机 = 换选 或 收藏集合(Pins)内容变化（值冻结语义：同一对象选中期间数值保持快照，
    // 不周期重建 StatSubject.Entries）；帧号仅用于低频检查 Pins 是否被改动，省去逐帧枚举。
    private static ISelectable? cachedSel;
    private static int cachedCheckFrame = -1000;
    private static string cachedPinText = "";
    private static readonly HashSet<StatDef> cachedPinStats = new HashSet<StatDef>();

    // 缓存的 ICP 设置实例与 StatSubject：settings/Pins 在运行期是长命对象，可长期持有；
    // 复用同一 StatSubject 使其内部 Entries 缓存生效，重算只发生在换选（新建实例）时。
    private static object? cachedSettingsInstance;
    private static object? cachedSubject;
    private static int cachedPinCount = -1; // 生成文本所用 Pins 的指纹（count + hash），用于廉价判断收藏是否变化
    private static int cachedPinHash;

    /// <summary>
    /// 取当前选中对象（Thing；Pawn 需开启 enableExtraPawnText 与 enableInfoCardPlusPinsPawn）
    /// 在 Info Card Plus 中收藏的 Stat 文字；无则返回空串。
    /// Bounded Rationality 同步：Pawn 的 Meta 信息未知（原版信息卡按钮被 BR 隐藏，Meta + Control 口径）
    /// 时，收藏 Stat 文字一并隐藏（收藏内容与其信息卡一致，属 Meta 信息）。
    /// 同时通过 pinnedStats 输出「已被收藏且本对象确实显示」的 StatDef 集合（供本模组额外文字去重）。
    /// 统计生成采用「事件驱动 + 值冻结」：
    ///   - 换选：重建 StatSubject（Entries 全量重算，每次选中仅一次）；
    ///   - 同对象、Pins 内容变化：复用已缓存的 Entries 仅重过滤（不重建）；
    ///   - 同对象、Pins 未变：直接返回缓存（每帧零开销）。
    /// 同一对象持续选中期间数值保持为快照，与 ICP 官方信息卡口径一致；每
    /// PinTextCacheIntervalFrames 帧只做一次廉价 Pins 指纹检查以感知用户增删收藏。
    /// </summary>
    internal static string GetPinnedText(ISelectable? sel, out HashSet<StatDef> pinnedStats)
    {
        pinnedStats = cachedPinStats;
        if (!Ready || !MyModTemplateSettings.enabled || !MyModTemplateSettings.enableInfoCardPlusPins
            || sel is not Thing thing
            || (thing is Pawn && (!MyModTemplateSettings.enableExtraPawnText || !MyModTemplateSettings.enableInfoCardPlusPinsPawn))
            || (thing is Pawn brPawn && !BoundedRationalityReflection.IsInfoCardInfoKnown(brPawn)))
        {
            ResetPinnedCache();
            return cachedPinText;
        }
        // 同对象且未到廉价检查周期：直接返回缓存（不做任何反射 / Entries 访问）。
        if (ReferenceEquals(cachedSel, sel)
            && Time.frameCount - cachedCheckFrame < PinTextCacheIntervalFrames)
        {
            return cachedPinText;
        }
        cachedCheckFrame = Time.frameCount;

        try
        {
            // 设置实例在运行期是同一个长命对象，只反射取值一次后缓存，避免每次 GetValue。
            if (cachedSettingsInstance == null)
            {
                if (SettingsProp!.GetValue(null) is not { } settingsInstance)
                {
                    ResetPinnedCache();
                    return cachedPinText;
                }
                cachedSettingsInstance = settingsInstance;
            }
            if (PinsField!.GetValue(cachedSettingsInstance!) is not HashSet<string> pins || pins.Count == 0)
            {
                // 当前无收藏：清空文字与去重集合。同对象场景保留 subject，便于随后再收藏时仅重过滤。
                cachedPinText = "";
                cachedPinStats.Clear();
                cachedPinCount = -1;
                cachedPinHash = 0;
                if (!ReferenceEquals(cachedSel, sel))
                {
                    cachedSel = sel;
                    cachedSubject = null;
                }
                return cachedPinText;
            }

            bool selChanged = !ReferenceEquals(cachedSel, sel);
            if (selChanged || cachedSubject == null)
            {
                // 换选 / 首次需要：新建 StatSubject（实例化使 ICP 内部 Entries 缓存失效 → 全量重算）。
                object? subject = Activator.CreateInstance(StatSubjectType!);
                if (subject == null)
                {
                    ResetPinnedCache();
                    return cachedPinText;
                }
                // MinifiedThing 解包由 StatSubject.Entries 内部处理，此处直接传入选中对象即可。
                ThingField!.SetValue(subject, thing);
                cachedSubject = subject;
                cachedSel = sel;
                cachedPinCount = -1; // 强制下面重建文本
                cachedPinHash = 0;
            }

            int pinCount = pins.Count;
            int pinHash = ComputePinsFingerprint(pins);
            if (pinCount == cachedPinCount && pinHash == cachedPinHash)
            {
                return cachedPinText; // 同对象、收藏未变：值冻结，文本与去重集合原样返回。
            }

            // 同对象复用 subject 时，Entries 返回的是 ICP 已缓存的列表，不会重算统计。
            if (EntriesProp!.GetValue(cachedSubject!) is not IEnumerable entries)
            {
                ResetPinnedCache();
                return cachedPinText;
            }
            RebuildPinnedText(entries, pins);
            cachedPinCount = pinCount;
            cachedPinHash = pinHash;

            // TEMP-DEBUG
            //DumpPawnCombatDebug(thing, pins, entries);
        }
        catch (Exception ex)
        {
            // 生成失败按无收藏文字处理，并清空去重集合避免误伤额外文字。
            Log.WarningOnce("ASQBetterInspectPane: 生成 Info Card Plus 收藏统计失败，已忽略。ex=" + ex.Message, 61802378);
            ResetPinnedCache();
        }
        return cachedPinText;
    }

    /// <summary>依据「当前收藏集合」从条目列表中筛出收藏行，重建文字与去重集合。</summary>
    private static void RebuildPinnedText(IEnumerable entries, HashSet<string> pins)
    {
        bool highlight = MyModTemplateSettings.enableInfoCardPlusPinsHighlight;
        StringBuilder sb = new StringBuilder();
        cachedPinStats.Clear();
        foreach (object obj in entries)
        {
            if (obj is not StatDrawEntry entry)
            {
                continue;
            }
            string label;
            string value;
            try
            {
                label = entry.LabelCap;
                value = entry.ValueString;
            }
            catch (Exception ex)
            {
                // 个别 mod 的统计可能在取值时抛异常；跳过该条（不拖垮面板）。
                Log.WarningOnce("ASQBetterInspectPane: 读取 Info Card Plus 收藏统计失败，已忽略。ex=" + ex.Message, 61802377);
                continue;
            }
            if (label.NullOrEmpty() || value.NullOrEmpty())
            {
                continue;
            }
            string category = entry.category != null ? entry.category.defName : "?";
            if (!pins.Contains(category + "/" + label))
            {
                continue;
            }
            if (entry.stat != null)
            {
                cachedPinStats.Add(entry.stat);
            }
            TaggedString line = (highlight
                ? "BetterInspectPane.InfoCardPlusPinnedLineHighlight"
                : "BetterInspectPane.InfoCardPlusPinnedLine").Translate(label, value);
            // 用 RawText 取回替换后的原文：TaggedString→string 的隐式转换会 StripTags 剥离
            // 富文本标记（<b>/<color>），导致高亮不生效，故此处不能走隐式转换。
            AppendLineTo(sb, line.RawText);
        }
        cachedPinText = sb.ToString();
    }

    // TEMP-DEBUG 字段与方法
    private static bool pawnCombatDumped;
    private static void DumpPawnCombatDebug(Thing thing, HashSet<string> pins, IEnumerable entries)
    {
        if (pawnCombatDumped || !pins.Contains("PawnCombat/远程 DPS"))
        {
            return;
        }
        pawnCombatDumped = true;
        StringBuilder sb = new StringBuilder("ASQDBG pawn=").Append(thing.LabelCap)
            .Append(" def=").Append(thing.def?.defName)
            .Append(" hasGun=").Append(thing is Pawn p && p.equipment != null && p.equipment.Primary != null
                ? p.equipment.Primary.def.defName : "no/melee");
        sb.Append("\npins:");
        foreach (string pin in pins)
        {
            sb.Append(" [").Append(pin).Append(']');
        }
        sb.Append("\nentries:");
        foreach (object o in entries)
        {
            if (o is not StatDrawEntry e)
            {
                sb.Append("\n  <non-StatDrawEntry ").Append(o?.GetType().Name).Append('>');
                continue;
            }
            sb.Append("\n  ").Append(e.category != null ? e.category.defName : "NULLCAT")
                .Append(" | ").Append(e.LabelCap)
                .Append(" | stat=").Append(e.stat != null ? e.stat.defName : "null");
            if (e.stat != null && e.stat.defName.Contains("DPS"))
            {
                sb.Append(" | value=[").Append(DebugValueString(e)).Append(']');
            }
        }
        sb.Append("\ncachedPinText=[").Append(cachedPinText).Append(']');
        Log.Warning(sb.ToString());
    }

    private static string DebugValueString(StatDrawEntry e)
    {
        try
        {
            return e.ValueString;
        }
        catch (Exception ex)
        {
            return "EX:" + ex.GetType().Name + ":" + ex.Message;
        }
    }

    /// <summary>收藏集合的廉价指纹：无序、可碰撞但不影响正确性——极端碰撞仅导致多一次重过滤。</summary>
    private static int ComputePinsFingerprint(HashSet<string> pins)
    {
        int hash = 17;
        foreach (string pin in pins)
        {
            hash = hash * 31 + (pin?.GetHashCode() ?? 0);
        }
        return hash;
    }

    /// <summary>清空全部缓存（模块不可用 / 生成失败时调用，保证下次重新构建）。</summary>
    private static void ResetPinnedCache()
    {
        cachedPinText = "";
        cachedPinStats.Clear();
        cachedSel = null;
        cachedSubject = null;
        cachedPinCount = -1;
        cachedPinHash = 0;
    }

    /// <summary>所给 StatDef 是否为当前选中对象正在显示的收藏统计（用于隐藏本模组重复额外文字）。</summary>
    internal static bool IsPinnedStat(StatDef? stat)
    {
        return stat != null && cachedPinStats.Contains(stat);
    }

    /// <summary>向 StringBuilder 追加一行：非空时先补换行，避免多余空行 / 行首空行。</summary>
    private static void AppendLineTo(StringBuilder sb, string text)
    {
        if (sb.Length > 0)
        {
            sb.Append('\n');
        }
        sb.Append(text);
    }
}
