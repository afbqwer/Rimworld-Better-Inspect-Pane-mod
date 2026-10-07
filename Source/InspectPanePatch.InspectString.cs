using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using HarmonyLib;
using static ASQBetterInspectPane.MyModTemplateSettings;

namespace ASQBetterInspectPane;

/// <summary>
/// InspectPanePatch 的 InspectString 绘制部分：本模组自己的 DrawInspectStringFor 实现。
/// 接管「拼接文本」这一步：按 30 帧缓存文本与其 CalcHeight，并据此实现自适应面板高度。
/// 文本 = 原版 GetInspectString（+ 低优先级段）+ 本模组额外信息文字（普通对象：所属势力 /
/// 市场价值 / 武器 / 衣物护甲 / 营养 / 美观 / 舒适度 / CE 高度；Pawn：CE 高度；世界地图空地砖：
/// 靠近污染与势力；世界地图对象：自然阵营关系；派系定居点：派系类别（置于原版文字之前），
/// 额外文字由 GetExtraText 统一计算、拼装时并入，测量与绘制共用同一份文本
/// （绘制走 DrawInspectStringText），保证高度与实际显示一致。
/// 不使用 Harmony Prefix，直接由 InspectPanePatch.Prefix 调用。
/// </summary>
public static partial class InspectPanePatch
{
    // InspectString 文本缓存间隔帧数（本功能独立常量，不影响现有区域/储存/电网的 30 帧缓存）。
    private const int InspectStringCacheIntervalFrames = 30;

    // LabelScrollable 内部始终为滚动条预留 16px 宽，并按 rect.width - 16 测量/排版文本；
    // 自适应高度须与之一致地测量，否则会低估所需高度导致滚动条出现。
    private const float ScrollbarSpaceWidth = 16f;
    // 高度余量：吸收测量与绘制上下文（字号/换行/缩放）的微小差异，避免差几像素仍出现滚动条。
    private const float AdaptiveHeightPadding = 2f;
    // 自适应面板高度的上限（像素）：所需高度超过该值时截断，避免文本过长时面板过度撑高。
    private const float MaxAdaptivePaneHeight = 600f;

    // InspectString 文本与测量高度缓存：文本因缓存不变，故其高度一并缓存
    // （按选中对象 + 帧数 + 文本宽度三重判定，任一变化即重算）。
    private static ISelectable? cachedInspectStringOwner;
    private static int cachedInspectStringFrame = -1000;
    private static float cachedInspectStringWidth = -1f;
    private static string cachedInspectString = "";
    private static float cachedInspectStringHeight;

    // 自适应高度的锁定目标：同一目标下高度只增不减（防止面板高度抖动），
    // 目标切换时立即按新目标的所需高度重新计算（此时可缩小）。
    private static ISelectable? adaptiveHeightTarget;

    /// <summary>
    /// 本模组自己的 DrawInspectStringFor：取缓存的 InspectString 并按需调整面板高度，随后交 DrawInspectString 绘制。
    /// 仅在本模组自定义面板路径（InspectPanePatch.Prefix）中调用。
    /// </summary>
    private static void DrawInspectStringFor(ISelectable sel, Rect rect)
    {
        RefreshInspectStringCache(sel, rect.width);

        if (adaptivePaneHeight)
        {
            // 自适应面板高度：所需高度 = 当前面板高度 + 文字不足量。
            // 数学上恒等于「固定内容高(标题/条/页脚) + 文字高」，与当前面板高度无关，
            // 因此不会来回振荡。rect 为信息文字区（其 yMax 已在 Prefix 中减去右下角设置按钮行的高度），
            // 故不足量已把设置按钮行计入。
            // 只增不减：同一目标下文本高度波动（如统计数值逐帧/逐缓存刷新变化）只允许面板被撑高、
            // 不立即缩回，待目标切换时才按新目标的所需高度重新计算（此时可缩小），防止面板高度抖动。
            float currentPaneHeight = Mathf.Max(InspectPaneUtility.PaneHeight, EffectivePaneHeight);
            float deficit = cachedInspectStringHeight + AdaptiveHeightPadding - rect.height;
            // 上限截断：文本过长时高度封顶在 MaxAdaptivePaneHeight，超出部分走面板内滚动。
            float required = Mathf.Min(MaxAdaptivePaneHeight, currentPaneHeight + deficit);
            if (adaptiveHeightTarget != sel)
            {
                requiredPaneHeight = required; // 目标切换：立即按新目标所需高度（可缩回）
                adaptiveHeightTarget = sel;
            }
            else if (required > requiredPaneHeight)
            {
                requiredPaneHeight = required; // 同一目标：只增不减
            }
        }
        else if (requiredPaneHeight > 0f)
        {
            ResetAdaptivePaneHeight(); // 关闭自适应时复位
        }

        // 绘制走共用实现 DrawInspectStringText（字号 + 滚动）；额外文字已在拼装阶段
        // 并入 cachedInspectString，直接绘制即可，不再经过被接管的 DrawInspectString，避免重复追加。
        DrawInspectStringText(cachedInspectString, rect);
    }

    /// <summary>
    /// 复位自适应面板高度状态：清空所需高度与锁定目标。
    /// 自适应高度仅在自定义绘制路径（DrawInspectStringFor）中更新；当选择不经过该路径
    /// （如 Pawn，ShouldDrawCustomPane 对 Pawn 返回 false）时必须复位，否则
    /// requiredPaneHeight 会一直保留上一个物品被撑高的值，导致面板无法缩回。
    /// </summary>
    internal static void ResetAdaptivePaneHeight()
    {
        requiredPaneHeight = 0f;
        adaptiveHeightTarget = null;
    }

