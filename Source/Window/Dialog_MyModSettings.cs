using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

/// <summary>
/// 独立设置窗口：游戏中从检查面板右下角按钮打开。
/// 复用 MyModTemplateSettings.DoSettingsWindowContents 的现有设置绘制（含滚动视图），
/// 修改实时作用于 static 字段，关闭时持久化保存。
/// </summary>
public class Dialog_MyModSettings : Window
{
    // 窗口标题字号（像素）：比原版 optionalTitle 的小字号更大一些。
    private const int TitleFontSize = 22;
    // 标题区高度：容纳大号字并留出垂直边距。
    private const float TitleHeight = 36f;

    public override Vector2 InitialSize { get; } = new Vector2(800f, 600f);

    public Dialog_MyModSettings()
    {
        // 标题改为在内容区顶部用大字号自行绘制（原版 optionalTitle 字号固定且偏小）。
        draggable = true;
        doCloseX = true;
        doCloseButton = true;
        onlyOneOfTypeAllowed = true;
        absorbInputAroundWindow = false;
    }

    public override void DoWindowContents(Rect inRect)
    {
        // 顶部标题：加大字号绘制；右侧预留 60px 避免与关闭按钮重叠。
        using (new FontSizeScope(TitleFontSize))
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 60f, TitleHeight), "BetterInspectPane.SettingsWindowTitle".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // 标题下方为设置内容；底部预留关闭按钮区域（FooterRowHeight）。
        Rect content = new Rect(0f, TitleHeight, inRect.width, inRect.height - TitleHeight - FooterRowHeight);
        MyModTemplate.Instance.DoSettingsWindowContents(content);
    }

    public override void PostClose()
    {
        base.PostClose();
        MyModTemplate.Instance.Write();
    }
}