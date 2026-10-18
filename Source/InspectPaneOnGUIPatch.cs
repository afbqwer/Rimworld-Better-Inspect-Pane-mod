using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;
using RimWorld;
using RimWorld.Planet;
using HarmonyLib;

namespace ASQBetterInspectPane;

/// <summary>
/// 重写检查面板的整体绘制 InspectPaneUtility.InspectPaneOnGUI（Prefix 返回 false 接管原方法）：
/// 原版在这里用 inRect.ContractedBy(12f) 排出四周 12 像素边距、并固定以 GameFont.Medium
/// 绘制标题文字。本补丁把这两个硬编码值改为可配置：
///   - 四周边距（paneMargin）：替换 ContractedBy 的 12f，其余保留原版的上下微调（yMin-4 / yMax+6）。
///   - 标题文本字号（titleFontSize）：用 FontSizeScope 以像素字号代替原版固定 Medium 档。
/// 另含 Surveillance Shift 兼容：接管绘制后额外图标与 SS 图标的排布衔接（见按钮组内 ExtraStripGapWidth 调用）。
/// 另含自适应面板宽度：按真实标题字号测量完整标题，经 requiredPaneWidth 供宽度补丁取 max，
/// 使「名称 + 右侧按钮」放不下时面板自动加宽（见 UpdateRequiredPaneWidth）；
/// 标题截断也改为按真实字号现算（原版按 Medium 度量且按文本冻结缓存，字号调大后测得偏窄）。
/// 未启用覆盖（overrideInspectPane 为 false）时返回 true，走原版绘制，不影响其它行为。
/// </summary>
[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.InspectPaneOnGUI))]
[HarmonyPriority(Priority.LowerThanNormal)]
public static class InspectPaneOnGUIPatch
{
    static bool Prefix(Rect inRect, IInspectPane pane)
    {
        if (!MyModTemplateSettings.overrideInspectPane)
        {
            return true;
        }

        pane.RecentHeight = Mathf.Max(InspectPaneUtility.PaneHeight, MyModTemplateSettings.EffectivePaneHeight);
        if (!pane.AnythingSelected)
        {
            // 无选中时标题不存在，复位自适应宽度所需值，避免残留上一个选中对象的宽度让空面板保持过宽。
            MyModTemplateSettings.requiredPaneWidth = 0f;
            return false;
        }
        // 单选 Pawn 检测：仅当面板为地图检查面板（MainTabWindow_Inspect）且恰好选中一个 Pawn 时，
        // Pawn 面板专用开关（显示全名 / 显示额外按钮 / Pawn 标题字号）才生效。
        // 世界地图检查面板打开时地图选择器里可能仍残留进入世界前的选择（如 Pawn），
        // 不能把地图选择器的单选对象当作当前面板的选中对象，否则 Pawn 的标题 / 按钮会绘制到世界面板上。
        Selector? mapSelector = pane is MainTabWindow_Inspect ? (Find.UIRoot as UIRoot_Play)?.mapUI.selector : null;
        Pawn? singlePawn = mapSelector == null ? null
            : (mapSelector.NumSelected == 1 ? mapSelector.SingleSelectedThing as Pawn : null);
        int effectiveTitleFontSize = singlePawn != null
            ? MyModTemplateSettings.pawnTitleFontSize
            : MyModTemplateSettings.titleFontSize;

        try
        {
            Rect rect = inRect.ContractedBy(MyModTemplateSettings.paneMargin);
            Widgets.BeginGroup(rect);
            try
            {
                // 标题区行高：以标题字号对应的单行行高为基准（字号越大、文本越大，整行越高），
                // 在此基础上额外预留 24f，使内容区（起始于 titleHeight - 24f）始终位于标题文字下方。
                float titleLineHeight = MyModTemplateSettings.LineHeightForSize(effectiveTitleFontSize);
                float titleHeight = Mathf.Max(50f, titleLineHeight + 24f);

                // 右上角按钮组：垂直居中于标题文字（原版固定画在顶部 y=0，标题变高后会显得偏上）。
                // 把按钮组放进一个向下平移 titleLineHeight 居中位置的 Group，使其随标题文字一起居中，
                // 而不是贴在顶部；文本较小时按钮高度不小于文本，居中偏移会回落到 0（与原先顶部效果一致）。
                float buttonY = Mathf.Max(0f, (titleLineHeight - 24f) * 0.5f);
                Widgets.BeginGroup(new Rect(0f, buttonY, rect.width, Mathf.Max(1f, rect.height - buttonY)));
                float lineEndWidth = 0f;
                try
                {
                    if (pane.ShouldShowSelectNextInCellButton)
                    {
                        Rect nextBtn = new Rect(rect.width - 24f, 0f, 24f, 24f);
                        MouseoverSounds.DoRegion(nextBtn);
                        if (Widgets.ButtonImage(nextBtn, TexButton.SelectOverlappingNext))
                        {
                            pane.SelectNextInCell();
                        }
                        lineEndWidth += 24f;
                        if (SteamDeck.IsSteamDeckInNonKeyboardMode)
                        {
                            TooltipHandler.TipRegionByKey(nextBtn, "SelectNextInSquareTipController");
                        }
                        else
                        {
                            TooltipHandler.TipRegionByKey(nextBtn, "SelectNextInSquareTip", KeyBindingDefOf.SelectNextInCell.MainKeyLabel);
                        }
                    }

                    pane.DoInspectPaneButtons(rect, ref lineEndWidth);

                    // Surveillance Shift 兼容：SS 在本调用内的 Postfix 里会再画图标，且其横向占位比实际占用少 24px
                    // （见 SurveillanceShiftReflection），这里先补齐再接排本模组的额外图标，避免两组图标重合。
                    // SS 的补丁只挂在地图检查面板上，世界面板无需补偿。
                    if (pane is MainTabWindow_Inspect)
                    {
                        lineEndWidth += SurveillanceShiftReflection.ExtraStripGapWidth(singlePawn);
                    }

                    // Pawn 单选检查面板：在原版右侧按钮左侧绘制额外图标按钮（异种类型 / 阵营 / 文化）。
                    // Bounded Rationality 同步：与原版信息卡按钮同一口径（Meta + Control），
                    // BR 隐藏信息卡按钮时一并隐藏额外按钮（见 BoundedRationalityReflection）。
                    if (singlePawn != null && MyModTemplateSettings.pawnShowExtraButtons
                        && BoundedRationalityReflection.IsInfoCardInfoKnown(singlePawn))
                    {
                        DrawPawnExtraButtons(rect, singlePawn, ref lineEndWidth);
                    }
                }
                finally
                {
                    Widgets.EndGroup();
                }

                // 标题文字：字号改为可配置像素值；原版 GameFont.Medium 对应高度约 50，按字号计算标题区高度。
                // 记录绘制前的字体与锚点，绘制后还原，避免污染后续 DoPaneContents 的绘制上下文。
                Rect titleRect = new Rect(0f, 0f, rect.width - lineEndWidth, titleHeight);
                // 标题每帧现取（与原版同频，名称显示不滞后于改名 / 堆叠数 / 多选计数变化）：
                //   - 全名路径：Name.ToStringFull；
                //   - 自适应宽度开启：取「完整标题」（GetFullTitle，借用原版拼装口径，但以临时缓存 +
                //     超大宽度绕开其按当前面板宽度的截断——原版截断结果按文本冻结缓存，面板加宽后不放开）；
                //   - 自适应宽度关闭：沿用原版 GetLabel 口径，零额外开销。
                // 截断统一由 TruncateToWidth 按真实字号现算；宽度测量按标题文本缓存（名称不变则命中，
                // 名称一变同帧重测），因此显示与宽度都不需要时间维度的缓存。
                string label;
                if (singlePawn != null && MyModTemplateSettings.pawnShowFullName && singlePawn.Name != null)
                {
                    label = singlePawn.Name.ToStringFull;
                }
                else if (MyModTemplateSettings.adaptivePaneWidth)
                {
                    label = GetFullTitle(pane, titleHeight) ?? pane.GetLabel(titleRect);
                }
                else
                {
                    label = pane.GetLabel(titleRect);
                }

                // 自适应面板宽度更新放在标题绘制之前：绘制侧 TruncateToWidth 直接复用本帧测量缓存，
                // 稳态（标题与字号不变）下标题绘制零 CalcSize。所需总宽 = 完整标题宽 + 按钮条宽 + 边距，
                // 供 PaneWidthFor / PaneSizeFor 取 max（见 UpdateRequiredPaneWidth）。
                // 多选网格场景的标题为「xx xN」，沿用多选独立最小宽度体系，不在此更新。
                // 多选网格场景判定按面板类型短路（同 PaneSizeForPatch 口径）：地图场景检查走 Find.Selector，
                // 主菜单世界视图（UIRoot_Entry）下该 getter 会把 UIRoot 强转 UIRoot_Play 抛异常，
                // 因此地图面板只判地图网格、世界面板只判世界网格（走 WorldSelector，主菜单下安全）。
                bool mapGrid = pane is MainTabWindow_Inspect && InspectPanePatch.IsMultiSelectGridActive();
                bool worldGrid = pane is WorldInspectPane && InspectPanePatch.IsWorldMultiSelectGridActive();
                if (MyModTemplateSettings.adaptivePaneWidth && !mapGrid && !worldGrid)
                {
                    UpdateRequiredPaneWidth(label, effectiveTitleFontSize, lineEndWidth);
                }
                else if (MyModTemplateSettings.requiredPaneWidth > 0f)
                {
                    MyModTemplateSettings.requiredPaneWidth = 0f; // 关闭或进入多选网格时复位，宽度回落
                }

                float titleAvailableWidth = titleRect.width;
                titleRect.width += 300f;
                GameFont prevFont = Text.Font;
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.UpperLeft;
                using (new FontSizeScope(effectiveTitleFontSize))
                {
                    // 截断改在真实标题字号下现算：原版 AdjustedLabelFor 固定按 Medium 度量且结果按文本
                    // 冻结缓存，标题字号调大后测得偏窄、名称会画进右侧按钮区（本功能要解决的原始问题）。
                    Widgets.Label(titleRect, TruncateToWidth(label, effectiveTitleFontSize, titleAvailableWidth));
                }
                Text.Font = prevFont;
                Text.Anchor = prevAnchor;

                if (pane.ShouldShowPaneContents)
                {
                    // 内容区随标题高度下移（原版固定 yMin += 26f）。
                    Rect contentRect = rect.AtZero();
                    contentRect.yMin += titleHeight - 24f;
                    pane.DoPaneContents(contentRect);
                }
            }
            finally
            {
                Widgets.EndGroup();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Exception doing inspect pane: " + ex.ToString());
        }
        return false;
    }

    // ---------- 自适应面板宽度 ----------

    /// <summary>测量用矩形宽度：GetLabel 按该宽度截断标题，取足够大的值以获得未截断的完整标题。</summary>
    private const float AdaptiveMeasureWidth = 100000f;

    /// <summary>自适应宽度余量：吸收字体测量与实际绘制的微小差异（四周 2×paneMargin 已在公式中另计）。</summary>
    private const float AdaptiveWidthPadding = 12f;

    /// <summary>自适应宽度上限：与「面板最小宽度」滑杆上限一致，超长名称超出部分仍按样截断。</summary>
    private const float MaxAdaptivePaneWidth = 800f;

    // 原版 AdjustedLabelFor 的截断缓存（私有静态）。仅在测量完整标题时临时换成空字典（见 GetFullTitle），
    // 解析失败则退回原版 GetLabel 口径（自适应宽度仍生效，只是测量对象可能是被截断过的标题）。
    private static readonly FieldInfo? truncatedLabelsCacheField =
        AccessTools.Field(typeof(InspectPaneUtility), "truncatedLabelsCached");
    private static readonly Dictionary<string, string> fullTitleMeasureCache = new Dictionary<string, string>();

    // 测量缓存：同一标题文本 + 字号只测一次。标题文本本身每帧现取（显示不滞后），
    // 文本不变（同一选中对象的常态）时命中缓存零 CalcSize，名称一变则键变化、同帧重测。
    private static string? cachedMeasureTitle;
    private static int cachedMeasureFontSize = -1;
    private static float cachedMeasureTitleWidth;

    /// <summary>
    /// 取未截断的完整标题：把原版截断缓存临时换成本类空字典后，以超大宽度调用 GetLabel ——
    /// Truncate 走「宽度足够 → 原样返回」分支，拼装口径与原版完全一致且不污染真实缓存，调用完立即还原。
    /// 必须换缓存的原因：原版 Truncate 先查缓存再按宽度截断、缓存按完整文本冻结，
    /// 直接走真实缓存会拿到历史窄宽度下的截断结果（面板加宽后省略号也不放开），且超大宽度调用
    /// 会把「完整标题」污染进真实缓存，让原版路径在该宽度不足时失去截断。
    /// </summary>
    private static string? GetFullTitle(IInspectPane pane, float titleHeight)
    {
        if (truncatedLabelsCacheField == null)
        {
            return null;
        }
        Dictionary<string, string>? realCache = truncatedLabelsCacheField.GetValue(null) as Dictionary<string, string>;
        if (realCache == null)
        {
            return null;
        }
        fullTitleMeasureCache.Clear();
        truncatedLabelsCacheField.SetValue(null, fullTitleMeasureCache);
        try
        {
            return pane.GetLabel(new Rect(0f, 0f, AdaptiveMeasureWidth, titleHeight));
        }
        finally
        {
            truncatedLabelsCacheField.SetValue(null, realCache);
        }
    }

    // 截断结果缓存（单条目）：截断是确定性计算，按「标题文本 + 字号 + 可用宽度」命中直接复用。
    // 仅在标题超出可用宽度时才有意义（放得下的路径本身就是一次 CalcSize），但命中后连那次也省掉。
    private static string? cachedTruncateSource;
    private static int cachedTruncateFontSize = -1;
    private static float cachedTruncateWidth = -1f;
    private static string cachedTruncateResult = "";

    /// <summary>
    /// 按当前（真实标题字号）字体把标题截断到指定宽度，超出部分以「…」结尾。
    /// 算法与 Verse 的 string.Truncate 一致，但不按文本冻结缓存、也不固定 Medium 档度量。
    /// 性能：宽度足够时仅一次 CalcSize，且该宽度通常已被 UpdateRequiredPaneWidth 测过（同帧同参数），
    /// 命中测量缓存后为纯比较；截断分支（标题超出可用宽度，如触到 800 上限）按「文本+字号+宽度」
    /// 缓存结果，稳态下同样零 CalcSize。
    /// </summary>
    private static string TruncateToWidth(string text, int fontSize, float width)
    {
        if (string.Equals(cachedTruncateSource, text) && cachedTruncateFontSize == fontSize
            && Mathf.Approximately(cachedTruncateWidth, width))
        {
            return cachedTruncateResult;
        }
        string result = TruncateToWidthUncached(text, fontSize, width);
        cachedTruncateSource = text;
        cachedTruncateFontSize = fontSize;
        cachedTruncateWidth = width;
        cachedTruncateResult = result;
        return result;
    }

    private static string TruncateToWidthUncached(string text, int fontSize, float width)
    {
        // 命中测量缓存时直接复用宽度（UpdateRequiredPaneWidth 已按同参数测过），省一次 CalcSize。
        float textWidth;
        if (string.Equals(cachedMeasureTitle, text) && cachedMeasureFontSize == fontSize)
        {
            textWidth = cachedMeasureTitleWidth;
        }
        else
        {
            textWidth = Text.CalcSize(text).x;
        }
        if (textWidth <= width)
        {
            return text;
        }
        string value = text;
        while (value.Length > 0 && Text.CalcSize(value + "...").x > width)
        {
            value = value.Substring(0, value.Length - 1);
        }
        return value + "...";
    }

    /// <summary>
    /// 更新自适应宽度所需的面板总宽（绘制路径每帧调用，测量结果按「标题文本 + 字号」缓存）：
    /// 所需总宽 = 完整标题宽度（真实标题字号）+ 右侧按钮条宽（lineEndWidth）+ 2×边距 + 余量，
    /// 上限截断到 MaxAdaptivePaneWidth。所需宽度只依赖标题与按钮条、与面板当前宽度无反馈关系
    /// （测量用完整标题而非被宽度截断后的文本），因此不会来回振荡；换选后一帧内到位。
    /// </summary>
    private static void UpdateRequiredPaneWidth(string title, int fontSize, float lineEndWidth)
    {
        if (string.IsNullOrEmpty(title))
        {
            return;
        }
        if (!string.Equals(cachedMeasureTitle, title) || cachedMeasureFontSize != fontSize)
        {
            float width;
            // 关闭自动换行测量单行宽度（与标题绘制的单行口径一致）。
            using (new FontSizeScope(fontSize))
            using (new WordWrapScope(false))
            {
                width = Text.CalcSize(title).x;
            }
            cachedMeasureTitle = title;
            cachedMeasureFontSize = fontSize;
            cachedMeasureTitleWidth = width;
        }
        float required = cachedMeasureTitleWidth + lineEndWidth
            + 2f * MyModTemplateSettings.paneMargin + AdaptiveWidthPadding;
        // 像素量化（向上取整，往「标题放得下」的方向）：窗口宽度同样被 Verse.Window 每帧取整，
        // 量化后所需宽度与实际窗口宽度严格一致（渲染为整数宽更锐利），也不给反馈回路留浮数偏差。
        MyModTemplateSettings.requiredPaneWidth = Mathf.Ceil(Mathf.Min(MaxAdaptivePaneWidth, required));
    }

    /// <summary>
    /// Pawn 单选检查面板的额外图标按钮（需在按钮组内调用）：
    /// 在原版右侧按钮（信息卡 / 阵营响应 / 改名 / 罪恶标记等）左侧，从右到左依次绘制
    /// 异种类型 / 阵营 / 文化 三个 24px 图标按钮，并把宽度计入 lineEndWidth 使标题自动让位：
    ///   - 异种类型：取 Pawn 的异种类型图标（需 Biotech，无图标时不显示），点击打开对应 XenotypeDef 信息卡；
    ///   - 阵营：以阵营颜色着色的阵营图标，点击打开阵营信息卡（沿用原版 DrawFactionIconWithTooltip）；
    ///   - 文化：所属 Ideo 的图标（Pawn 无 Ideo 时不显示），点击打开 Ideos 页（沿用原版 DoIdeoIcon）。
    /// </summary>
    private static void DrawPawnExtraButtons(Rect rect, Pawn pawn, ref float lineEndWidth)
    {
        const float size = 24f;
        float x = rect.width - lineEndWidth - size;

        // 异种类型（最右侧）。
        if (pawn.genes != null)
        {
            Texture2D xenotypeIcon = pawn.genes.XenotypeIcon;
            if (xenotypeIcon != null)
            {
                XenotypeDef xenotype = pawn.genes.Xenotype;
                Rect r = new Rect(x, 0f, size, size);
                if (Widgets.ButtonImage(r, xenotypeIcon))
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(xenotype));
                }
                TooltipHandler.TipRegion(r, xenotype.LabelCap);
                x -= size;
                lineEndWidth += size;
            }
        }

        // 阵营（有颜色图标）：以阵营颜色着色绘制，点击打开阵营信息卡。
        Faction faction = pawn.Faction;
        if (faction != null && faction.def.FactionIcon != BaseContent.BadTex)
        {
            Rect r = new Rect(x, 0f, size, size);
            FactionUIUtility.DrawFactionIconWithTooltip(r, faction);
            x -= size;
            lineEndWidth += size;
        }

        // 文化（所属 Ideo 的图标，无 Ideo 时不显示）。
        Ideo ideo = pawn.Ideo;
        if (ideo != null)
        {
            Rect r = new Rect(x, 0f, size, size);
            IdeoUIUtility.DoIdeoIcon(r, ideo, doTooltip: true, extraAction: () => IdeoUIUtility.OpenIdeoInfo(ideo));
            x -= size;
            lineEndWidth += size;
        }
    }
}