    /// <summary>
    /// 取当前正在显示检查面板的选中对象（世界地图用世界选择器，否则用地图选择器）。
    /// 供面板尺寸入口在绘制前校正自适应高度状态使用（不依赖 DrawInspectString 是否被调用，
    /// 因此对覆盖了 Pawn 检查面板、根本不走 DrawInspectString 的 mod 也可靠）。
    /// 世界判定必须用 WorldRendererUtility.WorldSelected：从殖民地地图打开世界视图时
    /// Find.CurrentMap 并不为 null（地图仍处于加载状态），按 CurrentMap 判定会误读地图选择器里
    /// 残留的选择（如 Pawn），导致世界对象的自适应高度状态被反复复位、面板无法撑高。
    /// </summary>
    internal static ISelectable? GetCurrentSelected()
    {
        if (WorldRendererUtility.WorldSelected)
        {
            return Find.WorldSelector.FirstSelectedObject as ISelectable;
        }
        return Find.Selector.FirstSelectedObject as ISelectable;
    }

    /// <summary>
    /// 把自适应高度状态校正到当前选择：仅当当前选择仍是自适应路径正在维护
    /// （DrawInspectStringFor 已锁定）的同一对象时才保持锁定，否则立即复位，
    /// 避免 requiredPaneHeight 保留上一个对象被撑高的值导致面板无法缩回。
    /// </summary>
    internal static void SyncAdaptivePaneHeight(ISelectable? sel)
    {
        if (!ReferenceEquals(sel, adaptiveHeightTarget))
        {
            ResetAdaptivePaneHeight();
        }
    }


    /// <summary>
    /// 按 InspectStringCacheIntervalFrames 帧刷新 InspectString 缓存：拼接逻辑与原版 DrawInspectStringFor 一致
    /// （GetInspectString + Thing 低优先级段，含异常保护），随后并入额外信息文字
    /// （GetExtraText：普通对象所属势力/价值/武器/衣物护甲/营养，世界地图砖）；文本不变则 CalcHeight 一并缓存。
    /// 测量口径与 LabelScrollable 一致：按「宽度 - 16（滚动条占位）」换行测量，保证面板撑高后不出现滚动条。
    /// </summary>
    private static void RefreshInspectStringCache(ISelectable sel, float width)
    {
        if (cachedInspectStringOwner == sel
            && Mathf.Abs(cachedInspectStringWidth - width) < 0.5f
            && Time.frameCount - cachedInspectStringFrame < InspectStringCacheIntervalFrames)
        {
            return;
        }
        cachedInspectStringOwner = sel;
        cachedInspectStringFrame = Time.frameCount;
        cachedInspectStringWidth = width;

        string text;
        try
        {
            text = sel.GetInspectString();
            if (sel is Thing thing)
            {
                string inspectStringLowPriority = thing.GetInspectStringLowPriority();
                if (!inspectStringLowPriority.NullOrEmpty())
                {
                    if (!text.NullOrEmpty())
                    {
                        text = text.TrimEndNewlines() + "\n";
                    }
                    text += inspectStringLowPriority;
                }
            }
        }
        catch (Exception ex)
        {
            text = $"GetInspectString exception on {sel}:\n{ex}";
            Log.ErrorOnce(text, 837163520); // 沿用原版一次性错误日志口径
        }
        cachedInspectString = text ?? "";

        // 额外信息文字由 GetExtraText 统一计算；Info Card Plus 收藏 Stat 文字由
        // InfoCardPlusReflection 计算；派系定居点类别（置于原版文字之前）由 GetFactionDefLabelText
        // 计算；三者与原文按位置设置并入（AssembleInspectString），保证测量与绘制是同一份文本。
        string extra = GetExtraText(sel);
        string pinned = InfoCardPlusReflection.GetPinnedText(sel, out _);
        string pre = GetFactionDefLabelText(sel);
        cachedInspectString = AssembleInspectString(sel, cachedInspectString, extra, pinned, pre);

        // 文本已缓存不变 → 高度一并缓存（与绘制同字号测量，保证算出的高度即实际绘制高度）。
        using (new FontSizeScope(infoFontSize))
        {
            bool prevWordWrap = Text.WordWrap;
            Text.WordWrap = true; // InspectString 在 LabelScrollable 中按换行排版
            cachedInspectStringHeight = Text.CalcHeight(cachedInspectString, Mathf.Max(1f, width - ScrollbarSpaceWidth));
            Text.WordWrap = prevWordWrap;
        }
    }

    #region 额外信息文字

    // GetExtraText 结果缓存：该方法被两条路径调用——自定义绘制路径（RefreshInspectStringCache）
    // 按 InspectStringCacheIntervalFrames 帧刷新一次，以及被接管的原版 DrawInspectString
    // 路径（DrawInspectStringPatch.Prefix）每帧一次；后者逐帧调用，必须在此层缓存，
    // 避免每帧重建 StringBuilder 并重复读取统计。
    // 键含世界砖 id：切换空白世界砖时 sel 不变，仅按 sel 缓存会读到旧砖文本。
    private static ISelectable? cachedExtraOwner;
    private static int cachedExtraTileId = -1;
    private static int cachedExtraFrame = -1000;
    private static string cachedExtraText = "";

    /// <summary>
    /// 计算需追加的额外信息文字（世界地图空地砖 / 世界地图对象 / 普通对象），无则返回空串。
    /// 由本路径（InspectString 接管时拼装并入）与被接管的原版 DrawInspectString 路径共用，
    /// （DrawInspectStringPatch 在追加到末尾时也调用本方法），保证测量与绘制共用同一份文本。
    /// 结果按「选中对象 + 世界砖 id + 帧数」缓存（InspectStringCacheIntervalFrames 帧），两条调用路径共用同一份缓存。
    /// </summary>
    internal static string GetExtraText(ISelectable? sel)
    {
        PlanetTile tile = Find.WorldSelector.SelectedTile;
        if (ReferenceEquals(cachedExtraOwner, sel)
            && cachedExtraTileId == tile.tileId
            && Time.frameCount - cachedExtraFrame < InspectStringCacheIntervalFrames)
        {
            return cachedExtraText;
        }
        cachedExtraOwner = sel;
        cachedExtraTileId = tile.tileId;
        cachedExtraFrame = Time.frameCount;

        // 确保 Info Card Plus 收藏 Stat 的 StatDef 集合为当前选中对象的最新值，
        // 供下方额外文字生成时去重（收藏项优先，隐藏本模组重复行）。
        InfoCardPlusReflection.GetPinnedText(sel, out _);

        StringBuilder sb = new StringBuilder();
        if (WorldRendererUtility.WorldSelected)
        {
            GenWorldTileExtra(sb, tile);
            // 世界地图对象：其所属势力的自然阵营关系文字随选中对象一并追加。
            if (sel is WorldObject)
            {
                GenNormalObjectExtra(sb, sel);
            }
        }
        else
        {
            GenNormalObjectExtra(sb, sel);
        }
        cachedExtraText = sb.ToString();
        return cachedExtraText;
    }

