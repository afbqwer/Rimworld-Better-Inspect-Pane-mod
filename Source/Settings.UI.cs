using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace ASQBetterInspectPane;
// 设置面板 UI
public partial class MyModTemplateSettings : ModSettings
{
    public void DoSettingsWindowContents(Rect inRect)
    {
        // 顶部标签页栏：用 RimWorld 标准的 TabDrawer 绘制（标签页位于内容区上方）。
        // 注意 inRect 的 x/y 可能非零（如原版 Mod 设置窗口传入的矩形局部 y=40），
        // 必须以 inRect 原点为基准定位，否则标签页会画到内容区顶部、盖住标题。
        float tabHeight = TabDrawer.TabHeight;
        Rect tabRect = new Rect(inRect.x, inRect.y + tabHeight, inRect.width, tabHeight);
        DoPageTabs(tabRect);

        // 标签栏下方为当前页内容区。
        Rect contentRect = new Rect(inRect.x, inRect.y + tabHeight + 6f, inRect.width, inRect.height - tabHeight - 6f);
        switch (currentPage)
        {
            case SettingsPage.General:
                DoGeneralPage(contentRect);
                break;
            case SettingsPage.Bars:
                DoBarsPage(contentRect);
                break;
            case SettingsPage.MultiSelect:
                DoMultiSelectPage(contentRect);
                break;
            case SettingsPage.Colors:
                DoColorsPage(contentRect);
                break;
            case SettingsPage.Effects:
                DoEffectsPage(contentRect);
                break;
        }
    }

    /// <summary>用 TabDrawer.DrawTabs 绘制顶部标签页；点击其它页时经 TabRecord.clickedAction 切换 currentPage。</summary>
    private static void DoPageTabs(Rect baseRect)
    {
        string[] labels =
        {
            "BetterInspectPane.PageGeneral".Translate(),
            "BetterInspectPane.PageBars".Translate(),
            "BetterInspectPane.PageMultiSelect".Translate(),
            "BetterInspectPane.PageColors".Translate(),
            "BetterInspectPane.PageEffects".Translate(),
        };
        List<TabRecord> tabs = new List<TabRecord>(labels.Length);
        for (int i = 0; i < labels.Length; i++)
        {
            SettingsPage page = (SettingsPage)i;
            tabs.Add(new TabRecord(labels[i], () => currentPage = page, () => currentPage == page));
        }
        TabDrawer.DrawTabs(baseRect, tabs);
    }

    /// <summary>开始一页的滚动视图（统一顶部留白、滚动条预留宽度），并输出该页排版的 x / y / width 起点。
    /// 内容高度取该页上一帧绘制时实测缓存的高度（见 EndPageScroll），使滚动范围随内容自适应。</summary>
    private static Rect BeginPageScroll(Rect inRect, SettingsPage page,
        out float x, out float y, out float width)
    {
        x = 0f;
        y = PageTopPadding;
        Rect viewRect = new Rect(0f, 0f, inRect.width - ScrollbarGutter, pageContentHeights[(int)page]);
        Widgets.BeginScrollView(inRect, ref pageScrollPositions[(int)page], viewRect);
        width = viewRect.width;
        return viewRect;
    }

    /// <summary>结束一页的滚动视图，并把本页实测内容高度（当前排版位置 y + 底部留白）记录进缓存，
    /// 供下一帧 BeginPageScroll 用作该页滚动范围。</summary>
    private static void EndPageScroll(SettingsPage page, float contentBottomY)
    {
        pageContentHeights[(int)page] = contentBottomY + RowGap;
        Widgets.EndScrollView();
    }

