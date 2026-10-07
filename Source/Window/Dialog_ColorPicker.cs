using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

public class Dialog_MyModColorPicker : Dialog_ColorPickerBase
{
    private readonly Action<Color> onSave;

    private static readonly List<Color> pickableColors = new List<Color>
    {
        new Color(0.30f, 0.69f, 0.20f), // 绿
        new Color(0.95f, 0.85f, 0.20f), // 黄
        new Color(0.85f, 0.20f, 0.20f), // 红
        new Color(1.00f, 0.50f, 0.00f), // 橙
        new Color(0.20f, 0.50f, 1.00f), // 蓝
        new Color(0.50f, 0.20f, 0.80f), // 紫
        Color.white,
        new Color(0.55f, 0.55f, 0.55f), // 灰
        Color.black,
        Color.red,
        Color.green,
        Color.blue,
        Color.yellow,
        Color.cyan,
        Color.magenta,
    };

    protected override Color DefaultColor => oldColor;
    protected override bool ShowDarklight => false;
    protected override List<Color> PickableColors => pickableColors;
    protected override float ForcedColorValue => -1f;
    protected override bool ShowColorTemperatureBar => false;
    public override Vector2 InitialSize => new Vector2(600f, 520f);

    public Dialog_MyModColorPicker(Color initialColor, Action<Color> onSave)
        : base(
            Widgets.ColorComponents.All,
            Widgets.ColorComponents.Red | Widgets.ColorComponents.Green | Widgets.ColorComponents.Blue)
    {
        color = initialColor;
        oldColor = initialColor;
        this.onSave = onSave;
    }

    protected override void SaveColor(Color color)
    {
        // 取色器不编辑透明度，保存时保留原有 alpha。
        onSave(new Color(color.r, color.g, color.b, oldColor.a));
    }
}