    /// <summary>
    /// 以设置字号绘制信息文字（LabelScrollable，沿用原版滚动位置），供本模组 DrawInspectStringFor
    /// 与被接管的 DrawInspectString 共用，保证测量与绘制口径一致。
    /// </summary>
    internal static void DrawInspectStringText(string str, Rect rect)
    {
        // LabelScrollable 内部走 Text.CurFontStyle（Text.CalcHeight / Widgets.Label），
        // 用 FontSizeScope 临时把字号改为像素值、绘制后还原，从而支持任意具体数值。
        using (new FontSizeScope(infoFontSize))
        {
            // 沿用原版滚动位置（private static 字段），保证滚动状态与原版一致。
            Vector2 scrollPos = (Vector2)InspectStringScrollPosField.GetValue(null);
            Widgets.LabelScrollable(rect, str, ref scrollPos, dontConsumeScrollEventsIfNoScrollbar: true);
            InspectStringScrollPosField.SetValue(null, scrollPos);
        }
    }

    // GetFactionDefLabelText 结果缓存：被接管的 DrawInspectString 路径逐帧调用，必须缓存，
    // 避免每帧重复翻译（与 cachedExtra 同理，按「选中对象 + 帧数」缓存）。
    private static ISelectable? cachedFactionDefLabelOwner;
    private static int cachedFactionDefLabelFrame = -1000;
    private static string cachedFactionDefLabelText = "";

    /// <summary>
    /// 派系定居点的派系类别文字（Faction.def.label，即势力类别名，如「外来文明」），置于原版文字之前。
    /// 仅世界地图定居点（Settlement，含玩家定居点）有所属势力时显示；开关 enableExtraFactionDefLabelText
    /// 独立控制。结果按「选中对象 + 帧数」缓存（InspectStringCacheIntervalFrames），两条调用路径共用同一份缓存。
    /// </summary>
    internal static string GetFactionDefLabelText(ISelectable? sel)
    {
        if (ReferenceEquals(cachedFactionDefLabelOwner, sel)
            && Time.frameCount - cachedFactionDefLabelFrame < InspectStringCacheIntervalFrames)
        {
            return cachedFactionDefLabelText;
        }
        cachedFactionDefLabelOwner = sel;
        cachedFactionDefLabelFrame = Time.frameCount;

        cachedFactionDefLabelText = "";
        if (enabled && enableExtraFactionDefLabelText
            && sel is Settlement settlement &&
            settlement.Faction != null && settlement.Faction != Faction.OfPlayerSilentFail
            )
        {
            string? label = settlement.Faction.def?.label;
            if (!label.NullOrEmpty())
            {
                cachedFactionDefLabelText = "BetterInspectPane.FactionDefLabel".Translate(label);
            }
        }
        return cachedFactionDefLabelText;
    }

    #endregion

    #region 额外信息文字（普通对象）

