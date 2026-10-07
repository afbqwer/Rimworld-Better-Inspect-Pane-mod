using System;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.Steam;
using RimWorld;
using HarmonyLib;

namespace ASQBetterInspectPane;

/// <summary>
/// 重写检查面板的整体绘制 InspectPaneUtility.InspectPaneOnGUI（Prefix 返回 false 接管原方法）：
/// 原版在这里用 inRect.ContractedBy(12f) 排出四周 12 像素边距、并固定以 GameFont.Medium
/// 绘制标题文字。本补丁把这两个硬编码值改为可配置：
///   - 四周边距（paneMargin）：替换 ContractedBy 的 12f，其余保留原版的上下微调（yMin-4 / yMax+6）。
///   - 标题文本字号（titleFontSize）：用 FontSizeScope 以像素字号代替原版固定 Medium 档。
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
                // Pawn 全名：开启「显示 Pawn 全名」且单选 Pawn 时，覆盖默认标题改用 Name.ToStringFull。
                string label;
                if (singlePawn != null && MyModTemplateSettings.pawnShowFullName && singlePawn.Name != null)
                {
                    label = singlePawn.Name.ToStringFull;
                }
                else
                {
                    label = pane.GetLabel(titleRect);
                }
                titleRect.width += 300f;
                GameFont prevFont = Text.Font;
                TextAnchor prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.UpperLeft;
                using (new FontSizeScope(effectiveTitleFontSize))
                {
                    Widgets.Label(titleRect, label);
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
        if (faction != null)
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