using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ASQBetterInspectPane;

public class MyModTemplate : Mod
{
    private static MyModTemplateSettings settings = null!;

    /// <summary>当前设置实例，供运行时设置窗口（Dialog_MyModSettings）读取与保存。</summary>
    public static MyModTemplateSettings Instance => settings;

    public MyModTemplate(ModContentPack pack) : base(pack)
    {
        settings = GetSettings<MyModTemplateSettings>();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        base.DoSettingsWindowContents(inRect);
        settings.DoSettingsWindowContents(inRect);
    }

    public override string SettingsCategory()
    {
        return "BetterInspectPane.SettingsCategory".Translate();
    }
}

[StaticConstructorOnStartup]
public static class HarmonyPatching
{
    static HarmonyPatching()
    {
        new Harmony("assssssqwww.MyModTemplate").PatchAll(Assembly.GetExecutingAssembly());
    }
}