    /// <summary>
    /// 各项是否显示由对应的独立开关控制；市场价值堆叠物品显示总和并用括号标注单价。
    /// Pawn 额外信息由 Pawn 总开关（enableExtraPawnText）下的子开关控制（特性 / CE 高度），
    /// 与普通对象总开关（enableExtraNormalObjectText）互相独立。
    /// </summary>
    private static StringBuilder GenNormalObjectExtra(StringBuilder sb, ISelectable? sel)
    {
        // Pawn：额外项多为物品专用，Pawn 的额外信息由 GenPawnExtra 统一生成
        // （特性 / CE 高度，受 Pawn 总开关 enableExtraPawnText 控制）。
        if (sel is Pawn pawn)
        {
            GenPawnExtra(sb, pawn);
            return sb;
        }
        if (!enabled || !enableExtraNormalObjectText)
        {
            return sb;
        }
        // 世界地图对象：显示所属势力的自然阵营关系（NaturalGoodwill）。
        if (sel is WorldObject wo)
        {
            GenWorldObjectExtra(sb, wo);
            return sb;
        }
        if (sel is not Thing thing)
        {
            return sb;
        }

        // 市场价值：堆叠物品显示总和价值，并用括号标注单价。价值 > 0 才显示。
        // Info Card Plus 已收藏市场价值时，本行让位给收藏版（避免重复显示）。
        if (enableExtraMarketValueText && !InfoCardPlusReflection.IsPinnedStat(StatDefOf.MarketValue))
        {
            if (thing.stackCount > 1)
            {
                float unit = thing.MarketValue;
                float total = unit * thing.stackCount;
                if (total > 0f)
                {
                    // 与游戏一致的金钱格式（MoneyFormat，通常带货币单位，如中文「银」）。
                    // 括号等本地化文字放在翻译串中（占位符 {0}=总和、{1}=单价），便于不同语言排版。
                    AppendLineTo(sb, "BetterInspectPane.MarketValueStackLabel".Translate(total.ToStringMoney(), unit.ToStringMoney()));
                }
            }
            else
            {
                float marketValue = thing.MarketValue;
                if (marketValue > 0f)
                {
                    AppendLineTo(sb, "BetterInspectPane.MarketValueLabel".Translate(marketValue.ToStringMoney()));
                }
            }
        }

        // 所属势力：显示物品所属阵营名称（无所属势力的物品按设置决定是否显示「无」）。
        if (enableExtraFactionText)
        {
            Faction? faction = thing.Faction;
            if (faction != null)
            {
                if (!(hideFactionPlayerText && faction.IsPlayer))
                {
                    AppendLineTo(sb, "BetterInspectPane.FactionLabel".Translate(faction.Name));
                }
            }
            else if (!hideFactionNoneText)
            {
                AppendLineTo(sb, "BetterInspectPane.NoFactionLabel".Translate());
            }
        }

        // 武器：单次伤害（仅远程）、射程（仅远程）、冷却时间（远程为 冷却(+预热)，近战为冷却）、
        // 远程/近战DPS 与护甲穿透（远程武器显示远程护甲穿透，否则显示近战护甲穿透）。
        // 以上数值均按 thing 缓存（EnsureWeaponStatsCached），仅在选择对象变化时重算。
        // 总开关 enableExtraWeaponText 关闭时整体不显示。
        // Info Card Plus 已收藏近战护甲穿透（MeleeWeapon_AverageArmorPenetration）时，本行让位给收藏版。
        bool showWeapon = enableExtraWeaponText
            && (enableExtraWeaponDamageText
                || enableExtraWeaponCooldownText
                || enableExtraWeaponDpsText
                || enableExtraWeaponArmorPenText
                || enableExtraWeaponRangeText);
        if (showWeapon)
        {
            EnsureWeaponStatsCached(thing);
            // Dps
            if (enableExtraWeaponDpsText)
            {
                if (cachedRangedDps > 0f)
                {
                    AppendLineTo(sb, "BetterInspectPane.RangedDPSLabel".Translate(cachedRangedDps.ToString("F1")));
                }
                if (cachedMeleeDps > 0f)
                {
                    AppendLineTo(sb, "BetterInspectPane.MeleeDPSLabel".Translate(cachedMeleeDps.ToString("F1")));
                }
            }

            // 射程：仅远程武器显示（位于冷却时间之上、DPS 之下）。
            if (enableExtraWeaponRangeText && cachedIsRanged && cachedRangedRange > 0f)
            {
                AppendLineTo(sb, "BetterInspectPane.RangedRangeLabel".Translate(cachedRangedRange.ToString("0.##")));
            }

            // 冷却时间：远程武器 = 冷却时间（括号内附预热时间）；近战武器 = 冷却时间。
            if (enableExtraWeaponCooldownText)
            {
                if (cachedIsRanged)
                {
                    if (cachedRangedCooldown > 0f)
                    {
                        AppendLineTo(sb, "BetterInspectPane.RangedCooldownLabel".Translate(
                            cachedRangedCooldown.ToString("F1"), cachedRangedWarmup.ToString("F1")));
                    }
                }
                else if (cachedMeleeCooldown > 0f)
                {
                    AppendLineTo(sb, "BetterInspectPane.MeleeCooldownLabel".Translate(cachedMeleeCooldown.ToString("F2")));
                }
            }

            // 单次伤害：仅远程武器显示（近战不显示）。
            if (enableExtraWeaponDamageText
                && cachedIsRanged && cachedRangedDamagePerShot > 0f)
            {
                AppendLineTo(sb, "BetterInspectPane.DamagePerShotLabel".Translate(cachedRangedDamagePerShot));
            }

            if (enableExtraWeaponArmorPenText)
            {
                float armorPen = cachedIsRanged ? cachedRangedArmorPen : cachedMeleeArmorPen;
                // 近战护甲穿透已被收藏时让位给收藏版（远程护甲穿透无对应 StatDef，不参与去重）。
                if (armorPen > 0f
                    && !InfoCardPlusReflection.IsPinnedStat(cachedIsRanged ? null : MeleeWeaponAverageArmorPenetrationStat))
                {
                    AppendLineTo(sb, (cachedIsRanged ? "BetterInspectPane.RangedArmorPenLabel" : "BetterInspectPane.MeleeArmorPenLabel")
                        .Translate(armorPen.ToString("P0")));
                }
            }
        }

        // 衣物：覆盖服装层与锐器/钝器/热能护甲。护甲数值用 StatDef.ValueToString 格式化，
        // 与原版统计面板口径一致（含材料/品质加成，负数与 0 不显示）。
        if (enableExtraApparelText && thing.def.IsApparel)
        {
            if (enableExtraApparelLayerText)
            {
                // 覆盖的服装层沿用原版 ApparelProperties.GetLayersString（各层 label 逗号连接并大写首字母）。
                AppendLineTo(sb, "BetterInspectPane.ApparelLayersLabel".Translate(thing.def.apparel.GetLayersString()));
            }
            if (enableExtraApparelSharpArmorText && !InfoCardPlusReflection.IsPinnedStat(StatDefOf.ArmorRating_Sharp))
            {
                float sharp = thing.GetStatValue(StatDefOf.ArmorRating_Sharp, true);
                if (sharp > 0f)
                {
                    AppendLineTo(sb, "BetterInspectPane.ApparelSharpArmorLabel".Translate(StatDefOf.ArmorRating_Sharp.ValueToString(sharp)));
                }
            }
            if (enableExtraApparelBluntArmorText && !InfoCardPlusReflection.IsPinnedStat(StatDefOf.ArmorRating_Blunt))
            {
                float blunt = thing.GetStatValue(StatDefOf.ArmorRating_Blunt, true);
                if (blunt > 0f)
                {
                    AppendLineTo(sb, "BetterInspectPane.ApparelBluntArmorLabel".Translate(StatDefOf.ArmorRating_Blunt.ValueToString(blunt)));
                }
            }
            if (enableExtraApparelHeatArmorText && !InfoCardPlusReflection.IsPinnedStat(StatDefOf.ArmorRating_Heat))
            {
                float heat = thing.GetStatValue(StatDefOf.ArmorRating_Heat, true);
                if (heat > 0f)
                {
                    AppendLineTo(sb, "BetterInspectPane.ApparelHeatArmorLabel".Translate(StatDefOf.ArmorRating_Heat.ValueToString(heat)));
                }
            }
        }

        // 食物：营养值（可摄食且营养 > 0 才显示，数值用 StatDef.ValueToString 格式化）。
        // Info Card Plus 已收藏营养值时，本行让位给收藏版。
        if (enableExtraNutritionText && thing.def.IsIngestible && !InfoCardPlusReflection.IsPinnedStat(StatDefOf.Nutrition))
        {
            float nutrition = thing.GetStatValue(StatDefOf.Nutrition, true);
            if (nutrition > 0f)
            {
                AppendLineTo(sb, "BetterInspectPane.NutritionLabel".Translate(StatDefOf.Nutrition.ValueToString(nutrition)));
            }
        }

        // 美观：不等于 0 才显示（正负都显示，如雕刻品正美观、垃圾负美观）。
        // Info Card Plus 已收藏美观时，本行让位给收藏版。
        if (enableExtraBeautyText && !InfoCardPlusReflection.IsPinnedStat(StatDefOf.Beauty))
        {
            float beauty = thing.GetStatValue(StatDefOf.Beauty, true);
            if (beauty != 0f)
            {
                AppendLineTo(sb, "BetterInspectPane.BeautyLabel".Translate(beauty.ToString("+0.##;-0.##;0")));
            }
        }

        // 舒适度：床等物品的舒适度属性（StatDef.Comfort，受品质/材料加成）。仅该属性 > 0 时显示，
        // 数值用 StatDef.ValueToString 格式化，与原版统计面板口径一致。
        // Info Card Plus 已收藏舒适度时，本行让位给收藏版。
        if (enableExtraComfortText && thing is Building && !InfoCardPlusReflection.IsPinnedStat(StatDefOf.Comfort))
        {
            float comfort = thing.GetStatValue(StatDefOf.Comfort, true);
            if (comfort > 0f)
            {
                AppendLineTo(sb, "BetterInspectPane.ComfortLabel".Translate(StatDefOf.Comfort.ValueToString(comfort)));
            }
        }

        // CE 高度：Combat Extended 的竖直碰撞高度（米）。仅当 CE 激活且该物体有有效高度时显示
        // （如站立生物的身高、完整墙壁为 2、植物为图形高度）；高度计算走 CE 自身实现，口径一致。
        if (enableExtraCeHeightText
            && CombatExtendedHeightReflection.TryGetHeight(thing, out float ceHeight))
        {
            AppendLineTo(sb, "BetterInspectPane.CeHeightLabel".Translate(ceHeight.ToString("0.##")));
        }

        return sb;
    }