    // ----- 第 1 页：常规 -----
    private void DoGeneralPage(Rect inRect)
    {
        BeginPageScroll(inRect, SettingsPage.General, out float x, out float y, out float width);

        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableRedraw".Translate(), ref enabled);
        y += RowGap;

        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.OverrideInspectPane".Translate(), ref overrideInspectPane);
        y += RowGap;

        if (overrideInspectPane)
        {
            paneMargin = HorizontalSlider(new Rect(x, y, width, SliderHeight), paneMargin, 0f, 30f,
                label: "BetterInspectPane.PaneMargin".Translate(Mathf.RoundToInt(paneMargin)), roundTo: 1f);
            y += SliderGap;

            titleFontSize = Mathf.RoundToInt(HorizontalSlider(new Rect(x, y, width, SliderHeight), titleFontSize, 8f, 32f,
                label: "BetterInspectPane.TitleFontSize".Translate(titleFontSize), roundTo: 1f));
            y += SliderGap;

        }
        y += ItemGap;

        Rect pawnToggleRect = new Rect(x, y, width, RowHeight);
        Widgets.CheckboxLabeled(pawnToggleRect, "BetterInspectPane.EnablePawns".Translate(), ref enablePawns);
        TooltipHandler.TipRegion(pawnToggleRect, "BetterInspectPane.EnablePawns_Tip".Translate());
        y += RowGap;
        if (enablePawns && overrideInspectPane)
        {
            float indent = 20f;
            pawnTitleFontSize = Mathf.RoundToInt(HorizontalSlider(new Rect(x + indent, y, width - indent, SliderHeight), pawnTitleFontSize, 8f, 32f,
                label: "BetterInspectPane.PawnTitleFontSize".Translate(pawnTitleFontSize), roundTo: 1f));
            y += SliderGap;

            // 显示 Pawn 全名 / 显示 Pawn 额外按钮（异种类型、阵营、文化）/ Pawn 标题字号。
            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight),
                "BetterInspectPane.PawnShowFullName".Translate(), ref pawnShowFullName);
            y += RowGap;

            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight),
                "BetterInspectPane.PawnShowExtraButtons".Translate(), ref pawnShowExtraButtons);
            y += RowGap;


        }

        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.ShowSeparators".Translate(), ref showSeparators);
        y += RowGap;

        // 分隔线高度 / 行间距：并排显示在同一行的左右两列。
        {
            float colW = (width - ItemGap) / 2f;
            separatorHeight = HorizontalSlider(new Rect(x, y, colW, SliderHeight), separatorHeight, 0f, 4f,
                label: "BetterInspectPane.SeparatorHeight".Translate(Mathf.RoundToInt(separatorHeight)), roundTo: 1f);
            rowSpacing = HorizontalSlider(new Rect(x + colW + ItemGap, y, colW, SliderHeight), rowSpacing, 0f, 8f,
                label: "BetterInspectPane.RowSpacing".Translate(Mathf.RoundToInt(rowSpacing)), roundTo: 1f);
            y += SliderGap;
        }

        // 条默认高度：所有条的基本高度，各条在「进度条」页通过高度偏移在此基础上增减。
        defaultBarHeight = HorizontalSlider(new Rect(x, y, width, SliderHeight), defaultBarHeight, 4f, 32f,
            label: "BetterInspectPane.DefaultBarHeight".Translate(Mathf.RoundToInt(defaultBarHeight)), roundTo: 1f);
        y += SliderGap;

        // 标签列宽 / 数值列宽：并排显示在同一行的左右两列。
        {
            float colW = (width - ItemGap) / 2f;
            labelWidth = HorizontalSlider(new Rect(x, y, colW, SliderHeight), labelWidth, 40f, 200f,
                label: "BetterInspectPane.LabelWidth".Translate(Mathf.RoundToInt(labelWidth)), roundTo: 1f);
            valueWidth = HorizontalSlider(new Rect(x + colW + ItemGap, y, colW, SliderHeight), valueWidth, 40f, 200f,
                label: "BetterInspectPane.ValueWidth".Translate(Mathf.RoundToInt(valueWidth)), roundTo: 1f);
            y += SliderGap;
        }

        // 标签样式 / 数值显示样式：普通=条外固定列；嵌入=条内反色绘制；隐藏=不显示数值也不预留宽度。
        // 两个下拉并排显示在同一行的左右两列，每列为「标签 + 下拉」。
        // 下拉按钮略向上偏移，避免与下一行滑杆的 label（画在滑轨上方、向上延伸）顶部重叠。
        {
            const float colGap = 12f;
            float colW = (width - colGap) / 2f;

            float labelStyleLabelW = Text.CalcSize("BetterInspectPane.LabelStyleLabel".Translate()).x;
            Widgets.Label(new Rect(x, y, labelStyleLabelW, RowHeight), "BetterInspectPane.LabelStyleLabel".Translate());
            DoEnumDropdown(x + labelStyleLabelW + 4f, y - dropdownNudgeY, colW - labelStyleLabelW - 4f, RowHeight,
                "BetterInspectPane.LabelStyleLabel".Translate(), true, labelStyle, v => labelStyle = v,
                v => ("BetterInspectPane.LabelStyle_" + v).Translate());

            float valueStyleLabelW = Text.CalcSize("BetterInspectPane.BarValueStyleLabel".Translate()).x;
            Widgets.Label(new Rect(x + colW + colGap, y, valueStyleLabelW, RowHeight), "BetterInspectPane.BarValueStyleLabel".Translate());
            DoEnumDropdown(x + colW + colGap + valueStyleLabelW + 4f, y - dropdownNudgeY, colW - valueStyleLabelW - 4f, RowHeight,
                "BetterInspectPane.BarValueStyleLabel".Translate(), true, barValueStyle, v => barValueStyle = v,
                v => ("BetterInspectPane.BarValueStyle_" + v).Translate());
            y += RowGap;
        }

        // 嵌入收缩宽度：标签 / 数值分别设置（普通/隐藏时对应设置无意义，仅当对应样式为嵌入时显示）。
        // 两个滑杆并排显示在同一行的左右两列；仅一个生效时占满整行。
        bool embedLabelShown = labelStyle == LabelStyle.Embedded;
        bool embedValueShown = barValueStyle == BarValueStyle.Embedded;
        if (embedLabelShown || embedValueShown)
        {
            float colW = (width - ItemGap) / 2f;
            if (embedLabelShown)
            {
                embedInsetLabel = HorizontalSlider(new Rect(x, y, embedValueShown ? colW : width, SliderHeight), embedInsetLabel, 0f, 30f,
                    label: "BetterInspectPane.EmbedInsetLabel".Translate(Mathf.RoundToInt(embedInsetLabel)), roundTo: 1f);
            }
            if (embedValueShown)
            {
                embedInsetValue = HorizontalSlider(new Rect(embedLabelShown ? x + colW + ItemGap : x, y, embedLabelShown ? colW : width, SliderHeight), embedInsetValue, 0f, 30f,
                    label: "BetterInspectPane.EmbedInsetValue".Translate(Mathf.RoundToInt(embedInsetValue)), roundTo: 1f);
            }
            y += SliderGap;
        }

        // 字号（像素，可调具体数值）：条标签 / 条数值 / 额外文字。
        // 条标签 / 条数值字号并排显示在同一行的左右两列。
        if (!embedLabelShown || !embedValueShown)
        {
            float colW = (width - ItemGap) / 2f;
            if (!embedLabelShown)
            {
                labelFontSize = Mathf.RoundToInt(HorizontalSlider(new Rect(x, y, !embedValueShown ? colW : width, SliderHeight), labelFontSize, 8f, 24f,
                    label: "BetterInspectPane.LabelFontSize".Translate(labelFontSize), roundTo: 1f));
            }
            if (!embedValueShown)
            {
                valueFontSize = Mathf.RoundToInt(HorizontalSlider(new Rect(!embedLabelShown ? x + colW + ItemGap : x, y, !embedLabelShown ? colW : width, SliderHeight), valueFontSize, 8f, 24f,
                    label: "BetterInspectPane.ValueFontSize".Translate(valueFontSize), roundTo: 1f));
            }
            y += SliderGap;
        }

        infoFontSize = Mathf.RoundToInt(HorizontalSlider(new Rect(x, y, width, SliderHeight), infoFontSize, 8f, 24f,
            label: "BetterInspectPane.InfoFontSize".Translate(infoFontSize), roundTo: 1f));
        y += SliderGap;

        // 检查面板窗口尺寸：最小宽度、最小高度（实际尺寸取原版值与设置值中的较大者）。
        paneWidth = HorizontalSlider(new Rect(x, y, width, SliderHeight), paneWidth, 432f, 800f,
            label: "BetterInspectPane.PaneWidth".Translate(Mathf.RoundToInt(paneWidth)), roundTo: 1f);
        y += SliderGap;

        paneHeight = HorizontalSlider(new Rect(x, y, width, SliderHeight), paneHeight, 165f, 600f,
            label: "BetterInspectPane.PaneHeight".Translate(Mathf.RoundToInt(paneHeight)), roundTo: 1f);
        y += SliderGap;

        // 覆盖 InspectString 绘制：自定义面板路径改用本模组实现（20 帧缓存）。
        // 自适应面板高度：信息文字放不下时自动增高面板（需覆盖 InspectString 绘制开启）。
        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.OverrideInspectString".Translate(), ref overrideInspectString);
        y += RowGap;

        if (overrideInspectString)
        {
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.AdaptivePaneHeight".Translate(), ref adaptivePaneHeight);
            y += RowGap;
        }

        EndPageScroll(SettingsPage.General, y);
    }

    // ----- 第 2 页：进度条（布局 + 主进度条 + 世界地图对象 + 区域）-----
    private void DoBarsPage(Rect inRect)
    {
        BeginPageScroll(inRect, SettingsPage.Bars, out float x, out float y, out float width);

        // 进度条区布局（同一行）：布局下拉 + 列间距滑杆（非单列时）+ 双条行均分开关（三列/四列时）。
        // 行高取滑杆块高度（SliderHeight，滑杆标签画在轨道上方），下拉/复选框在该行内垂直居中。
        // 每条进度条依次设置「显示开关 + 高度 + 数值显示 / 占位 / 显示顺序」。
        y = DoLayoutRow(x, y, width,
            layoutMode, v => layoutMode = v,
            columnGap, v => columnGap = v,
            evenSplitTwoBarRows, v => evenSplitTwoBarRows = v);

        // 进度条设置按当前显示顺序（mainBarOrder，即列表下标）排列显示，▲▼ 可上移/下移交换顺序。
        // 健康条始终显示（setEnabled 为 null）；所有条的高度设置统一为「高度偏移 + 字体缩放」，
        // 实际高度 = 条默认高度（defaultBarHeight）+ 高度偏移（健康条默认 +2，其余默认 0）。
        Dictionary<BarType, BarSetting> mainBars = new Dictionary<BarType, BarSetting>
        {
            { BarType.Health, new BarSetting("BetterInspectPane.HealthLabel".Translate(),
                enabled: true, setEnabled: null, enableKey: null,
                heightOffset: healthBarHeightOffset, setHeightOffset: v => healthBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: healthFontScale, setFontScale: v => healthFontScale = v,
                valueStyle: healthValueStyle, setValueStyle: v => healthValueStyle = v,
                span: healthBarSpan, setSpan: v => healthBarSpan = v,
                type: BarType.Health) },
            { BarType.Shield, new BarSetting("BetterInspectPane.ShieldLabel".Translate(),
                enabled: enableShieldBar, setEnabled: v => enableShieldBar = v, enableKey: "BetterInspectPane.EnableShieldBar",
                heightOffset: shieldBarHeightOffset, setHeightOffset: v => shieldBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: shieldFontScale, setFontScale: v => shieldFontScale = v,
                valueStyle: shieldValueStyle, setValueStyle: v => shieldValueStyle = v,
                span: shieldBarSpan, setSpan: v => shieldBarSpan = v,
                type: BarType.Shield,
                autoHide: autoHideShieldAtZero, setAutoHide: v => autoHideShieldAtZero = v,
                autoHideExtreme: "0%", autoHideTip: "BetterInspectPane.AutoHideBarTip") },
            { BarType.Ammo, new BarSetting("BetterInspectPane.AmmoLabel".Translate(),
                enabled: enableAmmoBar, setEnabled: v => enableAmmoBar = v, enableKey: "BetterInspectPane.EnableAmmoBar",
                heightOffset: ammoBarHeightOffset, setHeightOffset: v => ammoBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: ammoFontScale, setFontScale: v => ammoFontScale = v,
                valueStyle: ammoValueStyle, setValueStyle: v => ammoValueStyle = v,
                span: ammoBarSpan, setSpan: v => ammoBarSpan = v,
                type: BarType.Ammo) },
            { BarType.Freshness, new BarSetting("BetterInspectPane.FreshnessLabel".Translate(),
                enabled: enableFreshnessBar, setEnabled: v => enableFreshnessBar = v, enableKey: "BetterInspectPane.EnableFreshnessBar",
                heightOffset: freshnessBarHeightOffset, setHeightOffset: v => freshnessBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: freshnessFontScale, setFontScale: v => freshnessFontScale = v,
                valueStyle: freshnessValueStyle, setValueStyle: v => freshnessValueStyle = v,
                span: freshnessBarSpan, setSpan: v => freshnessBarSpan = v,
                type: BarType.Freshness) },
            { BarType.Work, new BarSetting("BetterInspectPane.WorkLabel".Translate(),
                enabled: enableWorkBar, setEnabled: v => enableWorkBar = v, enableKey: "BetterInspectPane.EnableWorkBar",
                heightOffset: workBarHeightOffset, setHeightOffset: v => workBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: workFontScale, setFontScale: v => workFontScale = v,
                valueStyle: workValueStyle, setValueStyle: v => workValueStyle = v,
                span: workBarSpan, setSpan: v => workBarSpan = v,
                type: BarType.Work,
                autoHide: autoHideWorkAtHundred, setAutoHide: v => autoHideWorkAtHundred = v,
                autoHideExtreme: "100%", autoHideTip: "BetterInspectPane.AutoHideBarTip") },
            // 研究条：研究台当前研究项目进度。
            { BarType.Research, new BarSetting("BetterInspectPane.ResearchLabel".Translate(),
                enabled: enableResearchBar, setEnabled: v => enableResearchBar = v, enableKey: "BetterInspectPane.EnableResearchBar",
                heightOffset: researchBarHeightOffset, setHeightOffset: v => researchBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: researchFontScale, setFontScale: v => researchFontScale = v,
                valueStyle: researchValueStyle, setValueStyle: v => researchValueStyle = v,
                span: researchBarSpan, setSpan: v => researchBarSpan = v,
                type: BarType.Research,
                autoHide: autoHideResearchAtHundred, setAutoHide: v => autoHideResearchAtHundred = v,
                autoHideExtreme: "100%", autoHideTip: "BetterInspectPane.AutoHideBarTip") },
            { BarType.Growth, new BarSetting("BetterInspectPane.GrowthLabel".Translate(),
                enabled: enableGrowthBar, setEnabled: v => enableGrowthBar = v, enableKey: "BetterInspectPane.EnableGrowthBar",
                heightOffset: growthBarHeightOffset, setHeightOffset: v => growthBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: growthFontScale, setFontScale: v => growthFontScale = v,
                valueStyle: growthValueStyle, setValueStyle: v => growthValueStyle = v,
                span: growthBarSpan, setSpan: v => growthBarSpan = v,
                type: BarType.Growth) },
            // 剩余容量条：既能出现在物品/建筑面板（储存容器建筑）也能出现在区域面板（储存区），
            // 两处共用同一组开关/外观设置，但顺序彼此独立，故主面板与区域面板各有一条。
            { BarType.Storage, new BarSetting("BetterInspectPane.StorageLabel".Translate(),
                enabled: enableStorageBar, setEnabled: v => enableStorageBar = v, enableKey: "BetterInspectPane.EnableStorageBar",
                heightOffset: storageBarHeightOffset, setHeightOffset: v => storageBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: storageFontScale, setFontScale: v => storageFontScale = v,
                valueStyle: storageValueStyle, setValueStyle: v => storageValueStyle = v,
                span: storageBarSpan, setSpan: v => storageBarSpan = v,
                type: BarType.Storage) },
            // 电池蓄电条：电池自身蓄电量。
            { BarType.Battery, new BarSetting("BetterInspectPane.BatteryLabel".Translate(),
                enabled: enableBatteryBar, setEnabled: v => enableBatteryBar = v, enableKey: "BetterInspectPane.EnableBatteryBar",
                heightOffset: batteryBarHeightOffset, setHeightOffset: v => batteryBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: batteryFontScale, setFontScale: v => batteryFontScale = v,
                valueStyle: batteryValueStyle, setValueStyle: v => batteryValueStyle = v,
                span: batteryBarSpan, setSpan: v => batteryBarSpan = v,
                type: BarType.Battery) },
            // 电网蓄电条：电力设备（用电/发电建筑，含电池）所连电网的蓄电量。
            { BarType.PowerGrid, new BarSetting("BetterInspectPane.PowerGridLabel".Translate(),
                enabled: enablePowerGridBar, setEnabled: v => enablePowerGridBar = v, enableKey: "BetterInspectPane.EnablePowerGridBar",
                heightOffset: powerGridBarHeightOffset, setHeightOffset: v => powerGridBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: powerGridFontScale, setFontScale: v => powerGridFontScale = v,
                valueStyle: powerGridValueStyle, setValueStyle: v => powerGridValueStyle = v,
                span: powerGridBarSpan, setSpan: v => powerGridBarSpan = v,
                type: BarType.PowerGrid) },
            // 维护条：Vanilla Gravship Expanded 需维护建筑的维护剩余量（反射读取）。
            { BarType.Maintenance, new BarSetting("BetterInspectPane.MaintenanceLabel".Translate(),
                enabled: enableMaintenanceBar, setEnabled: v => enableMaintenanceBar = v, enableKey: "BetterInspectPane.EnableMaintenanceBar",
                heightOffset: maintenanceBarHeightOffset, setHeightOffset: v => maintenanceBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: maintenanceFontScale, setFontScale: v => maintenanceFontScale = v,
                valueStyle: maintenanceValueStyle, setValueStyle: v => maintenanceValueStyle = v,
                span: maintenanceBarSpan, setSpan: v => maintenanceBarSpan = v,
                type: BarType.Maintenance) },
            // 管道网络条：所在管道网络的整网内容物 / 总容量（反射读取 PipeSystem）。
            { BarType.PipeNet, new BarSetting("BetterInspectPane.PipeNetLabel".Translate(),
                enabled: enablePipeNetBar, setEnabled: v => enablePipeNetBar = v, enableKey: "BetterInspectPane.EnablePipeNetBar",
                heightOffset: pipeNetBarHeightOffset, setHeightOffset: v => pipeNetBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: pipeNetFontScale, setFontScale: v => pipeNetFontScale = v,
                valueStyle: pipeNetValueStyle, setValueStyle: v => pipeNetValueStyle = v,
                span: pipeNetBarSpan, setSpan: v => pipeNetBarSpan = v,
                type: BarType.PipeNet) },
            // 冷却条：炮塔开火后的冷却倒计时（反射读取 burstCooldownTicksLeft）。
            { BarType.Cooldown, new BarSetting("BetterInspectPane.CooldownLabel".Translate(),
                enabled: enableCooldownBar, setEnabled: v => enableCooldownBar = v, enableKey: "BetterInspectPane.EnableCooldownBar",
                heightOffset: cooldownBarHeightOffset, setHeightOffset: v => cooldownBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: cooldownFontScale, setFontScale: v => cooldownFontScale = v,
                valueStyle: cooldownValueStyle, setValueStyle: v => cooldownValueStyle = v,
                span: cooldownBarSpan, setSpan: v => cooldownBarSpan = v,
                type: BarType.Cooldown,
                autoHide: autoHideCooldownAtHundred, setAutoHide: v => autoHideCooldownAtHundred = v,
                autoHideExtreme: "100%", autoHideTip: "BetterInspectPane.AutoHideBarTip") },
            // 充能条：飞船反应堆 / 机械孕育器 / 定时激活器 / 清污泵 / 排水泵的充能进度。
            { BarType.Charge, new BarSetting("BetterInspectPane.ChargeLabel".Translate(),
                enabled: enableChargeBar, setEnabled: v => enableChargeBar = v, enableKey: "BetterInspectPane.EnableChargeBar",
                heightOffset: chargeBarHeightOffset, setHeightOffset: v => chargeBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                fontScale: chargeFontScale, setFontScale: v => chargeFontScale = v,
                valueStyle: chargeValueStyle, setValueStyle: v => chargeValueStyle = v,
                span: chargeBarSpan, setSpan: v => chargeBarSpan = v,
                type: BarType.Charge,
                autoHide: autoHideChargeAtHundred, setAutoHide: v => autoHideChargeAtHundred = v,
                autoHideExtreme: "100%", autoHideTip: "BetterInspectPane.AutoHideBarTip") }
        };
        y = DoBarSettingsRows(x, y, width, mainBarOrder, mainBars, layoutMode != LayoutMode.Single);

        // 健康（耐久）条：为不使用命中点（无限耐久 / 无法损坏）的对象在单选检查面板也显示一条永远满格的耐久条（数值 ∞）。
        {
            Rect infiniteRect = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(infiniteRect, "BetterInspectPane.ShowInfiniteDurabilityBar".Translate(), ref showInfiniteDurabilityBar);
            TooltipHandler.TipRegion(infiniteRect, "BetterInspectPane.ShowInfiniteDurabilityBar_Tip".Translate());
            y += RowGap;
        }

        y += ItemGap;
        if (enablePipeNetBar)
        {
            // 管道条颜色：使用资源颜色（容量条原样 / 管道网络条 70% 透明度）。
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.UseResourceColor".Translate(), ref useResourceColor);
            y += RowGap;
        }
        if (enableCooldownBar)
        {
            // 冷却条：反转填充比例（默认由空渐满，开启后由满渐空）。
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.InvertCooldownFill".Translate(), ref invertCooldownFill);
            y += RowGap;
            // 冷却条：最短显示冷却时长（秒），总冷却低于该值的炮塔不显示冷却条（0 = 全部显示）。
            minCooldownSeconds = HorizontalSlider(new Rect(x, y, width, SliderHeight), minCooldownSeconds, 0f, 10f,
                label: "BetterInspectPane.MinCooldownSeconds".Translate(minCooldownSeconds.ToString("0.#")), roundTo: 0.5f);
            y += SliderGap;
        }

        // 分隔线。
        y += 8f;
        Widgets.DrawLineHorizontal(x, y, width, new Color(0.4f, 0.4f, 0.4f));
        y += 8f;

        // Pawn：健康（复用健康条设置）/ 血液 / 心情 / 食物 / 休息 / 娱乐 / 机械能量。
        // 布局（单列/两列/三列/四列）与普通面板互相独立；时间表/允许区域由 Pawn 策略行组件绘制（开关见常规页）。
        y += ItemGap;
        if (enablePawns)
        {
            Widgets.Label(new Rect(x, y, width, RowHeight), "BetterInspectPane.PawnSection".Translate());
            y += RowGap;

            // Pawn 条阈值标记总开关：心情（轻度/中度/重度崩溃）/ 血液（各流血阶段）/ 疼痛（疼痛休克）
            // 三者的黑色 1px 竖直阈值线（默认开启）。
            Rect thresholdMarkersToggleRect = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(thresholdMarkersToggleRect,
                "BetterInspectPane.EnableThresholdMarkers".Translate(), ref enableThresholdMarkers);
            TooltipHandler.TipRegion(thresholdMarkersToggleRect,
                "BetterInspectPane.EnableThresholdMarkers_Tip".Translate());
            y += RowGap;

            // 阈值标记高度：从条底部向上延伸的长度（px，默认 2）。
            if (enableThresholdMarkers)
            {
                thresholdMarkerHeight = HorizontalSlider(new Rect(x, y, width, SliderHeight), thresholdMarkerHeight, 1f, 20f,
                    label: "BetterInspectPane.ThresholdMarkerHeight".Translate(thresholdMarkerHeight.ToString("0.#")), roundTo: 1f);
                y += SliderGap;
            }

            // Pawn 布局行（与普通面板布局互相独立）。
            y = DoLayoutRow(x, y, width,
                pawnLayoutMode, v => pawnLayoutMode = v,
                pawnColumnGap, v => pawnColumnGap = v,
                pawnEvenSplitTwoBarRows, v => pawnEvenSplitTwoBarRows = v);

            Dictionary<BarType, BarSetting> pawnBars = new Dictionary<BarType, BarSetting>
            {
                // Pawn 健康条与物品/建筑健康条外观设置互相独立（高度/字号/数值样式/占位各自调整，
                // 颜色取通用 full/mid/low 分级）；显示顺序随 pawnBarOrder 独立排列；标签用独立键（Pawn 显示「健康」而非物品的「耐久」）。
                { BarType.Health, new BarSetting("BetterInspectPane.PawnHealthLabel".Translate(),
                    enabled: true, setEnabled: null, enableKey: null,
                    heightOffset: pawnHealthBarHeightOffset, setHeightOffset: v => pawnHealthBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: pawnHealthFontScale, setFontScale: v => pawnHealthFontScale = v,
                    valueStyle: pawnHealthValueStyle, setValueStyle: v => pawnHealthValueStyle = v,
                    span: pawnHealthBarSpan, setSpan: v => pawnHealthBarSpan = v,
                    type: BarType.Health) },
                { BarType.Bleed, new BarSetting("BetterInspectPane.BloodLabel".Translate(),
                    enabled: enableBloodBar, setEnabled: v => enableBloodBar = v, enableKey: "BetterInspectPane.EnableBloodBar",
                    heightOffset: bloodBarHeightOffset, setHeightOffset: v => bloodBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: bloodFontScale, setFontScale: v => bloodFontScale = v,
                    valueStyle: bloodValueStyle, setValueStyle: v => bloodValueStyle = v,
                    span: bloodBarSpan, setSpan: v => bloodBarSpan = v,
                    type: BarType.Bleed,
                    autoHide: autoHideBloodAtFull, setAutoHide: v => autoHideBloodAtFull = v,
                    autoHideExtreme: "100%", autoHideTip: "BetterInspectPane.AutoHideBarTip") },
                { BarType.Mood, new BarSetting("BetterInspectPane.MoodLabel".Translate(),
                    enabled: enableMoodBar, setEnabled: v => enableMoodBar = v, enableKey: "BetterInspectPane.EnableMoodBar",
                    heightOffset: moodBarHeightOffset, setHeightOffset: v => moodBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: moodFontScale, setFontScale: v => moodFontScale = v,
                    valueStyle: moodValueStyle, setValueStyle: v => moodValueStyle = v,
                    span: moodBarSpan, setSpan: v => moodBarSpan = v,
                    type: BarType.Mood) },
                { BarType.Food, new BarSetting("BetterInspectPane.FoodLabel".Translate(),
                    enabled: enableFoodBar, setEnabled: v => enableFoodBar = v, enableKey: "BetterInspectPane.EnableFoodBar",
                    heightOffset: foodBarHeightOffset, setHeightOffset: v => foodBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: foodFontScale, setFontScale: v => foodFontScale = v,
                    valueStyle: foodValueStyle, setValueStyle: v => foodValueStyle = v,
                    span: foodBarSpan, setSpan: v => foodBarSpan = v,
                    type: BarType.Food) },
                { BarType.Rest, new BarSetting("BetterInspectPane.RestLabel".Translate(),
                    enabled: enableRestBar, setEnabled: v => enableRestBar = v, enableKey: "BetterInspectPane.EnableRestBar",
                    heightOffset: restBarHeightOffset, setHeightOffset: v => restBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: restFontScale, setFontScale: v => restFontScale = v,
                    valueStyle: restValueStyle, setValueStyle: v => restValueStyle = v,
                    span: restBarSpan, setSpan: v => restBarSpan = v,
                    type: BarType.Rest) },
                { BarType.Joy, new BarSetting("BetterInspectPane.JoyLabel".Translate(),
                    enabled: enableJoyBar, setEnabled: v => enableJoyBar = v, enableKey: "BetterInspectPane.EnableJoyBar",
                    heightOffset: joyBarHeightOffset, setHeightOffset: v => joyBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: joyFontScale, setFontScale: v => joyFontScale = v,
                    valueStyle: joyValueStyle, setValueStyle: v => joyValueStyle = v,
                    span: joyBarSpan, setSpan: v => joyBarSpan = v,
                    type: BarType.Joy) },
                { BarType.MechEnergy, new BarSetting("BetterInspectPane.MechEnergyLabel".Translate(),
                    enabled: enableMechEnergyBar, setEnabled: v => enableMechEnergyBar = v, enableKey: "BetterInspectPane.EnableMechEnergyBar",
                    heightOffset: mechEnergyBarHeightOffset, setHeightOffset: v => mechEnergyBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: mechEnergyFontScale, setFontScale: v => mechEnergyFontScale = v,
                    valueStyle: mechEnergyValueStyle, setValueStyle: v => mechEnergyValueStyle = v,
                    span: mechEnergyBarSpan, setSpan: v => mechEnergyBarSpan = v,
                    type: BarType.MechEnergy) },
                // 产物条：可挤奶动物（CompMilkable）的奶水充盈进度。
                { BarType.Milk, new BarSetting("BetterInspectPane.MilkLabel".Translate(),
                    enabled: enableMilkBar, setEnabled: v => enableMilkBar = v, enableKey: "BetterInspectPane.EnableMilkBar",
                    heightOffset: milkBarHeightOffset, setHeightOffset: v => milkBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: milkFontScale, setFontScale: v => milkFontScale = v,
                    valueStyle: milkValueStyle, setValueStyle: v => milkValueStyle = v,
                    span: milkBarSpan, setSpan: v => milkBarSpan = v,
                    type: BarType.Milk) },
                // 产毛条：可剪毛动物（CompShearable）的羊毛生长进度。
                { BarType.Wool, new BarSetting("BetterInspectPane.WoolLabel".Translate(),
                    enabled: enableWoolBar, setEnabled: v => enableWoolBar = v, enableKey: "BetterInspectPane.EnableWoolBar",
                    heightOffset: woolBarHeightOffset, setHeightOffset: v => woolBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: woolFontScale, setFontScale: v => woolFontScale = v,
                    valueStyle: woolValueStyle, setValueStyle: v => woolValueStyle = v,
                    span: woolBarSpan, setSpan: v => woolBarSpan = v,
                    type: BarType.Wool) },
                // 繁殖条：下蛋动物（CompEggLayer）产蛋进度或怀孕（Hediff_Pregnant）妊娠进度。
                { BarType.Breeding, new BarSetting("BetterInspectPane.BreedingLabel".Translate(),
                    enabled: enableBreedingBar, setEnabled: v => enableBreedingBar = v, enableKey: "BetterInspectPane.EnableBreedingBar",
                    heightOffset: breedingBarHeightOffset, setHeightOffset: v => breedingBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: breedingFontScale, setFontScale: v => breedingFontScale = v,
                    valueStyle: breedingValueStyle, setValueStyle: v => breedingValueStyle = v,
                    span: breedingBarSpan, setSpan: v => breedingBarSpan = v,
                    type: BarType.Breeding) },
                // 疼痛条：疼痛比例（0% 时自动隐藏，可关闭）。
                { BarType.Pain, new BarSetting("BetterInspectPane.PainLabel".Translate(),
                    enabled: enablePainBar, setEnabled: v => enablePainBar = v, enableKey: "BetterInspectPane.EnablePainBar",
                    heightOffset: painBarHeightOffset, setHeightOffset: v => painBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: painFontScale, setFontScale: v => painFontScale = v,
                    valueStyle: painValueStyle, setValueStyle: v => painValueStyle = v,
                    span: painBarSpan, setSpan: v => painBarSpan = v,
                    type: BarType.Pain,
                    autoHide: autoHidePainAtZero, setAutoHide: v => autoHidePainAtZero = v,
                    autoHideExtreme: "0%", autoHideTip: "BetterInspectPane.AutoHideBarTip") },
                // 膀胱条：Dubs Bad Hygiene 等模组需求（按 defName Bladder 匹配，只要有对应需求就显示）。
                { BarType.Bladder, new BarSetting("BetterInspectPane.BladderLabel".Translate(),
                    enabled: enableBladderBar, setEnabled: v => enableBladderBar = v, enableKey: "BetterInspectPane.EnableBladderBar",
                    heightOffset: bladderBarHeightOffset, setHeightOffset: v => bladderBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: bladderFontScale, setFontScale: v => bladderFontScale = v,
                    valueStyle: bladderValueStyle, setValueStyle: v => bladderValueStyle = v,
                    span: bladderBarSpan, setSpan: v => bladderBarSpan = v,
                    type: BarType.Bladder) },
                // 卫生条：Dubs Bad Hygiene 等模组需求（按 defName Hygiene 匹配，只要有对应需求就显示）。
                { BarType.Hygiene, new BarSetting("BetterInspectPane.HygieneLabel".Translate(),
                    enabled: enableHygieneBar, setEnabled: v => enableHygieneBar = v, enableKey: "BetterInspectPane.EnableHygieneBar",
                    heightOffset: hygieneBarHeightOffset, setHeightOffset: v => hygieneBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: hygieneFontScale, setFontScale: v => hygieneFontScale = v,
                    valueStyle: hygieneValueStyle, setValueStyle: v => hygieneValueStyle = v,
                    span: hygieneBarSpan, setSpan: v => hygieneBarSpan = v,
                    type: BarType.Hygiene) },
                // 口渴条：Dubs Bad Hygiene 等模组需求（按 defName DBHThirst 匹配，只要有对应需求就显示）。
                { BarType.Thirst, new BarSetting("BetterInspectPane.ThirstLabel".Translate(),
                    enabled: enableThirstBar, setEnabled: v => enableThirstBar = v, enableKey: "BetterInspectPane.EnableThirstBar",
                    heightOffset: thirstBarHeightOffset, setHeightOffset: v => thirstBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: thirstFontScale, setFontScale: v => thirstFontScale = v,
                    valueStyle: thirstValueStyle, setValueStyle: v => thirstValueStyle = v,
                    span: thirstBarSpan, setSpan: v => thirstBarSpan = v,
                    type: BarType.Thirst) }
            };
            y = DoBarSettingsRows(x, y, width, pawnBarOrder, pawnBars, pawnLayoutMode != LayoutMode.Single);

            // 膀胱/口渴需求条的反转比例选项（仅对应条启用时显示）。
            if (enableBladderBar)
            {
                Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                    "BetterInspectPane.InvertBladderFill".Translate(), ref invertBladderFill);
                y += RowGap;
            }
            if (enableThirstBar)
            {
                Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                    "BetterInspectPane.InvertThirstFill".Translate(), ref invertThirstFill);
                y += RowGap;
            }
            // 疼痛条反转填充选项（仅疼痛条启用时显示）。
            if (enablePainBar)
            {
                Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                    "BetterInspectPane.InvertPainFill".Translate(), ref invertPainFill);
                y += RowGap;
            }
        }
        // Pawn 策略行（食物/管制/着装/区域）：显示开关与字号，仅启用 Pawn 面板时显示。
        if (enablePawns)
        {
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                "BetterInspectPane.EnablePawnPolicyRow".Translate(), ref enablePawnPolicyRow);
            y += RowGap;

            if (enablePawnPolicyRow)
            {
                // 行字号 / 行高：并排显示在同一行的左右两列。
                float colW = (width - ItemGap) / 2f;
                policyRowFontSize = Mathf.RoundToInt(HorizontalSlider(new Rect(x, y, colW, SliderHeight), policyRowFontSize, 8f, 24f,
                    label: "BetterInspectPane.PolicyRowFontSize".Translate(policyRowFontSize), roundTo: 1f));
                policyRowHeight = HorizontalSlider(new Rect(x + colW + ItemGap, y, colW, SliderHeight), policyRowHeight, 12f, 40f,
                    label: "BetterInspectPane.PolicyRowHeight".Translate(Mathf.RoundToInt(policyRowHeight)), roundTo: 1f);
                y += SliderGap;
            }
        }
        // Pawn 技能区（需 enablePawns 开启自定义 Pawn 面板内容；与 overrideInspectPane 无关）。
        if (enablePawns)
        {
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                "BetterInspectPane.PawnShowSkills".Translate(), ref pawnShowSkills);
            y += RowGap;

            if (pawnShowSkills)
            {
                // 技能条悬浮提示（默认开启）：悬停技能条时显示原版角色面板的技能提示。
                Rect skillTooltipRect = new Rect(x, y, width, RowHeight);
                Widgets.CheckboxLabeled(skillTooltipRect, "BetterInspectPane.PawnSkillTooltip".Translate(), ref pawnSkillTooltip);
                TooltipHandler.TipRegion(skillTooltipRect, "BetterInspectPane.PawnSkillTooltipTip".Translate());
                y += RowGap;

                // 技能条纯文本样式（默认关闭）：仅绘制技能名与等级文本，不绘制条背景与进度填充。
                Rect skillPlainTextRect = new Rect(x, y, width, RowHeight);
                Widgets.CheckboxLabeled(skillPlainTextRect, "BetterInspectPane.PawnSkillPlainText".Translate(), ref pawnSkillPlainText);
                TooltipHandler.TipRegion(skillPlainTextRect, "BetterInspectPane.PawnSkillPlainTextTip".Translate());
                y += RowGap;

                // 技能条按总进度填充（默认关闭）：开启后填充比例 = (当前等级 + 当前升级进度%) / 技能等级上限 20。
                Rect skillTotalProgressRect = new Rect(x, y, width, RowHeight);
                Widgets.CheckboxLabeled(skillTotalProgressRect, "BetterInspectPane.PawnSkillTotalProgress".Translate(), ref pawnSkillTotalProgress);
                TooltipHandler.TipRegion(skillTotalProgressRect, "BetterInspectPane.PawnSkillTotalProgressTip".Translate());
                y += RowGap;

                // 技能等级显示升级进度小数（默认关闭）：等级以固定两位小数显示，如 5 级 + 55% 升级进度 = 5.55，整级 / 满级 = 5.00 / 20.00。
                Rect skillProgressTextRect = new Rect(x, y, width, RowHeight);
                Widgets.CheckboxLabeled(skillProgressTextRect, "BetterInspectPane.PawnSkillShowProgressText".Translate(), ref pawnSkillShowProgressText);
                TooltipHandler.TipRegion(skillProgressTextRect, "BetterInspectPane.PawnSkillShowProgressTextTip".Translate());
                y += RowGap;

                // 技能条文本用白色字体（默认开启）：开启后标签与数值无视进度条比例，总是以白色显示。
                Rect skillWhiteFontRect = new Rect(x, y, width, RowHeight);
                Widgets.CheckboxLabeled(skillWhiteFontRect, "BetterInspectPane.PawnSkillWhiteFont".Translate(), ref pawnSkillWhiteFont);
                TooltipHandler.TipRegion(skillWhiteFontRect, "BetterInspectPane.PawnSkillWhiteFontTip".Translate());
                y += RowGap;

                // 每行列数（3 / 4 / 6）：标签 + 下拉并排。
                float skillColLabelW = Text.CalcSize("BetterInspectPane.PawnSkillColumns".Translate()).x;
                Widgets.Label(new Rect(x, y, skillColLabelW, RowHeight), "BetterInspectPane.PawnSkillColumns".Translate());
                DoEnumDropdown(x + skillColLabelW + 4f, y - dropdownNudgeY, width - skillColLabelW - 4f, RowHeight,
                    "BetterInspectPane.PawnSkillColumns".Translate(), true, pawnSkillColumns, v => pawnSkillColumns = v,
                    v => ("BetterInspectPane.PawnSkillColumns_" + v).Translate());
                y += RowGap;

                // 激情表示方式（文本 + / ++ / 原版图标）：标签 + 下拉并排。
                float passionLabelW = Text.CalcSize("BetterInspectPane.PawnSkillPassionStyle".Translate()).x;
                Widgets.Label(new Rect(x, y, passionLabelW, RowHeight), "BetterInspectPane.PawnSkillPassionStyle".Translate());
                DoEnumDropdown(x + passionLabelW + 4f, y - dropdownNudgeY, width - passionLabelW - 4f, RowHeight,
                    "BetterInspectPane.PawnSkillPassionStyle_Tip".Translate(), true, pawnSkillPassionStyle, v => pawnSkillPassionStyle = v,
                    v => ("BetterInspectPane.PawnSkillPassionStyle_" + v).Translate());
                y += RowGap;

                // 激情图标置于技能名称后（仅图标样式时显示）：默认图标绘制在技能名左侧，开启后改绘制在技能名文本右侧。
                if (pawnSkillPassionStyle == SkillPassionStyle.Icon)
                {
                    Rect passionIconAfterRect = new Rect(x, y, width, RowHeight);
                    Widgets.CheckboxLabeled(passionIconAfterRect, "BetterInspectPane.PawnSkillPassionIconAfterName".Translate(), ref pawnSkillPassionIconAfterName);
                    TooltipHandler.TipRegion(passionIconAfterRect, "BetterInspectPane.PawnSkillPassionIconAfterNameTip".Translate());
                    y += RowGap;
                }

                // 字号 / 高度：并排显示在同一行的左右两列。
                {
                    float colW = (width - ItemGap) / 2f;
                    pawnSkillFontSize = Mathf.RoundToInt(HorizontalSlider(new Rect(x, y, colW, SliderHeight), pawnSkillFontSize, 8f, 24f,
                        label: "BetterInspectPane.PawnSkillFontSize".Translate(pawnSkillFontSize), roundTo: 1f));
                    pawnSkillBarHeight = HorizontalSlider(new Rect(x + colW + ItemGap, y, colW, SliderHeight), pawnSkillBarHeight, 4f, 32f,
                        label: "BetterInspectPane.PawnSkillBarHeight".Translate(Mathf.RoundToInt(pawnSkillBarHeight)), roundTo: 1f);
                    y += SliderGap;
                }

                // 列间距 / 间隔：并排显示在同一行的左右两列。
                // 列间距 = 相邻两列之间的空隙；间隔 = 技能行之间、以及与上下分隔线的间隔。
                {
                    float colW = (width - ItemGap) / 2f;
                    pawnSkillColumnGap = HorizontalSlider(new Rect(x, y, colW, SliderHeight), pawnSkillColumnGap, 0f, 12f,
                        label: "BetterInspectPane.PawnSkillColumnGap".Translate(Mathf.RoundToInt(pawnSkillColumnGap)), roundTo: 1f);
                    pawnSkillSpacing = HorizontalSlider(new Rect(x + colW + ItemGap, y, colW, SliderHeight), pawnSkillSpacing, 0f, 12f,
                        label: "BetterInspectPane.PawnSkillSpacing".Translate(Mathf.RoundToInt(pawnSkillSpacing)), roundTo: 1f);
                    y += SliderGap;
                }
            }
        }

        // 分隔线。
        y += 8f;
        Widgets.DrawLineHorizontal(x, y, width, new Color(0.4f, 0.4f, 0.4f));
        y += 8f;

        // 世界地图对象：剩余时间条 / 阵营关系条（颜色固定，无取色器）。
        y += ItemGap;
        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableWorldObjects".Translate(), ref enableWorldObjects);
        y += RowGap;
        if (enableWorldObjects)
        {
            Widgets.Label(new Rect(x, y, width, RowHeight), "BetterInspectPane.WorldObjectSection".Translate());
            y += RowGap;

            Dictionary<BarType, BarSetting> worldObjectBars = new Dictionary<BarType, BarSetting>
            {
                { BarType.TimeRemaining, new BarSetting("BetterInspectPane.TimeRemainingLabel".Translate(),
                    enabled: enableTimeRemainingBar, setEnabled: v => enableTimeRemainingBar = v, enableKey: "BetterInspectPane.EnableTimeRemainingBar",
                    heightOffset: timeRemainingBarHeightOffset, setHeightOffset: v => timeRemainingBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: timeRemainingFontScale, setFontScale: v => timeRemainingFontScale = v,
                    valueStyle: timeRemainingValueStyle, setValueStyle: v => timeRemainingValueStyle = v,
                    span: timeRemainingBarSpan, setSpan: v => timeRemainingBarSpan = v,
                    type: BarType.TimeRemaining,
                    autoHide: autoHideTimeRemainingAtZero, setAutoHide: v => autoHideTimeRemainingAtZero = v,
                    autoHideExtreme: "0%", autoHideTip: "BetterInspectPane.AutoHideBarTip") },
                { BarType.FactionRelation, new BarSetting("BetterInspectPane.FactionRelationLabel".Translate(),
                    enabled: enableFactionRelationBar, setEnabled: v => enableFactionRelationBar = v, enableKey: "BetterInspectPane.EnableFactionRelationBar",
                    heightOffset: factionRelationBarHeightOffset, setHeightOffset: v => factionRelationBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: factionRelationFontScale, setFontScale: v => factionRelationFontScale = v,
                    valueStyle: factionRelationValueStyle, setValueStyle: v => factionRelationValueStyle = v,
                    span: factionRelationBarSpan, setSpan: v => factionRelationBarSpan = v,
                    type: BarType.FactionRelation) },
                { BarType.Strength, new BarSetting("BetterInspectPane.StrengthLabel".Translate(),
                    enabled: enableStrengthBar, setEnabled: v => enableStrengthBar = v, enableKey: "BetterInspectPane.EnableStrengthBar",
                    heightOffset: strengthBarHeightOffset, setHeightOffset: v => strengthBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: strengthFontScale, setFontScale: v => strengthFontScale = v,
                    valueStyle: strengthValueStyle, setValueStyle: v => strengthValueStyle = v,
                    span: strengthBarSpan, setSpan: v => strengthBarSpan = v,
                    type: BarType.Strength) }
            };
            y = DoBarSettingsRows(x, y, width, worldObjectBarOrder, worldObjectBars, layoutMode != LayoutMode.Single);

            // 使用关系阈值标记：关系条上限改用「关系上限」并在盟友阈值（75）位置画阈值标记
            // （World Domination 2 激活时上限取其设置中的关系上限，未激活时用原版好感上限 100）。
            Rect relationMarkerRow = new Rect(x + 20f, y, width - 20f, RowHeight);
            Widgets.CheckboxLabeled(relationMarkerRow, "BetterInspectPane.ShowRelationThresholdMarker".Translate(), ref showRelationThresholdMarker);
            TooltipHandler.TipRegion(relationMarkerRow, "BetterInspectPane.ShowRelationThresholdMarker_Tip".Translate());
            y += RowGap;
        }

        // 分隔线。
        y += 8f;
        Widgets.DrawLineHorizontal(x, y, width, new Color(0.4f, 0.4f, 0.4f));
        y += 8f;

        // 区域：已种植条（种植区）/ 剩余容量条（储存区）/ 剩余鱼条（钓鱼区）（颜色见底部取色器）。
        y += ItemGap;
        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableZones".Translate(), ref enableZones);
        y += RowGap;
        if (enableZones)
        {
            Widgets.Label(new Rect(x, y, width, RowHeight), "BetterInspectPane.ZoneSection".Translate());
            y += RowGap;

            Dictionary<BarType, BarSetting> zoneBars = new Dictionary<BarType, BarSetting>
            {
                { BarType.Growing, new BarSetting("BetterInspectPane.GrowingLabel".Translate(),
                    enabled: enableGrowingBar, setEnabled: v => enableGrowingBar = v, enableKey: "BetterInspectPane.EnableGrowingBar",
                    heightOffset: growingBarHeightOffset, setHeightOffset: v => growingBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: growingFontScale, setFontScale: v => growingFontScale = v,
                    valueStyle: growingValueStyle, setValueStyle: v => growingValueStyle = v,
                    span: growingBarSpan, setSpan: v => growingBarSpan = v,
                    type: BarType.Growing) },
                // 总体生长条：种植区已种植作物的平均生长水平（数值样式按设定作物估算剩余生长时间）。
                { BarType.ZoneGrowth, new BarSetting("BetterInspectPane.ZoneGrowthLabel".Translate(),
                    enabled: enableZoneGrowthBar, setEnabled: v => enableZoneGrowthBar = v, enableKey: "BetterInspectPane.EnableZoneGrowthBar",
                    heightOffset: zoneGrowthBarHeightOffset, setHeightOffset: v => zoneGrowthBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: zoneGrowthFontScale, setFontScale: v => zoneGrowthFontScale = v,
                    valueStyle: zoneGrowthValueStyle, setValueStyle: v => zoneGrowthValueStyle = v,
                    span: zoneGrowthBarSpan, setSpan: v => zoneGrowthBarSpan = v,
                    type: BarType.ZoneGrowth) },
                { BarType.Storage, new BarSetting("BetterInspectPane.StorageLabel".Translate(),
                    enabled: enableStorageBar, setEnabled: v => enableStorageBar = v, enableKey: "BetterInspectPane.EnableStorageBar",
                    heightOffset: storageBarHeightOffset, setHeightOffset: v => storageBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: storageFontScale, setFontScale: v => storageFontScale = v,
                    valueStyle: storageValueStyle, setValueStyle: v => storageValueStyle = v,
                    span: storageBarSpan, setSpan: v => storageBarSpan = v,
                    type: BarType.Storage) },
                { BarType.Fishing, new BarSetting("BetterInspectPane.FishingLabel".Translate(),
                    enabled: enableFishingBar, setEnabled: v => enableFishingBar = v, enableKey: "BetterInspectPane.EnableFishingBar",
                    heightOffset: fishingBarHeightOffset, setHeightOffset: v => fishingBarHeightOffset = v, minHeightOffset: -6f, maxHeightOffset: 14f,
                    fontScale: fishingFontScale, setFontScale: v => fishingFontScale = v,
                    valueStyle: fishingValueStyle, setValueStyle: v => fishingValueStyle = v,
                    span: fishingBarSpan, setSpan: v => fishingBarSpan = v,
                    type: BarType.Fishing) }
            };
            y = DoBarSettingsRows(x, y, width, zoneBarOrder, zoneBars, layoutMode != LayoutMode.Single);

            // 保留目标比例阈值标记（钓鱼区）：鱼条上按 Zone_Fishing.targetPopulationPct 画阈值标记。
            if (enableFishingBar)
            {
                y += ItemGap;
                Rect fishingPctRow = new Rect(x, y, width, RowHeight);
                Widgets.CheckboxLabeled(fishingPctRow, "BetterInspectPane.ShowFishingTargetPctMarker".Translate(), ref showFishingTargetPctMarker);
                TooltipHandler.TipRegion(fishingPctRow, "BetterInspectPane.ShowFishingTargetPctMarker_Tip".Translate());
                y += RowGap;
            }
        }

        EndPageScroll(SettingsPage.Bars, y);
    }

    // ----- 第 3 页：多选检查面板 -----
    // 多选网格设置（开关 / 单元大小 / 行数）与多选检查面板尺寸设置（覆盖最小宽/高、自适应高度）。
    private void DoMultiSelectPage(Rect inRect)
    {
        BeginPageScroll(inRect, SettingsPage.MultiSelect, out float x, out float y, out float width);

        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableMultiSelectGrid".Translate(), ref enableMultiSelectGrid);
        y += RowGap;

        if (enableMultiSelectGrid)
        {
            // 子开关：多选网格是否也作用于区域 / 世界地图对象（分层，需主开关开启）。
            Rect zonesRow = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(zonesRow, "BetterInspectPane.EnableMultiSelectGridZones".Translate(), ref enableMultiSelectGridZones);
            TooltipHandler.TipRegion(zonesRow, "BetterInspectPane.EnableMultiSelectGridZones_Tip".Translate());
            y += RowGap;

            Rect worldRow = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(worldRow, "BetterInspectPane.EnableMultiSelectGridWorldObjects".Translate(), ref enableMultiSelectGridWorldObjects);
            TooltipHandler.TipRegion(worldRow, "BetterInspectPane.EnableMultiSelectGridWorldObjects_Tip".Translate());
            y += RowGap;

            float prevCellSize = multiSelectGridCellSize;
            multiSelectGridCellSize = HorizontalSlider(new Rect(x, y, width, SliderHeight), multiSelectGridCellSize, 24f, 72f,
                label: "BetterInspectPane.MultiSelectCellSize".Translate(Mathf.RoundToInt(multiSelectGridCellSize)), roundTo: 1f);
            if (multiSelectGridCellSize != prevCellSize)
            {
                InspectPanePatch.RebakeMultiSelectBorderTexture(); // 单元尺寸变化 → 重新烘焙边框纹理。
            }
            y += SliderGap;

            // 单元间距 / 边框宽度：并排显示在同一行的左右两列。
            {
                float colW = (width - ItemGap) / 2f;
                multiSelectCellGap = HorizontalSlider(new Rect(x, y, colW, SliderHeight), multiSelectCellGap, 0f, 10f,
                    label: "BetterInspectPane.MultiSelectCellGap".Translate(Mathf.RoundToInt(multiSelectCellGap)), roundTo: 1f);

                int prevBorderWidth = multiSelectCellBorderWidth;
                multiSelectCellBorderWidth = Mathf.RoundToInt(HorizontalSlider(new Rect(x + colW + ItemGap, y, colW, SliderHeight), multiSelectCellBorderWidth, 1f, 4f,
                    label: "BetterInspectPane.MultiSelectBorderWidth".Translate(multiSelectCellBorderWidth), roundTo: 1f));
                if (multiSelectCellBorderWidth != prevBorderWidth)
                {
                    InspectPanePatch.RebakeMultiSelectBorderTexture(); // 边框宽度变化 → 重新烘焙边框纹理。
                }
                y += SliderGap;
            }

            multiSelectBadgeFontSize = Mathf.RoundToInt(HorizontalSlider(new Rect(x, y, width, SliderHeight), multiSelectBadgeFontSize, 6f, 16f,
                label: "BetterInspectPane.MultiSelectBadgeFontSize".Translate(multiSelectBadgeFontSize), roundTo: 1f));
            y += SliderGap;

            multiSelectBadgeScale = HorizontalSlider(new Rect(x, y, width, SliderHeight), multiSelectBadgeScale, 0.5f, 2f,
                label: "BetterInspectPane.MultiSelectBadgeScale".Translate(multiSelectBadgeScale.ToStringPercent("F0")), roundTo: 0.05f);
            y += SliderGap;

            multiSelectGridRows = Mathf.RoundToInt(HorizontalSlider(new Rect(x, y, width, SliderHeight), multiSelectGridRows, 1f, 10f,
                label: "BetterInspectPane.MultiSelectRows".Translate(multiSelectGridRows), roundTo: 1f));
            y += SliderGap;

            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.MultiSelectCountAbbreviation".Translate(), ref multiSelectCountAbbreviation);
            y += RowGap;

            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.MultiSelectBadgeShowX".Translate(), ref multiSelectBadgeShowX);
            y += RowGap;

            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.MultiSelectGridBackgroundExact".Translate(), ref multiSelectGridBackgroundExact);
            y += RowGap;

            // 输入键互换：默认左键多选 / Shift 单选、右键多重取消 / Ctrl 取消；
            // 开启后恢复旧行为（Shift 多选、Ctrl 多重取消）。拖拽跟随同一映射。
            Rect shiftKeyRow = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(shiftKeyRow, "BetterInspectPane.MultiSelectShiftDrag".Translate(), ref useShiftKeyForMultiSelect);
            TooltipHandler.TipRegion(shiftKeyRow, "BetterInspectPane.MultiSelectShiftDrag_Tip".Translate());
            y += RowGap;

            Rect ctrlKeyRow = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(ctrlKeyRow, "BetterInspectPane.MultiSelectCtrlDrag".Translate(), ref useCtrlKeyForMultiCancel);
            TooltipHandler.TipRegion(ctrlKeyRow, "BetterInspectPane.MultiSelectCtrlDrag_Tip".Translate());
            y += RowGap;

            // 单组展开（Pawn / 非 Pawn Thing / 非 Thing 独立开关，默认 Pawn 开、非 Pawn Thing 关、非 Thing 开）：
            // 某类别恰好只构成一个组时逐项平铺。复选框标签保持简短，详细说明放在 tooltip（悬停显示）。
            Rect expandPawnRow = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(expandPawnRow, "BetterInspectPane.MultiSelectExpandSingleGroupPawn".Translate(), ref expandSingleGroupPawn);
            TooltipHandler.TipRegion(expandPawnRow, "BetterInspectPane.MultiSelectExpandSingleGroupPawn_Tip".Translate());
            y += RowGap;

            Rect expandThingRow = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(expandThingRow, "BetterInspectPane.MultiSelectExpandSingleGroupNonPawn".Translate(), ref expandSingleGroupNonPawn);
            TooltipHandler.TipRegion(expandThingRow, "BetterInspectPane.MultiSelectExpandSingleGroupNonPawn_Tip".Translate());
            y += RowGap;

            Rect expandNonThingRow = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(expandNonThingRow, "BetterInspectPane.MultiSelectExpandSingleGroupNonThing".Translate(), ref expandSingleGroupNonThing);
            TooltipHandler.TipRegion(expandNonThingRow, "BetterInspectPane.MultiSelectExpandSingleGroupNonThing_Tip".Translate());
            y += RowGap;

            // 多选网格健康条：总开关 + 高度 + 满血隐藏（Pawn / 非 Pawn 各自独立）。
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                "BetterInspectPane.EnableMultiSelectHealthBar".Translate(), ref enableMultiSelectHealthBar);
            y += RowGap;

            if (enableMultiSelectHealthBar)
            {
                multiSelectHealthBarHeight = HorizontalSlider(new Rect(x, y, width, SliderHeight),
                    multiSelectHealthBarHeight, 2f, 10f,
                    label: "BetterInspectPane.MultiSelectHealthBarHeight".Translate(Mathf.RoundToInt(multiSelectHealthBarHeight)), roundTo: 1f);
                y += SliderGap;

                Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                    "BetterInspectPane.MultiSelectHideFullPawnHealthBar".Translate(), ref hideMultiSelectFullHealthBarPawn);
                y += RowGap;

                Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                    "BetterInspectPane.MultiSelectHideFullNonPawnHealthBar".Translate(), ref hideMultiSelectFullHealthBarNonPawn);
                y += RowGap;

                // 使用健康背景替代健康条（背景是健康条的替代方案，需总开关开启才有意义）。
                Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                    "BetterInspectPane.UseHealthBackgroundInsteadOfBar".Translate(), ref useHealthBackgroundInsteadOfBar);
                y += RowGap;

                if (useHealthBackgroundInsteadOfBar)
                {
                    // 反转健康背景比例：按受伤比例（1-健康）从上端填充。
                    Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight),
                        "BetterInspectPane.InvertHealthBackground".Translate(), ref invertHealthBackground);
                    y += RowGap;
                }
            }

            // 额外选中指示（悬停 / Shift / Ctrl 的当前项在地图上以原版指示箭头标出，详情见 tooltip）。
            Rect mapIndicatorRow = new Rect(x, y, width, RowHeight);
            Widgets.CheckboxLabeled(mapIndicatorRow,
                "BetterInspectPane.MultiSelectMapIndicator".Translate(), ref enableMultiSelectMapIndicator);
            TooltipHandler.TipRegion(mapIndicatorRow, "BetterInspectPane.MultiSelectMapIndicator_Tip".Translate());
            y += RowGap;

            // 覆盖最小宽度：开启后多选网格场景下检查面板最小宽度用覆盖值。
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.MultiSelectOverrideMinWidth".Translate(), ref multiSelectOverrideMinWidth);
            y += RowGap;

            if (multiSelectOverrideMinWidth)
            {
                multiSelectMinWidth = HorizontalSlider(new Rect(x, y, width, SliderHeight), multiSelectMinWidth, 432f, 800f,
                    label: "BetterInspectPane.MultiSelectMinWidth".Translate(Mathf.RoundToInt(multiSelectMinWidth)), roundTo: 1f);
                y += SliderGap;
            }

            // 覆盖最小高度：开启后多选网格场景下检查面板最小高度用覆盖值。
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.MultiSelectOverrideMinHeight".Translate(), ref multiSelectOverrideMinHeight);
            y += RowGap;

            if (multiSelectOverrideMinHeight)
            {
                multiSelectMinHeight = HorizontalSlider(new Rect(x, y, width, SliderHeight), multiSelectMinHeight, 165f, 600f,
                    label: "BetterInspectPane.MultiSelectMinHeight".Translate(Mathf.RoundToInt(multiSelectMinHeight)), roundTo: 1f);
                y += SliderGap;
            }

            // 自适应高度：配置的最小高度放不下配置行数的网格时，自动把最小高度提升到能容纳所需的高度。
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.MultiSelectAdaptiveHeight".Translate(), ref multiSelectAdaptiveHeight);
            y += RowGap;
        }

        EndPageScroll(SettingsPage.MultiSelect, y);
    }

    // ----- 第 4 页：颜色 -----
    private void DoColorsPage(Rect inRect)
    {
        BeginPageScroll(inRect, SettingsPage.Colors, out float x, out float y, out float width);

        for (int i = 0; i < AllColorSettings.Length; i++)
        {
            ColorSetting cs = AllColorSettings[i];
            if (cs.shown != null && !cs.shown())
            {
                continue;
            }
            y = DoColorRow(x, y, width, cs.get, cs.set, cs.labelKey.Translate());
        }

        y += ItemGap;
        if (Widgets.ButtonText(new Rect(x, y, width, 32f), "BetterInspectPane.ResetColors".Translate()))
        {
            ResetColorsToDefault();
        }
        y += 32f + ItemGap;

        EndPageScroll(SettingsPage.Colors, y);
    }

    // ----- 第 5 页：效果与杂项 -----
    private void DoEffectsPage(Rect inRect)
    {
        BeginPageScroll(inRect, SettingsPage.Effects, out float x, out float y, out float width);

        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableEaseEffect".Translate(), ref enableEaseEffect);
        y += RowGap;

        if (enableEaseEffect)
        {
            easeSpeed = HorizontalSlider(new Rect(x, y, width, 24f), easeSpeed, 0.01f, 1f,
                label: "BetterInspectPane.EaseSpeed".Translate(easeSpeed.ToStringPercent()), roundTo: 0.01f);
            y += SliderGap;

            Widgets.Label(new Rect(x, y, width, RowHeight), "BetterInspectPane.EasingStyle".Translate());
            y += RowGap;

            foreach (EasingStyle style in Enum.GetValues(typeof(EasingStyle)))
            {
                if (Widgets.RadioButtonLabeled(new Rect(x + 12f, y, width - 12f, RowHeight),
                    ("BetterInspectPane.EasingStyle_" + style).Translate(), easingStyle == style))
                {
                    easingStyle = style;
                }
                y += RowGap;
            }
            y += ItemGap;
        }

        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableExplosiveThreshold".Translate(), ref enableExplosiveThreshold);
        y += RowGap;

        if (enableExplosiveThreshold)
        {
            Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.ThinExplosiveMarker".Translate(), ref thinExplosiveMarker);
            y += RowGap;
        }

        // 额外检查文字：追加到检查面板信息文字末尾。
        // 普通对象总开关：开启后按下方细分开关决定显示哪些项；世界地图空地砖单独开关。
        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableExtraNormalObjectText".Translate(), ref enableExtraNormalObjectText);
        y += RowGap;

        if (enableExtraNormalObjectText)
        {
            // 普通对象细分开关（一级缩进）。
            float indent = 20f;

            // 所属势力（二级缩进：不显示玩家所属势力 / 不显示无所属势力）。
            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraFactionText".Translate(), ref enableExtraFactionText);
            y += RowGap;
            if (enableExtraFactionText)
            {
                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.HideFactionPlayerText".Translate(), ref hideFactionPlayerText);
                y += RowGap;
                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.HideFactionNoneText".Translate(), ref hideFactionNoneText);
                y += RowGap;
            }

            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraMarketValueText".Translate(), ref enableExtraMarketValueText);
            y += RowGap;

            // 武器：总开关，武器相关设置统一归入其下（二级缩进）。
            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraWeaponText".Translate(), ref enableExtraWeaponText);
            y += RowGap;
            if (enableExtraWeaponText)
            {
                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.EnableExtraWeaponDamageText".Translate(), ref enableExtraWeaponDamageText);
                y += RowGap;

                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.EnableExtraWeaponRangeText".Translate(), ref enableExtraWeaponRangeText);
                y += RowGap;

                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.EnableExtraWeaponCooldownText".Translate(), ref enableExtraWeaponCooldownText);
                y += RowGap;

                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.EnableExtraWeaponDpsText".Translate(), ref enableExtraWeaponDpsText);
                y += RowGap;

                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.EnableExtraWeaponArmorPenText".Translate(), ref enableExtraWeaponArmorPenText);
                y += RowGap;
            }

            // 衣物：总开关，护甲相关设置统一归入其下（二级缩进）。
            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraApparelText".Translate(), ref enableExtraApparelText);
            y += RowGap;
            if (enableExtraApparelText)
            {
                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.EnableExtraApparelLayerText".Translate(), ref enableExtraApparelLayerText);
                y += RowGap;

                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.EnableExtraApparelSharpArmorText".Translate(), ref enableExtraApparelSharpArmorText);
                y += RowGap;

                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.EnableExtraApparelBluntArmorText".Translate(), ref enableExtraApparelBluntArmorText);
                y += RowGap;

                Widgets.CheckboxLabeled(new Rect(x + indent * 2, y, width - indent * 2, RowHeight), "BetterInspectPane.EnableExtraApparelHeatArmorText".Translate(), ref enableExtraApparelHeatArmorText);
                y += RowGap;
            }

            // 食物：营养值。
            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraNutritionText".Translate(), ref enableExtraNutritionText);
            y += RowGap;

            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraBeautyText".Translate(), ref enableExtraBeautyText);
            y += RowGap;

            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraComfortText".Translate(), ref enableExtraComfortText);
            y += RowGap;

            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraCeHeightText".Translate(), ref enableExtraCeHeightText);
            y += RowGap;
        }

        // Pawn 额外信息文字：总开关，下辖 特性 / CE 高度 / 对 Pawn 加入收藏统计。
        // 与普通对象额外信息互相独立；Pawn 额外信息文字一律置于原版文字上方。
        bool icpActive = InfoCardPlusReflection.IsActive;
        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableExtraPawnText".Translate(), ref enableExtraPawnText);
        y += RowGap;

        if (enableExtraPawnText)
        {
            float indent = 20f;

            // 特性：列出该 Pawn 的全部特性。
            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraPawnTraitText".Translate(), ref enableExtraPawnTraitText);
            y += RowGap;

            // Pawn 的 CE 高度（Combat Extended 的竖直碰撞高度）。
            Widgets.CheckboxLabeled(new Rect(x + indent, y, width - indent, RowHeight), "BetterInspectPane.EnableExtraCeHeightPawnText".Translate(), ref enableExtraCeHeightPawnText);
            y += RowGap;

            // 对 Pawn 加入收藏统计：需要安装并激活 Info Card Plus（未激活时置灰禁用）。
            Rect pawnPinsRow = new Rect(x + indent, y, width - indent, RowHeight);
            if (!icpActive)
            {
                GUI.color = Color.gray;
            }
            Widgets.CheckboxLabeled(pawnPinsRow, "BetterInspectPane.EnableInfoCardPlusPinsPawn".Translate(), ref enableInfoCardPlusPinsPawn, disabled: !icpActive);
            if (!icpActive)
            {
                GUI.color = Color.white;
            }
            TooltipHandler.TipRegion(pawnPinsRow, "BetterInspectPane.EnableInfoCardPlusPinsPawn_Tip".Translate());
            y += RowGap;
        }

        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableExtraMapTileText".Translate(), ref enableExtraMapTileText);
        y += RowGap;

        // 世界地图对象：自然阵营关系（NaturalGoodwill）。
        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableExtraNaturalGoodwillText".Translate(), ref enableExtraNaturalGoodwillText);
        y += RowGap;

        // 世界地图对象：派系定居点的派系类别（factionDef.label，置于原版文字之前）。
        Widgets.CheckboxLabeled(new Rect(x, y, width, RowHeight), "BetterInspectPane.EnableExtraFactionDefLabelText".Translate(), ref enableExtraFactionDefLabelText);
        y += RowGap;

        // Info Card Plus 收藏 Stat 兼容（Thing；Pawn 的收藏统计开关见上方「Pawn 额外信息文字」分组；
        // 需要安装 Info Card Plus 才生效）。未安装/未激活时整组置灰禁用（勾选框与子项均不可操作）。
        Rect icpRow = new Rect(x, y, width, RowHeight);
        if (!icpActive)
        {
            GUI.color = Color.gray;
        }
        Widgets.CheckboxLabeled(icpRow, "BetterInspectPane.EnableInfoCardPlusPins".Translate(), ref enableInfoCardPlusPins, disabled: !icpActive);
        if (!icpActive)
        {
            GUI.color = Color.white;
        }
        TooltipHandler.TipRegion(icpRow, "BetterInspectPane.EnableInfoCardPlusPins_Tip".Translate());
        y += RowGap;

        if (enableInfoCardPlusPins)
        {
            float icpIndent = 20f;

            if (!icpActive)
            {
                GUI.color = Color.gray;
            }
            Widgets.CheckboxLabeled(new Rect(x + icpIndent, y, width - icpIndent, RowHeight), "BetterInspectPane.EnableInfoCardPlusPinsHighlight".Translate(), ref enableInfoCardPlusPinsHighlight, disabled: !icpActive);
            if (!icpActive)
            {
                GUI.color = Color.white;
            }
            y += RowGap;

            float icpPosLabelW = 120f;
            if (!icpActive)
            {
                GUI.color = Color.gray;
            }
            Widgets.Label(new Rect(x + icpIndent, y, icpPosLabelW, RowHeight), "BetterInspectPane.InfoCardPlusPinnedPosition".Translate());
            if (!icpActive)
            {
                GUI.color = Color.white;
            }
            DoEnumDropdown(x + icpIndent + icpPosLabelW + 4f, y - dropdownNudgeY,
                width - icpIndent - icpPosLabelW - 4f, RowHeight,
                "BetterInspectPane.EnableInfoCardPlusPins_Tip".Translate(), icpActive,
                infoCardPlusPinnedPosition, v => infoCardPlusPinnedPosition = v,
                v => ("BetterInspectPane.InfoCardPlusPinnedPosition_" + v).Translate());
            y += RowGap;
        }

        // ----- 杂项：选择行为 -----
        y += ItemGap;

        // 选择数量限制修改：开启后显示滑条，用滑条修改原版 Selector 的单次选择数量上限（默认 200）。
        Rect limitRow = new Rect(x, y, width, RowHeight);
        Widgets.CheckboxLabeled(limitRow, "BetterInspectPane.EnableSelectionLimitModify".Translate(), ref enableSelectionLimitModify);
        TooltipHandler.TipRegion(limitRow, "BetterInspectPane.EnableSelectionLimitModify_Tip".Translate());
        y += RowGap;

        if (enableSelectionLimitModify)
        {
            selectionLimit = Mathf.RoundToInt(HorizontalSlider(new Rect(x, y, width, SliderHeight), selectionLimit, 1f, 1000f,
                label: "BetterInspectPane.SelectionLimit".Translate(selectionLimit), roundTo: 1f));
            y += SliderGap;
        }

        // 改进的原版 Shift 选择（悬停查看详细说明）。
        Rect shiftRow = new Rect(x, y, width, RowHeight);
        Widgets.CheckboxLabeled(shiftRow, "BetterInspectPane.EnableImprovedShiftSelection".Translate(), ref enableImprovedShiftSelection);
        TooltipHandler.TipRegion(shiftRow, "BetterInspectPane.EnableImprovedShiftSelection_Tip".Translate());
        y += RowGap;

        // Ctrl 选择功能（悬停查看详细说明）。
        Rect ctrlRow = new Rect(x, y, width, RowHeight);
        Widgets.CheckboxLabeled(ctrlRow, "BetterInspectPane.EnableCtrlSelection".Translate(), ref enableCtrlSelection);
        TooltipHandler.TipRegion(ctrlRow, "BetterInspectPane.EnableCtrlSelection_Tip".Translate());
        y += RowGap;

        // 记忆已打开观察面板标签页（悬停查看详细说明）。
        Rect rememberRow = new Rect(x, y, width, RowHeight);
        Widgets.CheckboxLabeled(rememberRow, "BetterInspectPane.EnableRememberOpenTab".Translate(), ref rememberOpenTab);
        TooltipHandler.TipRegion(rememberRow, "BetterInspectPane.EnableRememberOpenTab_Tip".Translate());
        y += RowGap;

        EndPageScroll(SettingsPage.Effects, y);
    }



    // ==================== 设置面板辅助类型与绘制 ====================

    /// <summary>一条进度条设置的快照（含读写委托），按所属顺序列表的当前顺序显示并支持 ▲▼ 交换。</summary>
    /// <remarks>「是否显示」与「高度偏移 + 字体缩放」一并由 DoBarRow 渲染：setEnabled 为 null 表示该条不可关闭（如健康条始终显示）。
    /// 所有条共用同一组标签名称（BarHeightOffset / BarFontScale）与同一套高度机制（实际高度 = 条默认高度 + 高度偏移）。</remarks>
    private readonly struct BarSetting
    {
        public readonly string name;

        // 是否显示开关：setEnabled 为 null 时置灰勾选、不可改（健康条始终显示）。
        public readonly bool enabled;
        public readonly Action<bool>? setEnabled;
        public readonly string? enableKey; // 显示开关的工具提示翻译键（可选）。

        // 本条高度偏移（相对条默认高度，-6..+14）：滑杆写入 setHeightOffset。
        public readonly float heightOffset;
        public readonly Action<float> setHeightOffset;
        public readonly float minHeightOffset;
        public readonly float maxHeightOffset;
        // 本条字体缩放（标签与数值相对全局字号的缩放比例，50%..200%）。
        public readonly float fontScale;
        public readonly Action<float> setFontScale;

        public readonly ValueDisplayStyle valueStyle;
        public readonly Action<ValueDisplayStyle> setValueStyle;
        public readonly BarSpan span;
        public readonly Action<BarSpan> setSpan;
        public readonly BarType type; // 条标识，用于从顺序列表映射到本条。

        // 0% / 100% 时自动隐藏开关：仅支持该行为的条非 null（护盾/剩余时间为 0%，冷却/充能/工作/研究为 100%）。
        public readonly bool? autoHide;
        public readonly Action<bool>? setAutoHide;
        public readonly string? autoHideExtreme; // 极值文本（"0%" / "100%"），用于标签与提示。
        public readonly string? autoHideTip;      // 自动隐藏复选框的提示翻译键（可选）。

        public BarSetting(string name,
            bool enabled, Action<bool>? setEnabled, string? enableKey,
            float heightOffset, Action<float> setHeightOffset, float minHeightOffset, float maxHeightOffset,
            float fontScale, Action<float> setFontScale,
            ValueDisplayStyle valueStyle, Action<ValueDisplayStyle> setValueStyle,
            BarSpan span, Action<BarSpan> setSpan,
            BarType type,
            bool? autoHide = null, Action<bool>? setAutoHide = null,
            string? autoHideExtreme = null, string? autoHideTip = null)
        {
            this.name = name;
            this.enabled = enabled;
            this.setEnabled = setEnabled;
            this.enableKey = enableKey;
            this.heightOffset = heightOffset;
            this.setHeightOffset = setHeightOffset;
            this.minHeightOffset = minHeightOffset;
            this.maxHeightOffset = maxHeightOffset;
            this.fontScale = fontScale;
            this.setFontScale = setFontScale;
            this.valueStyle = valueStyle;
            this.setValueStyle = setValueStyle;
            this.span = span;
            this.setSpan = setSpan;
            this.type = type;
            this.autoHide = autoHide;
            this.setAutoHide = setAutoHide;
            this.autoHideExtreme = autoHideExtreme;
            this.autoHideTip = autoHideTip;
        }
    }

    /// <summary>
    /// 布局设置行（同一行）：布局下拉 + 列间距滑杆（非单列时）+ 双条行均分开关（三列/四列时）。
    /// 行高取滑杆块高度（SliderHeight，滑杆标签画在轨道上方），下拉/复选框在该行内垂直居中。
    /// 主面板与 Pawn 面板复用（读写的设置字段由调用方经委托传入）。返回新的 y。
    /// </summary>
    private static float DoLayoutRow(float x, float y, float width,
        LayoutMode mode, Action<LayoutMode> setMode,
        float gap, Action<float> setGap,
        bool evenSplit, Action<bool> setEvenSplit)
    {
        string layoutLabel = "BetterInspectPane.Layout".Translate();
        float layoutLabelW = Text.CalcSize(layoutLabel).x;
        float itemY = y + (SliderHeight - RowHeight) * 0.5f;

        Widgets.Label(new Rect(x, itemY, layoutLabelW, RowHeight), layoutLabel);
        float cx = x + layoutLabelW + 4f;
        DoEnumDropdown(cx, itemY - dropdownNudgeY, 120f, RowHeight,
            "BetterInspectPane.Layout".Translate(),
            enabled: true,
            mode,
            setMode,
            v => ("BetterInspectPane.Layout_" + v).Translate());
        cx += 120f + 14f;

        if (mode != LayoutMode.Single)
        {
            // 列间距滑杆（标签画在轨道上方）。
            float gapW = 170f;
            setGap(HorizontalSlider(new Rect(cx, y, gapW, SliderHeight), gap, 0f, 12f,
                label: "BetterInspectPane.ColumnGap".Translate(Mathf.RoundToInt(gap)), roundTo: 1f));
            cx += gapW + 14f;
        }

        // 双条行均分开关：三列/四列布局下显示（两列布局本身即两列平分，无需该开关）。
        if (mode == LayoutMode.Three || mode == LayoutMode.Four)
        {
            Rect evenSplitRect = new Rect(cx, itemY, width - cx, RowHeight);
            Widgets.CheckboxLabeled(evenSplitRect, "BetterInspectPane.EvenSplitTwoBarRows".Translate(), ref evenSplit);
            TooltipHandler.TipRegion(evenSplitRect, "BetterInspectPane.EvenSplitTwoBarRowsTip".Translate());
            setEvenSplit(evenSplit);
        }

        return y + SliderGap;
    }

    /// <summary>
    /// 按顺序列表渲染一组进度条设置：顺序列表下标即显示顺序，▲▼ 直接交换列表元素。
    /// </summary>
    private static float DoBarSettingsRows(float x, float y, float width, List<BarType> orderList,
        Dictionary<BarType, BarSetting> allBars, bool spanEnabled)
    {
        for (int i = 0; i < orderList.Count; i++)
        {
            y = DoBarRow(x, y, width, allBars[orderList[i]], spanEnabled, orderList, i);
        }
        return y;
    }

    /// <summary>
    /// 一条进度条的紧凑设置区块：
    /// 第一行「显示开关（复选框）+ 条名 + 数值显示下拉 + 占位下拉（两列关闭时置灰禁用）
    /// + ▲▼ 上移/下移调整顺序 + 自动隐藏开关（放在顺序调整右侧，仅支持该行为的条）」，
    /// 启用时在下方追加「高度滑杆」。
    /// orderList 为本条所属组的顺序列表，index 为本条在其中的下标（▲▼ 直接交换列表元素；
    /// 首/尾对应的方向禁用）。返回新的 y。
    /// </summary>
    private static float DoBarRow(float x, float y, float width, BarSetting bar, bool spanEnabled,
        List<BarType> orderList, int index)
    {
        float gap = 8f;
        float cx = x;


        // 显示开关：裸复选框（勾选 = 显示本条）。健康条始终显示（setEnabled 为 null，置灰不可改）。
        float checkSize = 24f;
        Rect checkRect = new Rect(cx, y + (RowHeight - checkSize) * 0.5f, checkSize, checkSize);
        bool enabled = bar.enabled;
        Widgets.Checkbox(checkRect.x, checkRect.y, ref enabled, disabled: bar.setEnabled == null);
        if (enabled != bar.enabled && bar.setEnabled != null)
        {
            bar.setEnabled(enabled);
        }
        if (bar.enableKey != null)
        {
            TooltipHandler.TipRegion(checkRect, bar.enableKey.Translate());
        }
        cx += checkSize + gap;

        // 条名。
        float nameW = 75f;
        Widgets.Label(new Rect(cx, y, nameW, RowHeight), bar.name);
        cx += nameW + gap;

        // 数值显示：下拉菜单。
        string valueStyleLabel = "BetterInspectPane.ValueStyleLabel".Translate();
        float valueStyleLabelW = Text.CalcSize(valueStyleLabel).x;
        Widgets.Label(new Rect(cx, y, valueStyleLabelW, RowHeight), valueStyleLabel);
        cx += valueStyleLabelW + 4f;

        float valueStyleBtnW = 95f;
        DoEnumDropdown(cx, y - dropdownNudgeY, valueStyleBtnW, RowHeight,
            "BetterInspectPane.ValueStyleLabel".Translate(),
            enabled: true,
            bar.valueStyle,
            bar.setValueStyle,
            v => ("BetterInspectPane.ValueStyle_" + v).Translate());
        cx += valueStyleBtnW + gap;

        // 占位：下拉菜单（两列布局关闭时置灰禁用）。
        string spanLabel = "BetterInspectPane.BarSpan".Translate();
        float spanLabelW = Text.CalcSize(spanLabel).x;
        Widgets.Label(new Rect(cx, y, spanLabelW, RowHeight), spanLabel);
        cx += spanLabelW + 4f;

        float spanBtnW = 105f;
        DoEnumDropdown(cx, y - dropdownNudgeY, spanBtnW, RowHeight,
            "BetterInspectPane.BarSpan".Translate(),
            enabled: spanEnabled,
            bar.span,
            bar.setSpan,
            v => ("BetterInspectPane.BarSpan_" + v).Translate());
        cx += spanBtnW + gap;

        // 顺序：▲ 上移 / ▼ 下移（直接交换顺序列表中的相邻元素，列表下标即显示顺序）。
        string orderLabel = "BetterInspectPane.BarOrder".Translate();
        float orderLabelW = Text.CalcSize(orderLabel).x;
        Widgets.Label(new Rect(cx, y, orderLabelW, RowHeight), orderLabel);
        cx += orderLabelW + 4f;

        float arrowW = 26f;
        Rect upRect = new Rect(cx, y - dropdownNudgeY, arrowW, RowHeight);
        if (index > 0)
        {
            if (Widgets.ButtonText(upRect, "▲"))
            {
                BarType tmp = orderList[index];
                orderList[index] = orderList[index - 1];
                orderList[index - 1] = tmp;
            }
        }
        else
        {
            DrawDisabledText(upRect, "▲");
        }
        TooltipHandler.TipRegion(upRect, "BetterInspectPane.MoveUp".Translate());
        cx += arrowW + 2f;

        Rect downRect = new Rect(cx, y - dropdownNudgeY, arrowW, RowHeight);
        if (index < orderList.Count - 1)
        {
            if (Widgets.ButtonText(downRect, "▼"))
            {
                BarType tmp = orderList[index];
                orderList[index] = orderList[index + 1];
                orderList[index + 1] = tmp;
            }
        }
        else
        {
            DrawDisabledText(downRect, "▼");
        }
        TooltipHandler.TipRegion(downRect, "BetterInspectPane.MoveDown".Translate());
        cx += arrowW;

        // 白色字体开关：放在顺序调整（▲▼）右侧。开启后该条的标签与数值无视进度条比例，总是以白色显示。
        if (enabled)
        {
            cx += gap;
            string whiteFontLabel = "BetterInspectPane.WhiteFont".Translate();
            float whiteFontW = Text.CalcSize(whiteFontLabel).x + 26f;
            Rect whiteFontRect = new Rect(cx, y - dropdownNudgeY, whiteFontW, RowHeight);
            bool whiteFont = WhiteFontFor(bar.type);
            Widgets.CheckboxLabeled(whiteFontRect, whiteFontLabel, ref whiteFont);
            if (whiteFont != WhiteFontFor(bar.type))
            {
                if (whiteFont)
                {
                    barWhiteFont.Add(bar.type);
                }
                else
                {
                    barWhiteFont.Remove(bar.type);
                }
            }
            TooltipHandler.TipRegion(whiteFontRect, "BetterInspectPane.WhiteFontTip".Translate());
            cx += whiteFontW;
        }

        // 0% / 100% 时自动隐藏开关：放在顺序调整（▲▼）右侧，使设置更紧凑。
        // 仅支持该行为的条显示（如护盾/剩余时间 0%，冷却/充能/工作/研究 100%）。
        if (enabled && bar.autoHide.HasValue && bar.setAutoHide != null)
        {
            cx += gap;
            string autoHideLabel = "BetterInspectPane.AutoHideBar".Translate(bar.autoHideExtreme ?? "0%");
            float autoHideW = Text.CalcSize(autoHideLabel).x + 26f;
            Rect autoHideRect = new Rect(cx, y - dropdownNudgeY, autoHideW, RowHeight);
            bool autoHide = bar.autoHide.Value;
            Widgets.CheckboxLabeled(autoHideRect, autoHideLabel, ref autoHide);
            if (autoHide != bar.autoHide.Value)
            {
                bar.setAutoHide(autoHide);
            }
            if (bar.autoHideTip != null)
            {
                TooltipHandler.TipRegion(autoHideRect, bar.autoHideTip.Translate(bar.autoHideExtreme ?? "0%"));
            }

            cx += autoHideW;
        }
        // 下一行
        cx = x;
        // 高度偏移 + 字体缩放：同一行并排两个滑杆（所有条共用同一组标签名称与机制），
        // 仅在该条启用时显示（健康条始终启用，故始终显示）。
        if (enabled)
        {
            y += RowGap;
            float sliderGap = 14f;
            float halfW = (width - sliderGap) * 0.5f;
            bar.setHeightOffset(HorizontalSlider(new Rect(cx, y, halfW, 24f), bar.heightOffset, bar.minHeightOffset, bar.maxHeightOffset,
                label: "BetterInspectPane.BarHeightOffset".Translate(FormatHeightOffset(bar.heightOffset)), roundTo: 1f));
            bar.setFontScale(HorizontalSlider(new Rect(cx + halfW + sliderGap, y, halfW, 24f), bar.fontScale, 0.5f, 2f,
                label: "BetterInspectPane.BarFontScale".Translate(bar.fontScale.ToStringPercent("F0")), roundTo: 0.05f));
            y += SliderGap;
        }
        else
        {
            y += RowGap;
        }

        return y;
    }

    /// <summary>高度偏移的标签文本：带正负号（如 "+2" / "0" / "-3"）。</summary>
    private static string FormatHeightOffset(float offset)
    {
        int rounded = Mathf.RoundToInt(offset);
        return rounded > 0 ? "+" + rounded : rounded.ToString();
    }

    /// <summary>置灰居中渲染文本：用于顺序列表首/尾的禁用箭头、被禁用的下拉按钮文本等。</summary>
    private static void DrawDisabledText(Rect rect, string text)
    {
        GUI.color = Color.gray;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, text);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }

    /// <summary>
    /// 枚举下拉按钮：显示当前值，点击打开 FloatMenu 供选择。
    /// enabled 为 false 时置灰禁用（不响应点击）。
    /// </summary>
    private static void DoEnumDropdown<T>(float x, float y, float width, float height,
        string tooltip, bool enabled, T current, Action<T> setter, Func<T, string> labelOf)
        where T : Enum
    {
        Rect rect = new Rect(x, y, width, height);
        string text = labelOf(current);

        if (!enabled)
        {
            var tr = rect;
            tr.y += 1f;
            DrawDisabledText(tr, text);
            TooltipHandler.TipRegion(rect, tooltip);
            return;
        }

        if (Widgets.ButtonText(rect, text))
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (T v in Enum.GetValues(typeof(T)))
            {
                T captured = v;
                options.Add(new FloatMenuOption(labelOf(captured), () => setter(captured)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        TooltipHandler.TipRegion(rect, tooltip);
    }

    /// <summary>一条颜色设置行：标签 + 色块 + 更改按钮 + 透明度滑块。返回新的 y。</summary>
    private float DoColorRow(float x, float y, float width, Func<Color> getColor, Action<Color> setColor, string label)
    {
        Color color = getColor();

        // 第一行：标签 + 色块 + 更改按钮（打开完整取色器）。
        float row1H = 28f;
        float labelW = 200f;
        float swatchSize = row1H - 4f;
        float gap = 6f;

        Widgets.Label(new Rect(x, y, labelW, row1H), label);

        Rect swatch = new Rect(x + labelW + gap, y + (row1H - swatchSize) / 2f, swatchSize, swatchSize);
        Widgets.DrawBoxSolid(swatch, color);
        Widgets.DrawBox(swatch, 1);

        float buttonX = swatch.xMax + gap;
        Rect btnRect = new Rect(buttonX, y, width - buttonX, row1H);
        if (Widgets.ButtonText(btnRect, "BetterInspectPane.ChangeColor".Translate()))
        {
            Find.WindowStack.Add(new Dialog_MyModColorPicker(color, setColor));
        }
        y += row1H + ItemGap;

        // 第二行：透明度滑块。
        float row2H = 24f;
        float alphaLabelW = Text.CalcSize("BetterInspectPane.AlphaLabel".Translate()).x;
        float pctW = 46f;
        float sliderW = width - alphaLabelW - 4f - pctW;

        Widgets.Label(new Rect(x, y, alphaLabelW, row2H), "BetterInspectPane.AlphaLabel".Translate());

        float newAlpha = HorizontalSlider(
            new Rect(x + alphaLabelW + 4f, y, sliderW, row2H),
            color.a, 0f, 1f, middleAlignment: true, roundTo: 0.01f);

        Widgets.Label(new Rect(x + alphaLabelW + 4f + sliderW, y, pctW, row2H), newAlpha.ToStringPercent("F0"));

        if (Mathf.Abs(newAlpha - color.a) > 0.001f)
        {
            setColor(new Color(color.r, color.g, color.b, newAlpha));
        }

        return y + row2H + ItemGap;
    }

    // ==================== 滑杆封装（修复原版命中区偏下） ====================

    /// <summary>与原版 Widgets.HorizontalSlider 绘制布局一致，但命中区只取可见轨道/把手所在的小矩形，
    /// 不再整体下移、也不再向下溢出到下一行。
    /// 原版在传入 label 时会把矩形下移 round((h-10)/2)+5 像素，并把整块 h 高的矩形当作命中区，
    /// 导致交互区域偏下、比可见滑块大一圈。</summary>
    private static float HorizontalSlider(Rect rect, float value, float min, float max,
        bool middleAlignment = false, string? label = null, string? leftAlignedLabel = null,
        string? rightAlignedLabel = null, float roundTo = -1f)
    {
        // 与原版相同：有标签（或 middleAlignment）时先整体下移，给上方标签留出空间。
        Rect drawRect = rect;
        if (middleAlignment || !string.IsNullOrEmpty(label))
        {
            drawRect.y += Mathf.Round((drawRect.height - 10f) / 2f);
        }
        if (!string.IsNullOrEmpty(label))
        {
            drawRect.y += 5f;
        }
        // 标签画在原版相同的位置（轨道上方）。
        if (!string.IsNullOrEmpty(label) || !string.IsNullOrEmpty(leftAlignedLabel) || !string.IsNullOrEmpty(rightAlignedLabel))
        {
            TextAnchor anchor = Text.Anchor;
            GameFont font = Text.Font;
            Text.Font = GameFont.Small;
            float num2 = (string.IsNullOrEmpty(label) ? 18f : Text.CalcSize(label).y);
            Rect labelRect = new Rect(drawRect.x, drawRect.y - num2 + 3f, drawRect.width, drawRect.height);
            if (!string.IsNullOrEmpty(leftAlignedLabel))
            {
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(labelRect, leftAlignedLabel);
            }
            if (!string.IsNullOrEmpty(rightAlignedLabel))
            {
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(labelRect, rightAlignedLabel);
            }
            if (!string.IsNullOrEmpty(label))
            {
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(labelRect, label);
            }
            Text.Anchor = anchor;
            Text.Font = font;
        }
        // 关键修复：只把“轨道 + 把手”所在的小矩形交给原版绘制与命中。
        // 原版若直接传入整行矩形，命中区会整体下移并向下溢出到下一行。
        Rect sliderRect = new Rect(rect.x, drawRect.y, rect.width, 18f);
        return Widgets.HorizontalSlider(sliderRect, value, min, max, false, null, null, null, roundTo);
    }

    /// <summary>
    /// 指定像素字号对应的单行行高。
    /// Text.LineHeightOf 只认 GameFont 三档固定字号，这里临时把共享样式
    /// Text.fontStyles 的字号改为指定像素值、读取 GUIStyle.lineHeight 后立即还原。
    /// </summary>
    public static float LineHeightForSize(int size)
    {
        GUIStyle style = Text.fontStyles[(int)BaseFontSlot];
        int prev = style.fontSize;
        style.fontSize = size;
        float height = style.lineHeight;
        style.fontSize = prev;
        return height;
    }

    /// <summary>
    /// 规整一组显示顺序：去重、剔除未知成员，并把该组默认成员中缺失的按默认顺序补到队尾。
    /// 这样开发期新增的条（枚举里已存在、但旧档案顺序列表里没有）会自动获得一个有效位置，
    /// 不会被遗漏，也不会有重复顺序。
    /// </summary>
    private static void EnsureOrderListValid(List<BarType> orderList, BarType[] defaults)
    {
        if (orderList == null)
        {
            return;
        }
        List<BarType> result = new List<BarType>(defaults.Length);
        for (int i = 0; i < orderList.Count; i++)
        {
            BarType t = orderList[i];
            if (!result.Contains(t))
            {
                result.Add(t);
            }
        }
        for (int i = 0; i < defaults.Length; i++)
        {
            if (!result.Contains(defaults[i]))
            {
                result.Add(defaults[i]);
            }
        }
        orderList.Clear();
        orderList.AddRange(result);
    }

    /// <summary>
    /// 确保白色字体开关集合可用：旧存档的 Mod 设置里没有 barWhiteFont 节点时，
    /// Scribe_Collections.Look 会把字段置为 null（见原版该集合重载），这里兜底重建。
    /// </summary>
    private static void EnsureWhiteFontReady()
    {
        if (barWhiteFont == null)
        {
            barWhiteFont = [BarType.Freshness, BarType.Growth, BarType.Research, BarType.Pain];
        }
    }

    /// <summary>某条是否开启白色字体（集合外的条一律为默认关闭）。</summary>
    public static bool WhiteFontFor(BarType type)
    {
        EnsureWhiteFontReady();
        return barWhiteFont.Contains(type);
    }

    /// <summary>条的实际高度 = 条默认高度 + 本条高度偏移（各条设置统一按此计算）。</summary>
    public static float BarHeightFor(float heightOffset) => defaultBarHeight + heightOffset;
}

/// <summary>
/// 自定义像素字号作用域：RimWorld 的 Widgets.Label / LabelScrollable / Text.CalcHeight
/// 都基于 Text.CurFontStyle 绘制，而 CurFontStyle 取自共享样式 Text.fontStyles，只支持
/// GameFont 三档固定字号。参考 RimHUD 以像素字号为基础的做法：进入作用域时临时把共享
/// 样式字号改为指定像素值并选中基准档位，Dispose 时还原，从而支持任意具体数值。
/// </summary>
public readonly struct FontSizeScope : IDisposable
{
    private readonly int previousFontSize;

    public FontSizeScope(int size)
    {
        GUIStyle style = Text.fontStyles[(int)MyModTemplateSettings.BaseFontSlot];
        previousFontSize = style.fontSize;
        style.fontSize = Mathf.Max(1, size);
        Text.Font = MyModTemplateSettings.BaseFontSlot;
    }

    public void Dispose() => Text.fontStyles[(int)MyModTemplateSettings.BaseFontSlot].fontSize = previousFontSize;
}

/// <summary>Text.WordWrap 作用域：进入时设为指定值，Dispose 时还原；异常时也能恢复，防止文本状态泄漏。</summary>
public readonly struct WordWrapScope : IDisposable
{
    private readonly bool previousWordWrap;

    public WordWrapScope(bool wordWrap)
    {
        previousWordWrap = Text.WordWrap;
        Text.WordWrap = wordWrap;
    }

    public void Dispose() => Text.WordWrap = previousWordWrap;
}

/// <summary>Text.Anchor 作用域：进入时设为指定值，Dispose 时还原；异常时也能恢复，防止文本状态泄漏。</summary>
public readonly struct AnchorScope : IDisposable
{
    private readonly TextAnchor previousAnchor;

    public AnchorScope(TextAnchor anchor)
    {
        previousAnchor = Text.Anchor;
        Text.Anchor = anchor;
    }

    public void Dispose() => Text.Anchor = previousAnchor;
}