    /// <summary>
    /// Pawn 额外信息文字：特性 与 CE 高度（Combat Extended 的竖直碰撞高度，米）。
    /// 特性：列出该 Pawn 的全部特性（Trait.LabelCap，与原版角色卡口径一致，含等级/基因特性）；
    /// Bounded Rationality 同步：逐条按 BR「特性是否已知」判定过滤，全部未知时整行不显示。
    /// CE 高度按 CE 自身实现（GetCollisionBodyFactors）计算并叠加蹲伏/飞行修正，
    /// 与其信息卡「掩护高度」口径一致。
    /// 受 Pawn 总开关 enableExtraPawnText 与其下子开关控制；CE 高度仅在 Combat Extended 激活、
    /// 高度有效时显示。
    /// </summary>
    private static void GenPawnExtra(StringBuilder sb, Pawn pawn)
    {
        if (!enabled || !enableExtraPawnText)
        {
            return;
        }

        // 特性：单行列出全部特性（用「, 」连接），与原版角色卡一致显示等级/基因特性标签。
        // Bounded Rationality 同步：未知的特性逐条跳过（全部未知时 names 为空、整行不显示）。
        if (enableExtraPawnTraitText)
        {
            List<Trait>? traits = pawn.story?.traits?.allTraits;
            if (traits != null && traits.Count > 0)
            {
                StringBuilder names = new StringBuilder();
                for (int i = 0; i < traits.Count; i++)
                {
                    Trait trait = traits[i];
                    if (trait == null || !BoundedRationalityReflection.IsTraitKnown(trait))
                    {
                        continue;
                    }
                    string label;
                    try
                    {
                        label = trait.LabelCap;
                    }
                    catch (Exception ex)
                    {
                        // 个别 mod 的特性可能在取值时抛异常；跳过该条（不拖垮面板）。
                        Log.WarningOnce("ASQBetterInspectPane: 读取 Pawn 特性失败，已忽略。ex=" + ex.Message, 13982456);
                        continue;
                    }
                    if (label.NullOrEmpty())
                    {
                        continue;
                    }
                    if (names.Length > 0)
                    {
                        names.Append(", ");
                    }
                    names.Append(label);
                }
                if (names.Length > 0)
                {
                    AppendLineTo(sb, "BetterInspectPane.TraitLabel".Translate(names.ToString()).RawText);
                }
            }
        }

        // CE 高度：Combat Extended 的竖直碰撞高度（米），仅当 CE 激活且该 Pawn 有有效高度时显示。
        if (enableExtraCeHeightPawnText
            && CombatExtendedHeightReflection.TryGetHeight(pawn, out float ceHeight))
        {
            AppendLineTo(sb, "BetterInspectPane.CeHeightLabel".Translate(ceHeight.ToString("0.##")));
        }
    }

    /// <summary>
    /// 世界地图对象额外文字：所属势力的自然阵营关系（NaturalGoodwill，即未受玩家行动影响的初始关系）。
    /// 永久敌对势力（FactionDef.permanentEnemy）直接显示为永久敌对；其余按数值分档显示：
    /// &gt;= 75 友善，&lt;= -75 敌对，其余中立；数值附在括号内。
    /// 仅当所属势力存在、非玩家阵营且可有好感度时显示；开关 enableExtraNaturalGoodwillText 独立控制。
    /// </summary>
    private static void GenWorldObjectExtra(StringBuilder sb, WorldObject wo)
    {
        if (!enableExtraNaturalGoodwillText)
        {
            return;
        }
        Faction? faction = wo.Faction;
        if (faction == null || faction == Faction.OfPlayer || !faction.HasGoodwill)
        {
            return;
        }
        if (faction.def.permanentEnemy)
        {
            AppendLineTo(sb, "BetterInspectPane.PermanentEnemyLabel".Translate());
            return;
        }
        int naturalGoodwill = faction.NaturalGoodwill;
        string labelKey = naturalGoodwill >= 75
            ? "BetterInspectPane.NaturalGoodwillFriendlyLabel"
            : (naturalGoodwill <= -75
                ? "BetterInspectPane.NaturalGoodwillHostileLabel"
                : "BetterInspectPane.NaturalGoodwillNeutralLabel");
        AppendLineTo(sb, labelKey.Translate(naturalGoodwill.ToString()));
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

    /// <summary>
    /// 按设置拼装最终信息文字：原版文字 + 本模组额外文字 + Info Card Plus 收藏 Stat 文字。
    /// pre：置于原版文字之前的文字（派系定居点的派系类别，GetFactionDefLabelText），
    /// 始终位于原版文字正上方（收藏文字置最前时在其之后）。
    /// 收藏文字位置（infoCardPlusPinnedPosition）：Before=置于最前（原版文字之前，默认），After=置于最后。
    /// Pawn 例外：Pawn 的所有额外信息文字（本模组额外文字 + 收藏统计）一律置于原版文字上方，
    /// 无视收藏文字置底设置（infoCardPlusPinnedPosition）；pre 对 Pawn 恒为空串，不影响 Pawn 分支。
    /// 各段非空才参与拼接，段间以单个换行分隔（沿用 TrimEndNewlines 口径）。
    /// </summary>
    internal static string AssembleInspectString(ISelectable? sel, string vanilla, string extra, string pinned, string pre = "")
    {
        // Pawn：额外信息文字（含收藏统计）全部置于原版文字上方，无视置底设置。
        if (sel is Pawn)
        {
            StringBuilder pawnSb = new StringBuilder();
            AppendMergedPart(pawnSb, extra);
            AppendMergedPart(pawnSb, pinned);
            AppendMergedPart(pawnSb, vanilla);
            return pawnSb.ToString();
        }

        bool positionBefore = infoCardPlusPinnedPosition == InfoCardPlusPinnedPosition.Before;
        StringBuilder sb = new StringBuilder();
        if (positionBefore)
        {
            AppendMergedPart(sb, pinned);
        }
        AppendMergedPart(sb, pre);
        AppendMergedPart(sb, vanilla);
        AppendMergedPart(sb, extra);
        if (!positionBefore)
        {
            AppendMergedPart(sb, pinned);
        }
        return sb.ToString();
    }

    /// <summary>按「非空才参与、段间单个换行」并入一段文字（先去除段尾换行）。</summary>
    private static void AppendMergedPart(StringBuilder sb, string part)
    {
        if (part.NullOrEmpty())
        {
            return;
        }
        part = part.TrimEndNewlines();
        if (part.NullOrEmpty())
        {
            return;
        }
        if (sb.Length > 0)
        {
            sb.Append('\n');
        }
        sb.Append(part);
    }

    // ===== 武器额外统计（普通对象）=====
    // DrawInspectString 同一帧只对单个对象（sel）绘制，故以 thing 引用为键缓存武器统计，
    // 仅在选择对象变化（thing 变化）时重算，避免逐帧重复统计计算。
    private static Thing? cachedWeaponThing;
    private static bool cachedIsRanged;   // 是否为远程武器（决定护甲穿透取远程/近战）
    private static float cachedRangedDamagePerShot;
    private static float cachedRangedWarmup;
    private static float cachedRangedCooldown;
    private static float cachedRangedDps;
    private static float cachedRangedRange;   // 远程武器射程（格）
    private static float cachedMeleeCooldown;
    private static float cachedMeleeDps;
    private static float cachedRangedArmorPen;
    private static float cachedMeleeArmorPen;

    // 近战平均护甲穿透 StatDef 不在 StatDefOf 中，按 defName 运行时解析。
    private static readonly StatDef? MeleeWeaponAverageArmorPenetrationStat =
        DefDatabase<StatDef>.GetNamedSilentFail("MeleeWeapon_AverageArmorPenetration");

    private static void EnsureWeaponStatsCached(Thing thing)
    {
        if (cachedWeaponThing == thing)
        {
            return;
        }
        cachedWeaponThing = thing;
        ResetWeaponStats();

        // 防御性处理：畸形/异常的 mod 定义不应让整个检查面板绘制崩溃，出错时按「非武器」处理。
        ThingDef def = thing.def;
        if (def == null)
        {
            return;
        }

        try
        {
            List<Tool> tools = def.tools;          // 非武器可能为 null
            List<VerbProperties> verbs = def.Verbs; // 非武器可能为 null
            bool hasTools = !tools.NullOrEmpty();

            // 既无工具也无攻击动词的不属于武器，保持全部 0、不显示任何武器信息。
            if (!hasTools && verbs.NullOrEmpty())
            {
                return;
            }

            // 远程统计：取首个远程投射类攻击动词。
            VerbProperties? rangedVerb = FindRangedVerb(verbs);
            cachedIsRanged = rangedVerb != null;
            if (rangedVerb != null)
            {
                float damagePerShot = SafeStat(rangedVerb.defaultProjectile.projectile.GetDamageAmount(thing, null));
                cachedRangedDamagePerShot = damagePerShot;
                cachedRangedWarmup = SafeStat(rangedVerb.warmupTime * thing.GetStatValue(StatDefOf.RangedWeapon_WarmupMultiplier));
                cachedRangedCooldown = SafeStat(thing.GetStatValue(StatDefOf.RangedWeapon_Cooldown));
                cachedRangedDps = GetRangedDps(damagePerShot, rangedVerb, cachedRangedWarmup,
                    cachedRangedCooldown);
                cachedRangedArmorPen = SafeStat(rangedVerb.defaultProjectile.projectile.GetArmorPenetration(thing, null));
                cachedRangedRange = SafeStat(rangedVerb.range);
            }

            // 近战统计：仅在确实有近战手段时读取，避免对纯远程武器得到 NaN
            // （游戏自带统计内部对空列表做 0/0 加权平均）。
            bool hasMelee = hasTools || HasMeleeVerb(verbs);
            cachedMeleeCooldown = SafeStat(hasMelee ? GetMeleeCooldown(thing, tools, verbs) : 0f);
            cachedMeleeDps = SafeStat(hasMelee ? thing.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS) : 0f);
            cachedMeleeArmorPen = hasMelee && MeleeWeaponAverageArmorPenetrationStat != null
                ? SafeStat(thing.GetStatValue(MeleeWeaponAverageArmorPenetrationStat))
                : 0f;
        }
        catch (Exception ex)
        {
            // 个别 mod 的定义可能在被读取/计算时抛异常；按对象记录一次即可，随后按非武器处理。
            Log.WarningOnce("ASQBetterInspectPane: 计算武器附加信息失败，已忽略。def=" + def.defName
                + " 异常=" + ex.Message, 39812744);
            ResetWeaponStats();
        }
    }

    /// <summary>把所有武器附加统计重置为「非武器」状态。</summary>
    private static void ResetWeaponStats()
    {
        cachedIsRanged = false;
        cachedRangedDamagePerShot = 0f;
        cachedRangedWarmup = 0f;
        cachedRangedCooldown = 0f;
        cachedRangedDps = 0f;
        cachedRangedRange = 0f;
        cachedMeleeCooldown = 0f;
        cachedMeleeDps = 0f;
        cachedRangedArmorPen = 0f;
        cachedMeleeArmorPen = 0f;
    }

    /// <summary>把 NaN / ±无穷 / 负数等异常统计值规整为 0（正向有效值原样返回）。</summary>
    private static float SafeStat(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f ? value : 0f;

    /// <summary>取动词列表中的首个远程投射类攻击动词，无则返回 null。</summary>
    private static VerbProperties? FindRangedVerb(List<VerbProperties>? verbs)
    {
        if (verbs == null)
        {
            return null;
        }
        for (int i = 0; i < verbs.Count; i++)
        {
            VerbProperties v = verbs[i];
            // 需要 projectile.damageDef：GetArmorPenetration 内部会直接解引用 damageDef.armorCategory，
            // 缺少 damageDef 的（异常）投射物会被 GetDamageAmount 报错并返回错误值，故整体跳过。
            if (v != null && v.Ranged && v.defaultProjectile != null && v.defaultProjectile.projectile != null
                && v.defaultProjectile.projectile.damageDef != null)
            {
                return v;
            }
        }
        return null;
    }

    /// <summary>
    /// 计算远程武器的 DPS：以「单发伤害 × 连射数」除以一次完整射击循环时长（秒）：
    /// 瞄准预热（warmupTime × RangedWeapon_WarmupMultiplier）+ 连射间隔 + 冷却（RangedWeapon_Cooldown）。
    /// 预热与冷却已由调用方规整为非负有限值并传入（含品质/材料加成）。
    /// 无法取得投射物伤害/射击循环时长时返回 0（不显示）。
    /// </summary>
    private static float GetRangedDps(float damagePerShot, VerbProperties verb,
        float warmup, float cooldown)
    {
        if (damagePerShot <= 0f || verb == null)
        {
            return 0f;
        }

        // 一次射击循环时长（秒）：瞄准 + 连射（burstShotCount 发之间 ticksBetweenBurstShots 间隔）+ 冷却。
        float burstTime = Mathf.Max(0, verb.burstShotCount - 1) * verb.ticksBetweenBurstShots / 60f;
        float cycleSeconds = warmup + burstTime + cooldown;
        if (cycleSeconds <= 0f)
        {
            return 0f;
        }

        return damagePerShot * verb.burstShotCount / cycleSeconds;
    }

    /// <summary>
    /// 计算近战武器的平均冷却时间（秒）：以各工具的 chanceFactor 为权重对
    /// tool.AdjustedCooldown(thing) 加权平均（与游戏近战 DPS 统计的选择权重口径相近）。
    /// 无工具但存在近战动词时，退回首个近战动词的冷却（默认冷却 × 近战冷却乘数）。
    /// tools / verbs 为调用方已取得的定义列表（可能为 null）。
    /// </summary>
    private static float GetMeleeCooldown(Thing thing, List<Tool>? tools, List<VerbProperties>? verbs)
    {
        if (tools != null)
        {
            float totalWeight = 0f;
            float weightedCooldown = 0f;
            for (int i = 0; i < tools.Count; i++)
            {
                Tool tool = tools[i];
                if (tool == null)
                {
                    continue;
                }
                float weight = tool.chanceFactor;
                if (weight <= 0f || float.IsNaN(weight) || float.IsInfinity(weight))
                {
                    continue;
                }
                totalWeight += weight;
                weightedCooldown += tool.AdjustedCooldown(thing) * weight;
            }
            if (totalWeight > 0f)
            {
                return weightedCooldown / totalWeight;
            }
        }
        if (verbs != null)
        {
            for (int i = 0; i < verbs.Count; i++)
            {
                VerbProperties v = verbs[i];
                if (v != null && v.IsMeleeAttack)
                {
                    return v.defaultCooldownTime * thing.GetStatValue(StatDefOf.MeleeWeapon_CooldownMultiplier);
                }
            }
        }
        return 0f;
    }

    /// <summary>动词列表中是否存在近战攻击动词。</summary>
    private static bool HasMeleeVerb(List<VerbProperties>? verbs)
    {
        if (verbs == null)
        {
            return false;
        }
        for (int i = 0; i < verbs.Count; i++)
        {
            VerbProperties v = verbs[i];
            if (v != null && v.IsMeleeAttack)
            {
                return true;
            }
        }
        return false;
    }

    #endregion

    #region 额外信息文字（世界地图空地砖）

    /// <summary>
    /// 世界地图空地砖：靠近污染（附近污染分数 &gt; 0 显示）、靠近其它势力（沿用原版
    /// 定居点邻近关系惩罚算法，考虑势力类型与距离）。
    /// 仅当世界地图处于打开状态且选中一个空地块时计算
    /// （可排除进入殖民地地图后 SelectedTile 仍残留有效值的情况）；
    /// 文本由 GetExtraText 的帧缓存统一缓存，此处不再单独缓存。
    /// </summary>
    private static StringBuilder GenWorldTileExtra(StringBuilder sb, PlanetTile tile)
    {
        if (!enabled || !enableExtraMapTileText || !tile.Valid)
        {
            return sb;
        }
        // 靠近污染：附近 4 格内的污染加权分数。
        float pollutionScore = WorldPollutionUtility.CalculateNearbyPollutionScore(tile);
        if (pollutionScore > 0f)
        {
            AppendLineTo(sb, "BetterInspectPane.NearPollutionLabel".Translate(pollutionScore.ToString("0.##")));
        }
        // 靠近其它势力：4 格内存在非玩家阵营的势力（以其在世界地图上的对象如定居点为锚点）。
        string factions = GetNearbyFactionsText(tile);
        if (!factions.NullOrEmpty())
        {
            AppendLineTo(sb, "BetterInspectPane.NearFactionLabel".Translate(factions));
        }
        return sb;
    }

    /// <summary>
    /// 收集所给砖附近的其它势力名称，判断口径与原版「定居点邻近关系惩罚」算法
    /// （SettlementProximityGoodwillUtility.AppendProximityGoodwillOffsets）完全一致：
    /// 以各阵营的定居点为锚点，按通行距离进入惩罚距离（MaxDist）即视为「靠近」；
    /// 该算法自动考虑了势力类型（永久敌对排除、阵营层距离系数 rangeDistanceFactor）
    /// 与距离（Goodwill_PerQuadrumFromSettlementProximity 惩罚曲线，半径为最大距离）。
    /// 显示时按阵营去重（同一阵营可能有多个定居点）。
    /// </summary>
    private static string GetNearbyFactionsText(PlanetTile tile)
    {
        List<Pair<Settlement, int>> offsets = new List<Pair<Settlement, int>>();
        SettlementProximityGoodwillUtility.AppendProximityGoodwillOffsets(
            tile, offsets, ignoreIfAlreadyMinGoodwill: true, ignorePermanentlyHostile: true);

        StringBuilder names = new StringBuilder();
        List<Faction> seen = new List<Faction>();
        for (int i = 0; i < offsets.Count; i++)
        {
            Faction faction = offsets[i].First.Faction;
            if (seen.Contains(faction))
            {
                continue;
            }
            seen.Add(faction);
            if (names.Length > 0)
            {
                names.Append('、');
            }
            names.Append(faction.Name);
        }
        return names.ToString();
    }

    #endregion
}

/// <summary>
/// 额外文字字号：检查面板下方的原版信息文字。
/// 其实际绘制走 InspectPaneFiller.DrawInspectString，内部会把字号硬编码为 Small，
/// 因此仅当本模组自定义绘制面板（ShouldDrawCustomPane）时接管绘制，按设置字号重画；
/// 在此基础上，把需要追加的额外信息文字（InspectPanePatch.GetExtraText）、
/// 置于原版文字之前的文字（InspectPanePatch.GetFactionDefLabelText）与
/// Info Card Plus 收藏 Stat 文字（InfoCardPlusReflection.GetPinnedText）由
/// InspectPanePatch.AssembleInspectString 统一拼装后再一起绘制。
/// 因 InspectPanePatch 已自带一份 [HarmonyPatch(DoPaneContentsFor)] 与其 Prefix(ISelectable, Rect)，
/// 本类必须独立保留为一个 Harmony 补丁类挂在 DrawInspectString 上（无法同时挂两个类型级补丁），
/// 其计算与绘制逻辑均复用 InspectPanePatch 的共用方法。
/// </summary>
[HarmonyPatch(typeof(InspectPaneFiller), nameof(InspectPaneFiller.DrawInspectString))]
internal static class DrawInspectStringPatch
{
    // ===== 补丁入口 =====

    static bool Prefix(string str, Rect rect)
    {
        // 检查面板当前绘制的是 selectedObject（DoPaneContents 的 sel 来源）。
        // 世界判定必须用 WorldRendererUtility.WorldSelected：从殖民地地图打开世界视图时
        // Find.CurrentMap 并不为 null（地图仍处于加载状态），按 CurrentMap 判定会把地图选择器里
        // 残留的选择（如 Pawn）误当成本面板的选中对象，导致 Pawn 信息混入世界面板的文字里；
        // 另外主菜单的世界视图下 UIRoot 不是 UIRoot_Play，Find.Selector 会抛类型转换异常，
        // 世界分支先行可一并避开。
        bool world = WorldRendererUtility.WorldSelected;
        ISelectable? sel = world
            ? Find.WorldSelector.FirstSelectedObject as ISelectable
            : Find.Selector.FirstSelectedObject as ISelectable;

        // 计算需追加的额外信息文字与 Info Card Plus 收藏 Stat 文字。
        // 注意：本模组接管 InspectString 时（overrideInspectString）额外文字已在拼装阶段
        // 并入（见 InspectPanePatch.RefreshInspectStringCache），且绘制改走本模组自己的
        // DrawInspectStringFor（共用 DrawInspectStringText 实现）、不再经过本 Prefix，因此不会重复追加。
        // 原版文字 + 额外文字 + 收藏 Stat 文字 + 置前文字的最终顺序由 AssembleInspectString 统一拼装。
        string extra = InspectPanePatch.GetExtraText(sel);
        string pinned = InfoCardPlusReflection.GetPinnedText(sel, out _);
        string pre = InspectPanePatch.GetFactionDefLabelText(sel);
        str = InspectPanePatch.AssembleInspectString(sel, str, extra, pinned, pre);

        bool custom = InspectPanePatch.ShouldDrawCustomPane(sel, out _, out _, out _);
        if (!custom && extra.NullOrEmpty() && pinned.NullOrEmpty())
        {
            return true; // 未接管绘制且无需追加额外文字：走原版绘制。
        }

        InspectPanePatch.DrawInspectStringText(str, rect);
        return false;
    }
}