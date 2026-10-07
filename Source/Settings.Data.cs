using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace ASQBetterInspectPane;
// 设置面板数据
public partial class MyModTemplateSettings : ModSettings
{
    // ==================== 常量 ====================

    // 设置面板中各设置项之间的统一间隔。
    private const float ItemGap = 4f;
    // 单行设置项高度（复选框 / 标签行）。
    private const float RowHeight = 24f;
    private const float RowGap = ItemGap + RowHeight - 2f;
    // 滑杆设置块高度（滑杆矩形高度与其后的 y 增量一致）。
    private const float SliderHeight = 40f;
    private const float SliderGap = ItemGap + SliderHeight - 2f;
    // 滚动视图右侧预留的滚动条宽度。
    private const float ScrollbarGutter = 16f;
    // 每页内容顶部留白。
    private const float PageTopPadding = 4f;
    // 下拉按钮略向上偏移。
    private const float dropdownNudgeY = 3f;
    /// <summary>自定义字号的基准档位：以原版 Small（Arial_small）为基底，行高随像素字号变化。</summary>
    public const GameFont BaseFontSlot = GameFont.Small;

    // 各类颜色的默认值。
    private static readonly Color DefaultFullColor = new Color(0.30f, 0.69f, 0.20f);
    private static readonly Color DefaultMidColor = new Color(0.95f, 0.85f, 0.20f);
    private static readonly Color DefaultLowColor = new Color(0.85f, 0.20f, 0.20f);
    private static readonly Color DefaultEmptyColor = new Color(0.25f, 0.25f, 0.25f);
    private static readonly Color DefaultShieldColor = new Color(0.40f, 0.70f, 1.00f);
    private static readonly Color DefaultAmmoColor = new Color(0.45f, 0.45f, 0.45f);
    private static readonly Color DefaultFreshnessColor = new Color(0.20f, 0.35f, 0.65f);
    private static readonly Color DefaultWorkColor = new Color(0.85f, 0.85f, 0.85f);
    private static readonly Color DefaultGrowthColor = new Color(0.10f, 0.45f, 0.15f); // 深绿色
    private static readonly Color DefaultTimeRemainingColor = new Color(0.85f, 0.85f, 0.85f);
    private static readonly Color DefaultExplosiveColor = new Color(1.00f, 1.00f, 0.00f, 0.40f);
    // 电池/电网蓄电条默认颜色（黄色）。
    private static readonly Color DefaultBatteryColor = new Color(1.00f, 0.85f, 0.10f);
    private static readonly Color DefaultPowerGridColor = new Color(0.70f, 0.65f, 0.10f);
    // 研究条默认颜色（深蓝色）。
    private static readonly Color DefaultResearchColor = new Color(0.10f, 0.25f, 0.75f);
    // 维护条默认颜色（淡紫色，Vanilla Gravship Expanded）。
    private static readonly Color DefaultMaintenanceColor = new Color(0.78f, 0.55f, 0.95f);
    // 管道网络条默认颜色（白色，被「使用资源颜色」覆盖时取 PipeSystem.Resource.color）。
    private static readonly Color DefaultPipeNetColor = new Color(1.00f, 1.00f, 1.00f);
    // 冷却条默认颜色（淡蓝色，迫击炮开火后冷却期显示）。
    private static readonly Color DefaultCooldownColor = new Color(0.60f, 0.85f, 1.00f);
    // 区域条默认颜色：已种植 / 总体生长共用绿色、剩余容量白色、剩余鱼蓝色。
    private static readonly Color DefaultGrowingColor = new Color(0.10f, 0.80f, 0.15f);
    private static readonly Color DefaultZoneGrowthColor = new Color(0.10f, 0.80f, 0.15f);
    private static readonly Color DefaultStorageColor = new Color(0.90f, 0.90f, 0.90f);
    private static readonly Color DefaultFishingColor = new Color(0.30f, 0.60f, 1.00f);
    // 多选网格默认单元格边框颜色：近似原版 Widgets.DrawBox 的浅灰白边框。
    private static readonly Color DefaultMultiSelectBorderColor = new Color(0.72f, 0.72f, 0.72f);
    // 健康背景默认颜色：25% 透明度红。
    private static readonly Color DefaultHealthBackgroundColor = new Color(1f, 0f, 0f, 0.25f);
    // 充能条默认颜色（紫色，飞船反应堆 / 机械孕育器 / 定时激活器 / 清污泵 / 排水泵）。
    private static readonly Color DefaultChargeColor = new Color(0.70f, 0.40f, 1.00f);
    // Pawn 条默认颜色：血液红、心情青、食物橙、休息灰蓝、娱乐品红、机械能量黄。
    private static readonly Color DefaultBloodColor = new Color(1.0f, 0.40f, 0.40f);
    private static readonly Color DefaultMoodColor = new Color(0.00f, 0.80f, 0.80f);
    private static readonly Color DefaultFoodColor = new Color(0.90f, 0.55f, 0.15f);
    private static readonly Color DefaultRestColor = new Color(0.55f, 0.60f, 0.85f);
    private static readonly Color DefaultJoyColor = new Color(0.85f, 0.45f, 0.85f);
    private static readonly Color DefaultMechEnergyColor = new Color(0.80f, 0.80f, 0.00f);
    // 动物产物条默认颜色：产物（牛奶，白色）、产毛（羊毛，浅褐）、繁殖（蛋/怀孕，粉红）。
    private static readonly Color DefaultMilkColor = new Color(0.95f, 0.95f, 0.95f);
    private static readonly Color DefaultWoolColor = new Color(0.80f, 0.72f, 0.55f);
    private static readonly Color DefaultBreedingColor = new Color(0.95f, 0.55f, 0.75f);
    // 疼痛条默认颜色（深红，疼痛比例越高条越满）。
    private static readonly Color DefaultPainColor = new Color(0.85f, 0.18f, 0.18f);
    // Dubs Bad Hygiene 需求条默认颜色：膀胱（黄）、卫生（青）、口渴（蓝）。
    private static readonly Color DefaultBladderColor = new Color(0.95f, 0.80f, 0.25f);
    private static readonly Color DefaultHygieneColor = new Color(0.35f, 0.80f, 0.95f);
    private static readonly Color DefaultThirstColor = new Color(0.25f, 0.55f, 0.95f);
    // 强度条默认颜色（金橙色，World Domination 2 的据点 / 前哨站强度）。
    private static readonly Color DefaultStrengthColor = new Color(0.95f, 0.72f, 0.20f);
    // Pawn 技能条默认颜色（黄色）。
    private static readonly Color DefaultSkillColor = new Color(0.50f, 0.50f, 0.25f);

    // ==================== 枚举 ====================

    /// <summary>进度条占位：整行（独占一行）/ 单列 / 两列 / 适应（默认单列，右侧无条时增宽填满本行剩余列）。</summary>
    public enum BarSpan
    {
        FullRow,
        Column,
        TwoColumns,
        Adaptive
    }

    /// <summary>进度条区布局：单列 / 两列 / 三列 / 四列。</summary>
    public enum LayoutMode
    {
        Single,
        Two,
        Three,
        Four
    }

    /// <summary>进度条数值显示样式。</summary>
    public enum ValueDisplayStyle
    {
        Numeric,
        Percent,
        PercentOneDecimal,
        PercentTwoDecimals,
        None
    }

    /// <summary>进度条标签显示样式：普通（条外固定列）或嵌入（文本绘制在条内，进度反色）。</summary>
    public enum LabelStyle
    {
        Normal,
        Embedded
    }

    /// <summary>进度条数值显示样式：普通（条外固定列）/ 嵌入（条内反色）/ 隐藏（不显示数值、不预留宽度）。</summary>
    public enum BarValueStyle
    {
        Normal,
        Embedded,
        Hidden
    }

    /// <summary>Pawn 技能区每行列数：3 / 4 / 6 列（默认 6 列）。</summary>
    public enum PawnSkillColumns
    {
        Three = 3,
        Four = 4,
        Six = 6
    }

    /// <summary>Pawn 技能激情的表示方式：文本（名称后附加 + / ++）或图标（原版激情图标）。</summary>
    public enum SkillPassionStyle
    {
        Text,
        Icon
    }

    // 缓动方式。
    public enum EasingStyle
    {
        Linear,
        EaseIn,
        EaseOut
    }

    /// <summary>Info Card Plus 收藏 Stat 在信息文字中的位置：最前 / 最后。</summary>
    public enum InfoCardPlusPinnedPosition
    {
        Before,
        After
    }

    /// <summary>唯一条标识，用于表示一条进度条。</summary>
    public enum BarType
    {
        Health,
        Shield,
        Ammo,
        Freshness,
        Work,
        Growth,
        TimeRemaining,
        FactionRelation,
        Growing,
        ZoneGrowth,
        Storage,
        Fishing,
        Battery,
        PowerGrid,
        Research,
        Maintenance,
        PipeNet,
        Cooldown,
        Charge,
        // Pawn 面板条（追加在枚举末尾；Scribe 按名称序列化，旧存档兼容）。
        Bleed,
        Mood,
        Food,
        Rest,
        Joy,
        MechEnergy,
        // Pawn 动物条（追加在枚举末尾）：产物（挤奶）/ 产毛（剪毛）/ 繁殖（下蛋或怀孕）。
        Milk,
        Wool,
        Breeding,
        // Pawn 疼痛条：疼痛比例（0..1，可超过 1 高痛）。
        Pain,
        // Pawn 模组需求条（Dubs Bad Hygiene 等，只要有对应需求就显示）：膀胱 / 卫生 / 口渴。
        Bladder,
        Hygiene,
        Thirst,
        // 世界对象强度条（World Domination 2，反射读取）：据点 / 前哨站总强度（进攻 + 防御）。
        Strength
    }

    /// <summary>设置窗口的标签页。</summary>
    private enum SettingsPage { General, Bars, MultiSelect, Colors, Effects }

    // ==================== 进度条显示顺序 ====================

    // 显示顺序不再用逐条手工编号的 order 整数，而是以三组「顺序列表」（元素为 BarType）
    // 作为唯一事实来源：列表下标即显示顺序。新增一条只需：
    //   1) 在 BarType 枚举加一个成员；
    //   2) 把该成员加入对应组的默认顺序数组；
    //   3) 在设置面板的 BarSetting 与绘制逻辑的 Spec 分发中补对应分支。
    // 不必再手工维护互不重复的顺序数字，也不存在 order 冲突导致排序错乱的问题。

    // 三组进度条的默认显示顺序（数组下标即顺序）。加载旧档案时，
    // 顺序列表里缺失的默认成员会按下面这些数组的顺序自动补到队尾。
    private static readonly BarType[] DefaultMainBarOrder =
        { BarType.Health, BarType.Shield, BarType.Ammo, BarType.Freshness, BarType.Work, BarType.Growth, BarType.Storage, BarType.Battery, BarType.PowerGrid, BarType.Research, BarType.Maintenance, BarType.PipeNet, BarType.Cooldown, BarType.Charge };
    private static readonly BarType[] DefaultWorldObjectBarOrder =
        { BarType.TimeRemaining, BarType.FactionRelation, BarType.Strength };
    private static readonly BarType[] DefaultZoneBarOrder =
        { BarType.Growing, BarType.ZoneGrowth, BarType.Storage, BarType.Fishing };
    private static readonly BarType[] DefaultPawnBarOrder =
        {
            BarType.Health, BarType.Bleed, BarType.Pain, BarType.Mood, BarType.Food, BarType.Rest, BarType.Joy, BarType.MechEnergy,
            BarType.Milk, BarType.Wool, BarType.Breeding,
            BarType.Bladder, BarType.Hygiene, BarType.Thirst
        };

    // 当前显示顺序（设置面板 ▲▼ 直接交换这里的元素；绘制逻辑按此顺序收集条）。
    public static List<BarType> mainBarOrder = new List<BarType>(DefaultMainBarOrder);
    public static List<BarType> worldObjectBarOrder = new List<BarType>(DefaultWorldObjectBarOrder);
    public static List<BarType> zoneBarOrder = new List<BarType>(DefaultZoneBarOrder);
    public static List<BarType> pawnBarOrder = new List<BarType>(DefaultPawnBarOrder);

    // 白色字体开关（按条，HashSet 仅存开启的条，缺失即默认关闭）：开启后该条的标签与数值无视进度条比例，
    // 总是以白色显示（条内嵌入文字不再被进度覆盖部分反黑）。
    public static HashSet<BarType> barWhiteFont = [BarType.Freshness, BarType.Growth, BarType.Research, BarType.Pain];

    // ==================== 基础开关与检查面板 ====================

    public static bool enabled = true;

    // Pawn 单选检查面板总开关：开启后选中 Pawn 时用自定义进度条面板替代原版内容（默认关闭）。
    public static bool enablePawns = true;

    // Pawn 策略行（食物/管制/着装/区域）显示开关：开启后在 Pawn 面板顶部绘制策略行（需 enablePawns）。
    public static bool enablePawnPolicyRow = true;

    // 是否覆盖原版 InspectPaneOnGUI：开启后接管整体绘制以应用四周边距与标题字号，
    // 关闭则走原版绘制（不影响其它功能）。
    public static bool overrideInspectPane = false;

    // 检查面板整体边距与标题字号（仅在 overrideInspectPane 开启时生效）：
    // 四周边距替换原版 InpectPaneOnGUI 里的 ContractedBy(12f)；标题字号替换原版固定 GameFont.Medium。
    public static float paneMargin = 8f;
    public static int titleFontSize = 23;

    // Pawn 单选检查面板专用（需 overrideInspectPane 开启才显示对应设置；运行时仅对单选 Pawn 生效）：
    // 显示 Pawn 全名：覆盖默认标题，改用 Pawn.Name.ToStringFull。
    public static bool pawnShowFullName = false;
    // 显示 Pawn 额外按钮：在标题右侧原版按钮左侧依次绘制 异种类型 / 阵营 / 文化 三个图标按钮（顺序从右到左）。
    public static bool pawnShowExtraButtons = false;
    // Pawn 面板标题字号：单选 Pawn 时标题改用该字号（普通对象仍用 titleFontSize）。
    public static int pawnTitleFontSize = 23;

    // Pawn 技能区（需 enablePawns 开启自定义 Pawn 面板内容；与 overrideInspectPane 无关）：
    // 在 Pawn 面板普通条区域下方以多列平铺的嵌入样式条显示技能（标签 = 技能名、数值 = 技能等级、填充 = 升级进度）。
    public static bool pawnShowSkills = true;
    // 每行列数（3 / 4 / 6，默认 6）。
    public static PawnSkillColumns pawnSkillColumns = PawnSkillColumns.Six;
    // 技能条文本字号（像素）。
    public static int pawnSkillFontSize = 12;
    // 技能条高度。
    public static float pawnSkillBarHeight = 13f;
    // 技能条列间距：相邻两列之间的空隙。
    public static float pawnSkillColumnGap = 1f;
    // 技能区纵向间隔：技能行之间、以及技能区与上下分隔线之间的间隔。
    public static float pawnSkillSpacing = 2f;
    // 技能条填充颜色（默认黄色）。
    public static Color pawnSkillColor = DefaultSkillColor;
    // 技能条文本用白色字体：开启后标签与数值无视进度条比例，总是以白色显示（不绘制反色遮罩）。
    public static bool pawnSkillWhiteFont = true;
    // 技能条按总进度填充：开启后填充比例 = (当前等级 + 当前升级进度%) / 技能等级上限 20；
    // 关闭则仅显示当前等级内的升级进度。
    public static bool pawnSkillTotalProgress = false;
    // 技能激情表示方式：文本（技能名后附加 + / ++）或图标（原版激情图标，SkillUI.PassionMinorIcon / PassionMajorIcon）。
    public static SkillPassionStyle pawnSkillPassionStyle = SkillPassionStyle.Text;
    // 激情图标位置（仅图标样式生效）：关闭 = 图标绘制在技能名左侧（默认）；开启 = 图标绘制在技能名文本右侧。
    public static bool pawnSkillPassionIconAfterName = false;
    // 技能条纯文本样式（默认关闭）：仅绘制技能名与等级文本，不绘制条背景与进度填充。
    public static bool pawnSkillPlainText = false;
    // 技能等级带升级进度小数（默认关闭）：等级以固定两位小数显示，如 5 级 + 55% 升级进度 = 5.55，整级 / 满级 = 5.00 / 20.00。
    public static bool pawnSkillShowProgressText = false;

    // Pawn 条悬浮提示（默认开启，统一开关）：悬停 Pawn 面板的技能条与需求条（心情/食物/休息/娱乐/机械能量/模组需求）时
    // 显示原版对应提示——技能条复用原版 SkillUI.DrawSkill 的技能提示（私有 GetSkillDescription，反射调用），
    // 需求条复用原版 Need.GetTipString 需求提示。
    public static bool pawnBarTooltips = true;

    // 检查面板窗口尺寸：宽度 = max(原版按 Tab 数量计算值, 最小宽度)、
    // 高度 = max(原版固定 165f, 最小高度)。
    public static float paneWidth = 432f;
    public static float paneHeight = 170f;

    // 覆盖 InspectString 绘制：开启后自定义面板路径改用本模组自己的 DrawInspectStringFor
    // 实现（InspectString 以 20 帧缓存，文本不变则高度一并缓存）。
    public static bool overrideInspectString = true;
    // 自适应面板高度：信息文字在面板预留区域放不下时自动增高面板（需 overrideInspectString 开启）。
    public static bool adaptivePaneHeight = true;

    // 自适应面板高度实际所需的面板总高度（运行时按帧更新，不持久化）。
    public static float requiredPaneHeight = 0f;

    /// <summary>
    /// 面板有效高度（供各处读取面板高度的补丁统一使用）：
    /// 仅在「覆盖 InspectString 绘制 + 自适应面板高度」同时开启时，才把运行时所需高度
    /// 纳入计算；否则等于配置的最小高度，避免功能关闭时残留值影响面板尺寸。
    /// </summary>
    public static float EffectivePaneHeight =>
        overrideInspectString && adaptivePaneHeight
            ? Mathf.Max(paneHeight, requiredPaneHeight)
            : paneHeight;

    /// <summary>当前选择场景下的面板有效高度：多选网格激活时用多选最小高度，否则用单选 EffectivePaneHeight。</summary>
    public static float CurrentEffectivePaneHeight =>
        InspectPanePatch.IsMultiSelectGridActive() ? MultiSelectEffectiveMinHeight : EffectivePaneHeight;

    // ==================== 多选单位选择网格 ====================

    // 多选单位选择网格：多选（>1 个 Thing）时在检查面板正文绘制 RTS 风格单位选择网格
    // （殖民者单列立绘，其余按 PawnKindDef / ThingDef 分组），可点击单元格选择对应单位/组。
    public static bool enableMultiSelectGrid = true;
    // 多选网格是否也作用于区域（殖民地图多选多个区域时，在检查面板绘制区域网格，按区域类型分组）。
    // 需 enableMultiSelectGrid 同时开启才生效（分层子项，见 UI 嵌套）。
    public static bool enableMultiSelectGridZones = true;
    // 多选网格是否也作用于世界地图对象（世界地图多选多个对象时，在地图检查面板绘制对象网格，按 WorldObjectDef 分组）。
    // 需 enableMultiSelectGrid 同时开启才生效（分层子项，见 UI 嵌套）。
    public static bool enableMultiSelectGridWorldObjects = true;
    public static float multiSelectGridCellSize = 48f;
    public static int multiSelectGridRows = 2;
    // 背景底框以实际网格绘制：关闭时底框铺满整个内容区（默认），开启时仅按 columns × effectiveRows 的实际网格范围绘制。
    public static bool multiSelectGridBackgroundExact = false;
    // 网格单元间距：相邻单元格之间的空隙（像素）。
    public static float multiSelectCellGap = 4f;
    // 网格单元格边框宽度（像素）。
    public static int multiSelectCellBorderWidth = 1;
    // 网格单元格默认（非 shift 构建选区 / 非 ctrl 待移除）边框颜色。
    public static Color multiSelectCellBorderColor = DefaultMultiSelectBorderColor;
    // 数量角标：数量 ≥ 1000 时以 k 缩写显示（如 1.2k）。
    public static bool multiSelectCountAbbreviation = true;
    // 数量角标：是否显示前缀「×」（关闭后仅显示数字，如 "12" 而非 "×12"）。
    public static bool multiSelectBadgeShowX = true;
    // 数量角标：字号设置文本大小，缩放比例设置角标框大小。
    public static int multiSelectBadgeFontSize = 11;
    public static float multiSelectBadgeScale = 1f;
    // 输入键映射：默认由左键触发多选、右键触发多重取消（拖拽跟随同一映射）；
    // 开启下面两项后恢复旧行为（Shift 触发多选 / Ctrl 触发多重取消）。
    // 开启「使用 Shift 键多选」= 与左键单选功能互换；开启「使用 Ctrl 键多重取消」= 与右键取消功能互换。
    public static bool useShiftKeyForMultiSelect = false;
    public static bool useCtrlKeyForMultiCancel = false;
    // 单组展开：某类别的非殖民者项恰好只构成一个组（且组内有多项）时，不再聚合成 ×N 分组格，
    // 改为逐项平铺绘制。Pawn / 非 Pawn Thing / 非 Thing（区域与世界对象）类别独立判定、可独立开关
    // （Pawn 默认开，非 Pawn Thing 默认关，非 Thing 默认开）。
    public static bool expandSingleGroupPawn = true;
    public static bool expandSingleGroupNonPawn = false;
    public static bool expandSingleGroupNonThing = true;
    // 多选网格健康条：在每个单元格顶部绘制健康条（默认开启）。
    public static bool enableMultiSelectHealthBar = true;
    // 健康条高度（像素）。
    public static float multiSelectHealthBarHeight = 3f;
    // Pawn 健康满 100% 时隐藏该格健康条（默认关闭，Pawn 满血仍显示）。
    public static bool hideMultiSelectFullHealthBarPawn = false;
    // 非 Pawn 健康满 100% 时隐藏该格健康条（默认开启，非 Pawn 满血不显示）。
    public static bool hideMultiSelectFullHealthBarNonPawn = true;
    // 使用健康背景替代健康条：开启后不再在单元格顶部绘制健康条，改为在单元格背景
    // 按健康比例自下而上填充半透明色块（类似垂直健康条但不绘制背景轨道）。
    public static bool useHealthBackgroundInsteadOfBar = false;
    // 反转健康背景填充方向：开启后改为按受伤比例（1-健康）从上端填充（需 useHealthBackgroundInsteadOfBar 开启）。
    public static bool invertHealthBackground = true;
    // 健康背景填充颜色（默认 25% 透明度红）。
    public static Color healthBackgroundColor = DefaultHealthBackgroundColor;
    // 额外选中指示：多选网格中鼠标悬停 / Shift 构建选区 / Ctrl 标记待移除的当前项，
    // 在地图上用原版指示箭头（GenDraw.DrawArrowPointingAt）标出（默认开启）。
    public static bool enableMultiSelectMapIndicator = true;

    // 多选检查面板尺寸：多选网格场景下检查面板可独立于单选面板设置最小宽/高。
    // 「覆盖最小宽度/高度」开启后改用覆盖值，否则沿用全局 paneWidth / paneHeight；
    // 「自适应高度」开启后若配置的最小高度放不下配置行数的网格，自动提升到能容纳所需的高度。
    public static bool multiSelectOverrideMinWidth = false;
    public static float multiSelectMinWidth = 432f;
    public static bool multiSelectOverrideMinHeight = false;
    public static float multiSelectMinHeight = 170f;
    public static bool multiSelectAdaptiveHeight = true;

    /// <summary>多选检查面板的有效最小宽度：开启覆盖用覆盖值，否则沿用全局 paneWidth。</summary>
    public static float MultiSelectEffectiveMinWidth =>
        multiSelectOverrideMinWidth ? multiSelectMinWidth : paneWidth;

    /// <summary>多选检查面板的有效最小高度：开启覆盖用覆盖值（否则沿用全局 paneHeight），
    /// 自适应高度开启时再提升到能容纳配置行数网格所需的最小面板高度。</summary>
    public static float MultiSelectEffectiveMinHeight
    {
        get
        {
            float baseH = multiSelectOverrideMinHeight ? multiSelectMinHeight : paneHeight;
            if (!multiSelectAdaptiveHeight)
            {
                return baseH;
            }
            return Mathf.Max(baseH, InspectPanePatch.MultiSelectGridRequiredPaneHeight());
        }
    }

    // ==================== 选择行为（杂项）====================

    // 选择数量限制修改：开启后可用滑条修改原版 Selector 的单次选择数量上限（原版固定 200）。
    public static bool enableSelectionLimitModify = false;
    public static int selectionLimit = 200;

    // 改进的原版 Shift 选择：开启后按住 Shift 选择时，
    //   1) 框选时当前优先级没有新增选中物体则降级尝试下一优先级；
    //   2) 选中与当前选择类型不同的对象时忽略此次选择（不再清空现有选择）。
    public static bool enableImprovedShiftSelection = false;

    // Ctrl 选择功能：按住 Ctrl 框选时按当前选择状态做「范围内同类型批量选择」。
    public static bool enableCtrlSelection = false;

    /// <summary>当前生效的选择数量上限：关闭修改开关时沿用原版 200。</summary>
    public static int CurrentSelectionLimit => enableSelectionLimitModify ? selectionLimit : 200;

    // 记忆已打开观察面板标签页：切换目标种类导致标签页关闭后，再次选中同种目标时
    // 若之前打开过的标签页仍可打开则自动打开；记忆按「最近 N 个目标种类」保存（LRU）。
    public static bool rememberOpenTab = false;
    public const int RememberOpenTabCapacity = 2; // 记忆容量（目标种类数量），范围 2~10

    // ==================== 进度条通用外观 ====================

    // 条默认高度：所有条的基本高度，各条再通过「高度偏移」在此基础上增减（健康条默认 +2，其余默认 0）。
    public static float defaultBarHeight = 14f;
    public static bool showSeparators = true;
    public static float separatorHeight = 1f;
    public static float rowSpacing = 3f;

    // 进度条标签与数值的固定宽度（统一对齐，不随文本长度变化）。
    public static float labelWidth = 64f;
    public static float valueWidth = 80f;

    // 标签/数值的条外显示样式（全局，对所有条生效）：
    // 嵌入 → 不预留条外宽度，文本在条内按「未覆盖=白、被覆盖=黑」反色绘制；隐藏 → 不显示数值也不预留宽度。
    public static LabelStyle labelStyle = LabelStyle.Embedded;
    public static BarValueStyle barValueStyle = BarValueStyle.Embedded;
    // 嵌入时左右收缩宽度（像素）：标签 / 数值分别向条中心靠拢（0-30，默认 4）。
    public static float embedInsetLabel = 4f;
    public static float embedInsetValue = 4f;

    // 字号设置（像素）：条标签、条数值、额外文字（面板下方原版信息文字）分别独立。
    // 参考 RimHUD 的做法：以像素字号复制当前游戏字体样式，可自由指定具体数值，
    // 不再局限于 GameFont 的 Tiny / Small / Medium 三档固定选择（默认 13 对应原版 Small）。
    public static int labelFontSize = 14;
    public static int valueFontSize = 14;
    public static int infoFontSize = 14;

    // Pawn 策略行（食物/管制/着装/区域）字号（像素）与行高（像素）。
    public static int policyRowFontSize = 14;
    // 策略行行高下限（像素）：字号过大导致文本放不下时自动增高（默认与原自动高度一致）。
    public static float policyRowHeight = 16f;

    // ==================== 布局 ====================

    // 进度条区布局：单列 / 两列 / 三列 / 四列。多列时条按占位（整行/单列/两列/适应）排布网格。
    public static LayoutMode layoutMode = LayoutMode.Single;
    public static float columnGap = 4f;

    // 三列/四列布局下的「双条行均分」：同一行只有 1~2 个单列宽的条（如单个单列条、两个单列条，
    // 或单列条+适应条）而占不满整行、会留出空列时，该行改为两列平分显示（单个单列条占左半、右半留空），
    // 避免条过窄与大片空白。仅在三列/四列布局时生效并显示该开关（两列布局本身即两列平分，无需该开关）。
    public static bool evenSplitTwoBarRows = false;

    /// <summary>当前布局的列数：单列=1、两列=2、三列=3、四列=4。</summary>
    public static int LayoutColumns => layoutMode switch
    {
        LayoutMode.Two => 2,
        LayoutMode.Three => 3,
        LayoutMode.Four => 4,
        _ => 1
    };

    // Pawn 面板独立布局（与普通面板的 layoutMode / columnGap / evenSplitTwoBarRows 互相独立）。
    public static LayoutMode pawnLayoutMode = LayoutMode.Two;
    public static float pawnColumnGap = 4f;
    // 三列/四列布局下的「双条行均分」（Pawn 面板独立开关）。
    public static bool pawnEvenSplitTwoBarRows = false;

    /// <summary>Pawn 面板当前布局的列数：单列=1、两列=2、三列=3、四列=4。</summary>
    public static int PawnLayoutColumns => pawnLayoutMode switch
    {
        LayoutMode.Two => 2,
        LayoutMode.Three => 3,
        LayoutMode.Four => 4,
        _ => 1
    };

    // ==================== 缓动效果 ====================

    public static bool enableEaseEffect = true;
    public static float easeSpeed = 0.2f;
    public static EasingStyle easingStyle = EasingStyle.Linear;

    // ==================== 各进度条设置 ====================

    // 以下每条进度条设置集中排列：健康条始终显示、高度/颜色沿用通用设置（仅占位/数值样式）；
    // 其余可开关条按「开关 → 高度 → 颜色 → 数值样式 → 占位」排列；
    // 各自的「显示顺序」由上面的顺序列表（mainBarOrder / worldObjectBarOrder / zoneBarOrder）定义。

    // ----- 健康条 -----
    // 健康条高度偏移默认 +2（相对默认高度略高于其它条）；其余条默认 0。
    public static float healthBarHeightOffset = 2f;
    public static float healthFontScale = 1f;
    public static BarSpan healthBarSpan = BarSpan.Column;
    public static ValueDisplayStyle healthValueStyle = ValueDisplayStyle.Numeric;
    // 无限耐久条：为不使用命中点（def.useHitPoints 为 false，即无限耐久 / 无法损坏）的对象
    // 在单选检查面板也显示一条永远满格的耐久条（数值显示 ∞），默认关闭。
    public static bool showInfiniteDurabilityBar = false;

    // ----- 护盾条 -----
    public static bool enableShieldBar = true;
    public static bool autoHideShieldAtZero = true; // 0% 时自动隐藏（护盾耗尽后隐藏，默认开启）
    public static float shieldBarHeightOffset = 0f;
    public static float shieldFontScale = 1f;
    public static Color shieldColor = DefaultShieldColor;
    public static BarSpan shieldBarSpan = BarSpan.Column;
    public static ValueDisplayStyle shieldValueStyle = ValueDisplayStyle.Numeric;

    // ----- 弹药条 -----
    public static bool enableAmmoBar = true;
    public static float ammoBarHeightOffset = 0f;
    public static float ammoFontScale = 1f;
    public static Color ammoColor = DefaultAmmoColor;
    public static BarSpan ammoBarSpan = BarSpan.Column;
    public static ValueDisplayStyle ammoValueStyle = ValueDisplayStyle.Numeric;

    // ----- 新鲜条 -----
    public static bool enableFreshnessBar = true;
    public static float freshnessBarHeightOffset = 0f;
    public static float freshnessFontScale = 1f;
    public static Color freshnessColor = DefaultFreshnessColor;
    public static BarSpan freshnessBarSpan = BarSpan.Column;
    public static ValueDisplayStyle freshnessValueStyle = ValueDisplayStyle.Numeric;

    // ----- 工作条 -----
    public static bool enableWorkBar = true;
    public static bool autoHideWorkAtHundred = true; // 100% 时自动隐藏（工作完成后隐藏，默认开启）
    public static float workBarHeightOffset = 0f;
    public static float workFontScale = 1f;
    public static Color workColor = DefaultWorkColor;
    public static BarSpan workBarSpan = BarSpan.Column;
    public static ValueDisplayStyle workValueStyle = ValueDisplayStyle.Numeric;

    // ----- 生长条 -----
    public static bool enableGrowthBar = true;
    public static float growthBarHeightOffset = 0f;
    public static float growthFontScale = 1f;
    public static Color growthColor = DefaultGrowthColor;
    public static BarSpan growthBarSpan = BarSpan.Column;
    public static ValueDisplayStyle growthValueStyle = ValueDisplayStyle.Numeric;

    // ----- 研究条 -----
    public static bool enableResearchBar = true;
    public static bool autoHideResearchAtHundred = true; // 100% 时自动隐藏（研究完成后隐藏，默认开启）
    public static float researchBarHeightOffset = 0f;
    public static float researchFontScale = 1f;
    public static Color researchColor = DefaultResearchColor;
    public static BarSpan researchBarSpan = BarSpan.Column;
    public static ValueDisplayStyle researchValueStyle = ValueDisplayStyle.Numeric;

    // ----- 世界地图对象 -----
    // 总开关：自定义检查面板是否也适用于世界地图对象。
    public static bool enableWorldObjects = true;

    // 剩余时间条（世界地图对象）。
    public static bool enableTimeRemainingBar = true;
    public static bool autoHideTimeRemainingAtZero = true; // 0% 时自动隐藏（剩余时间耗尽后隐藏，默认开启）
    public static float timeRemainingBarHeightOffset = 0f;
    public static float timeRemainingFontScale = 1f;
    public static Color timeRemainingColor = DefaultTimeRemainingColor;
    public static BarSpan timeRemainingBarSpan = BarSpan.Column;
    public static ValueDisplayStyle timeRemainingValueStyle = ValueDisplayStyle.Numeric;

    // 阵营关系条（世界地图对象）。
    public static bool enableFactionRelationBar = true;
    public static float factionRelationBarHeightOffset = 0f;
    public static float factionRelationFontScale = 1f;
    public static BarSpan factionRelationBarSpan = BarSpan.Column;
    public static ValueDisplayStyle factionRelationValueStyle = ValueDisplayStyle.Numeric;
    // 关系阈值标记（世界地图对象阵营关系条）：开启后条上限改用「关系上限」并在盟友阈值（75）位置画标记——
    // World Domination 2 激活时上限取其设置中的关系上限（GoodwillCapUtility.MaxGoodwillCap，默认 200），
    // 未激活时用原版好感上限（100）；关闭时维持原口径（上限 = 阈值 75，无标记）。
    public static bool showRelationThresholdMarker = true;

    // 强度条（World Domination 2，反射读取）。
    public static bool enableStrengthBar = true;
    public static float strengthBarHeightOffset = 0f;
    public static float strengthFontScale = 1f;
    public static Color strengthColor = DefaultStrengthColor;
    public static BarSpan strengthBarSpan = BarSpan.Column;
    public static ValueDisplayStyle strengthValueStyle = ValueDisplayStyle.Numeric;

    // ----- 区域 -----
    // 总开关：自定义检查面板是否也适用于区域（种植/储存/钓鱼区）。
    public static bool enableZones = true;

    // 已种植条（种植区）。
    public static bool enableGrowingBar = true;
    public static float growingBarHeightOffset = 0f;
    public static float growingFontScale = 1f;
    public static Color growingColor = DefaultGrowingColor;
    public static BarSpan growingBarSpan = BarSpan.Column;
    public static ValueDisplayStyle growingValueStyle = ValueDisplayStyle.Numeric;

    // 总体生长条（种植区）。
    public static bool enableZoneGrowthBar = true;
    public static float zoneGrowthBarHeightOffset = 0f;
    public static float zoneGrowthFontScale = 1f;
    public static Color zoneGrowthColor = DefaultZoneGrowthColor;
    public static BarSpan zoneGrowthBarSpan = BarSpan.Column;
    public static ValueDisplayStyle zoneGrowthValueStyle = ValueDisplayStyle.Numeric;

    // 剩余容量条（储存区）。
    public static bool enableStorageBar = true;
    public static float storageBarHeightOffset = 0f;
    public static float storageFontScale = 1f;
    public static Color storageColor = DefaultStorageColor;
    public static BarSpan storageBarSpan = BarSpan.Column;
    public static ValueDisplayStyle storageValueStyle = ValueDisplayStyle.Numeric;

    // 剩余鱼条（钓鱼区）。
    public static bool enableFishingBar = true;
    public static float fishingBarHeightOffset = 0f;
    public static float fishingFontScale = 1f;
    public static Color fishingColor = DefaultFishingColor;
    public static BarSpan fishingBarSpan = BarSpan.Column;
    public static ValueDisplayStyle fishingValueStyle = ValueDisplayStyle.Numeric;

    // 保留目标比例阈值标记（钓鱼区）：在剩余鱼条上按钓鱼区的 targetPopulationPct
    // （默认 0.5）位置绘制黑色阈值标记，标示水域鱼群比例低于该值时钓鱼区停止捕鱼。
    public static bool showFishingTargetPctMarker = true;

    // ----- 电池蓄电条（电池）-----
    public static bool enableBatteryBar = true;
    public static float batteryBarHeightOffset = 0f;
    public static float batteryFontScale = 1f;
    public static Color batteryColor = DefaultBatteryColor;
    public static BarSpan batteryBarSpan = BarSpan.Column;
    public static ValueDisplayStyle batteryValueStyle = ValueDisplayStyle.Numeric;

    // ----- 电网蓄电条（电力设备）-----
    public static bool enablePowerGridBar = true;
    public static float powerGridBarHeightOffset = 0f;
    public static float powerGridFontScale = 1f;
    public static Color powerGridColor = DefaultPowerGridColor;
    public static BarSpan powerGridBarSpan = BarSpan.Column;
    public static ValueDisplayStyle powerGridValueStyle = ValueDisplayStyle.Numeric;

    // ----- 维护条（Vanilla Gravship Expanded，反射读取）-----
    public static bool enableMaintenanceBar = true;
    public static float maintenanceBarHeightOffset = 0f;
    public static float maintenanceFontScale = 1f;
    public static Color maintenanceColor = DefaultMaintenanceColor;
    public static BarSpan maintenanceBarSpan = BarSpan.Column;
    public static ValueDisplayStyle maintenanceValueStyle = ValueDisplayStyle.PercentTwoDecimals;

    // ----- 管道网络条（Vanilla Expanded Framework 的管道系统）-----
    // 「使用资源颜色」：开启后容量条用 PipeSystem.Resource.color，管道网络条用同一颜色但透明度 70%。
    public static bool useResourceColor = true;
    public static bool enablePipeNetBar = true;
    public static float pipeNetBarHeightOffset = 0f;
    public static float pipeNetFontScale = 1f;
    public static Color pipeNetColor = DefaultPipeNetColor;
    public static BarSpan pipeNetBarSpan = BarSpan.Column;
    public static ValueDisplayStyle pipeNetValueStyle = ValueDisplayStyle.Numeric;

    // ----- 冷却条（炮塔，反射读取 burstCooldownTicksLeft）-----
    public static bool enableCooldownBar = true;
    public static bool autoHideCooldownAtHundred = true; // 100% 时自动隐藏（冷却完成后隐藏，默认开启）
    public static float cooldownBarHeightOffset = 0f;
    public static float cooldownFontScale = 1f;
    public static Color cooldownColor = DefaultCooldownColor;
    public static BarSpan cooldownBarSpan = BarSpan.Column;
    public static ValueDisplayStyle cooldownValueStyle = ValueDisplayStyle.Numeric;
    public static bool invertCooldownFill = false; // 反转填充比例（默认由空渐满，开启后由满渐空）
    public static float minCooldownSeconds = 3f; // 炮塔冷却条最短显示冷却时长（秒）：总冷却低于该值的炮塔不显示冷却条，避免短冷却炮塔的条频繁闪动

    // ----- 充能条（飞船反应堆 / 机械孕育器 / 定时激活器 / 清污泵 / 排水泵）-----
    public static bool enableChargeBar = true;
    public static bool autoHideChargeAtHundred = true; // 100% 时自动隐藏（充能完成后隐藏，默认开启）
    public static float chargeBarHeightOffset = 0f;
    public static float chargeFontScale = 1f;
    public static Color chargeColor = DefaultChargeColor;
    public static BarSpan chargeBarSpan = BarSpan.Column;
    public static ValueDisplayStyle chargeValueStyle = ValueDisplayStyle.Numeric;

    // ----- Pawn 条（Pawn 单选检查面板）-----
    // 健康条高度/字号/占位/数值样式与物品/建筑健康条互相独立（颜色仍取通用 full/mid/low 分级）；
    // 时间表/允许区域部件由原版方法绘制、无条设置。

    // Pawn 健康条（高度偏移默认沿原共享设置值，避免改动默认外观）。
    public static float pawnHealthBarHeightOffset = 0f;
    public static float pawnHealthFontScale = 1f;
    public static BarSpan pawnHealthBarSpan = BarSpan.Column;
    public static ValueDisplayStyle pawnHealthValueStyle = ValueDisplayStyle.Percent;

    // 血液条。
    public static bool enableBloodBar = true;
    public static bool autoHideBloodAtFull = true; // 满血（无失血）时自动隐藏（默认开启）
    public static float bloodBarHeightOffset = 0f;
    public static float bloodFontScale = 1f;
    public static Color bloodColor = DefaultBloodColor;
    public static BarSpan bloodBarSpan = BarSpan.Column;
    public static ValueDisplayStyle bloodValueStyle = ValueDisplayStyle.Numeric;

    // 心情条。
    public static bool enableMoodBar = true;
    public static float moodBarHeightOffset = 0f;
    public static float moodFontScale = 1f;
    public static Color moodColor = DefaultMoodColor;
    public static BarSpan moodBarSpan = BarSpan.Column;
    public static ValueDisplayStyle moodValueStyle = ValueDisplayStyle.Percent;

    // 食物条。
    public static bool enableFoodBar = true;
    public static float foodBarHeightOffset = 0f;
    public static float foodFontScale = 1f;
    public static Color foodColor = DefaultFoodColor;
    public static BarSpan foodBarSpan = BarSpan.Column;
    public static ValueDisplayStyle foodValueStyle = ValueDisplayStyle.Percent;

    // 休息条。
    public static bool enableRestBar = true;
    public static float restBarHeightOffset = 0f;
    public static float restFontScale = 1f;
    public static Color restColor = DefaultRestColor;
    public static BarSpan restBarSpan = BarSpan.Column;
    public static ValueDisplayStyle restValueStyle = ValueDisplayStyle.Percent;

    // 娱乐条。
    public static bool enableJoyBar = true;
    public static float joyBarHeightOffset = 0f;
    public static float joyFontScale = 1f;
    public static Color joyColor = DefaultJoyColor;
    public static BarSpan joyBarSpan = BarSpan.Column;
    public static ValueDisplayStyle joyValueStyle = ValueDisplayStyle.Percent;

    // 机械能量条（机械体专用，替代其食物/休息需求）。
    public static bool enableMechEnergyBar = true;
    public static float mechEnergyBarHeightOffset = 0f;
    public static float mechEnergyFontScale = 1f;
    public static Color mechEnergyColor = DefaultMechEnergyColor;
    public static BarSpan mechEnergyBarSpan = BarSpan.Column;
    public static ValueDisplayStyle mechEnergyValueStyle = ValueDisplayStyle.Percent;

    // 产物条（可挤奶动物，CompMilkable）：奶水充盈进度（挤奶后归零重新累积）。
    public static bool enableMilkBar = true;
    public static float milkBarHeightOffset = 0f;
    public static float milkFontScale = 1f;
    public static Color milkColor = DefaultMilkColor;
    public static BarSpan milkBarSpan = BarSpan.Column;
    public static ValueDisplayStyle milkValueStyle = ValueDisplayStyle.Percent;

    // 产毛条（可剪毛动物，CompShearable）：羊毛生长进度（剪毛后归零重新累积）。
    public static bool enableWoolBar = true;
    public static float woolBarHeightOffset = 0f;
    public static float woolFontScale = 1f;
    public static Color woolColor = DefaultWoolColor;
    public static BarSpan woolBarSpan = BarSpan.Column;
    public static ValueDisplayStyle woolValueStyle = ValueDisplayStyle.Percent;

    // 繁殖条（下蛋动物 CompEggLayer / 怀孕 Hediff_Pregnant）：产蛋进度或怀孕（妊娠）进度。
    public static bool enableBreedingBar = true;
    public static float breedingBarHeightOffset = 0f;
    public static float breedingFontScale = 1f;
    public static Color breedingColor = DefaultBreedingColor;
    public static BarSpan breedingBarSpan = BarSpan.Column;
    public static ValueDisplayStyle breedingValueStyle = ValueDisplayStyle.Percent;

    // 疼痛条：疼痛比例（hediffSet.PainTotal 可超过 1，条按 0..1 截断显示，数值按实际显示）。
    public static bool enablePainBar = true;
    public static bool autoHidePainAtZero = true; // 无疼痛（0%）时自动隐藏（默认开启）
    public static bool invertPainFill = false;    // 反转疼痛条填充（默认按疼痛比例由空渐满，开启后按 1 - 疼痛比例由满渐空）
    public static float painBarHeightOffset = 0f;
    public static float painFontScale = 1f;
    public static Color painColor = DefaultPainColor;
    public static BarSpan painBarSpan = BarSpan.Column;
    public static ValueDisplayStyle painValueStyle = ValueDisplayStyle.Percent;

    // 膀胱条（Dubs Bad Hygiene 等模组需求，按 defName 匹配，只要有对应需求就显示）。
    public static bool enableBladderBar = true;
    public static bool invertBladderFill = false; // 反转需求比例（默认显示当前水平，开启后显示 1 - 当前水平，如 80%→20%）
    public static float bladderBarHeightOffset = 0f;
    public static float bladderFontScale = 1f;
    public static Color bladderColor = DefaultBladderColor;
    public static BarSpan bladderBarSpan = BarSpan.Column;
    public static ValueDisplayStyle bladderValueStyle = ValueDisplayStyle.Percent;

    // 卫生条（Dubs Bad Hygiene 等模组需求，按 defName 匹配，只要有对应需求就显示）。
    public static bool enableHygieneBar = true;
    public static float hygieneBarHeightOffset = 0f;
    public static float hygieneFontScale = 1f;
    public static Color hygieneColor = DefaultHygieneColor;
    public static BarSpan hygieneBarSpan = BarSpan.Column;
    public static ValueDisplayStyle hygieneValueStyle = ValueDisplayStyle.Percent;

    // 口渴条（Dubs Bad Hygiene 等模组需求，按 defName 匹配，只要有对应需求就显示）。
    public static bool enableThirstBar = true;
    public static bool invertThirstFill = false; // 反转需求比例（默认显示当前水平，开启后显示 1 - 当前水平，如 80%→20%）
    public static float thirstBarHeightOffset = 0f;
    public static float thirstFontScale = 1f;
    public static Color thirstColor = DefaultThirstColor;
    public static BarSpan thirstBarSpan = BarSpan.Column;
    public static ValueDisplayStyle thirstValueStyle = ValueDisplayStyle.Percent;

    // ==================== Pawn 条阈值标记 ====================

    // 心情条（轻度/中度/重度崩溃阈值）、血液条（各流血阶段）、疼痛条（疼痛休克阈值）
    // 的黑色 1px 竖直阈值线总开关（默认开启）。
    public static bool enableThresholdMarkers = true;

    // 阈值线高度（px）：在条底部自下而上延伸的高度
    public static float thresholdMarkerHeight = 3f;

    // ==================== 自爆阈值标记 ====================

    public static bool enableExplosiveThreshold = true;
    public static bool thinExplosiveMarker = false;
    public static Color explosiveColor = DefaultExplosiveColor;

    // ==================== 额外检查文字 ====================

    // 在检查面板信息文字末尾追加额外文本，按对象类别分组开关（普通对象细分到单项）：
    public static bool enableExtraNormalObjectText = true;   // 普通对象：总开关
    public static bool enableExtraFactionText = true;        // 普通对象：所属势力
    public static bool hideFactionPlayerText = true;        // 普通对象：不显示玩家所属势力
    public static bool hideFactionNoneText = true;           // 普通对象：不显示无所属势力
    public static bool enableExtraNaturalGoodwillText = true; // 世界地图对象：自然阵营关系（NaturalGoodwill）
    public static bool enableExtraFactionDefLabelText = false; // 世界地图对象：派系定居点的派系类别（factionDef.label，置于原版文字之前）
    public static bool enableExtraMarketValueText = true;    // 普通对象：市场价值
    public static bool enableExtraWeaponText = true;         // 普通对象-武器：总开关（武器相关设置统一归入此开关下）
    public static bool enableExtraWeaponDamageText = true;   // 普通对象-武器：单次伤害
    public static bool enableExtraWeaponRangeText = true;    // 普通对象-武器：射程（远程）
    public static bool enableExtraWeaponCooldownText = true; // 普通对象-武器：冷却时间
    public static bool enableExtraWeaponDpsText = true;      // 普通对象-武器：DPS
    public static bool enableExtraWeaponArmorPenText = true; // 普通对象-武器：护甲穿透
    public static bool enableExtraApparelText = true;        // 普通对象-衣物：总开关
    public static bool enableExtraApparelLayerText = true;   // 普通对象-衣物：覆盖服装层
    public static bool enableExtraApparelSharpArmorText = true; // 普通对象-衣物：锐器护甲
    public static bool enableExtraApparelBluntArmorText = true; // 普通对象-衣物：钝器护甲
    public static bool enableExtraApparelHeatArmorText = true;  // 普通对象-衣物：热能护甲
    public static bool enableExtraNutritionText = true;      // 普通对象-食物：营养值
    public static bool enableExtraBeautyText = true;         // 普通对象：美观
    public static bool enableExtraComfortText = true;        // 普通对象：舒适度（床等，StatDef.Comfort）
    public static bool enableExtraCeHeightText = true;       // 普通对象：CE 高度（Combat Extended 激活时显示）
    public static bool enableExtraPawnText = true;           // Pawn：额外信息文字总开关（下辖 特性 / CE 高度 / 收藏统计）
    public static bool enableExtraPawnTraitText = true;      // Pawn：特性
    public static bool enableExtraCeHeightPawnText = true;   // Pawn：CE 高度（随 Pawn 额外信息总开关嵌套显示）
    public static bool enableExtraMapTileText = true;        // 世界地图空地砖：靠近污染 / 靠近其它势力

    // Info Card Plus 收藏 Stat 兼容（仅 Thing；Pawn 由 enableExtraPawnText + enableInfoCardPlusPinsPawn 控制；Info Card Plus 未安装时不生效）。
    public static bool enableInfoCardPlusPins = true;                    // 总开关
    public static bool enableInfoCardPlusPinsPawn = true;                // Pawn：对 Pawn 加入收藏统计
    public static bool enableInfoCardPlusPinsHighlight = true;           // 高亮收藏 Stat 名称标签（加粗 + 黄色）
    public static InfoCardPlusPinnedPosition infoCardPlusPinnedPosition = InfoCardPlusPinnedPosition.Before; // 位置（默认最前）

    // ==================== 通用颜色（健康条分级与条背景）====================

    public static Color fullColor = DefaultFullColor;
    public static Color midColor = DefaultMidColor;
    public static Color lowColor = DefaultLowColor;
    public static Color emptyColor = DefaultEmptyColor;

    // ==================== 分页 UI 状态（不持久化）====================

    private static SettingsPage currentPage;
    private static readonly Vector2[] pageScrollPositions = new Vector2[Enum.GetValues(typeof(SettingsPage)).Length];

    // 各页内容高度缓存（与 SettingsPage 枚举顺序一致）：每页绘制结束后记录实测内容高度（末行底部 + 底部留白），
    // 下一帧滚动视图用它精确限定滚动范围，替代原先每页硬编码的高度。初始值沿用原硬编码的宽松估计，首帧行为不变。
    private static readonly float[] pageContentHeights =
    {
        1400f,  // General
        2900f,  // Bars
        900f,   // MultiSelect
        1600f,  // Colors
        800f,   // Effects
    };

    // ==================== 颜色项说明与重置 ====================

    /// <summary>单个可调颜色项的说明：显示条件（null 恒显示）、读写委托、默认值、标签翻译键。</summary>
    private readonly struct ColorSetting
    {
        public readonly Func<bool>? shown; // null 表示恒显示
        public readonly Func<Color> get;
        public readonly Action<Color> set;
        public readonly Color defaultValue;
        public readonly string labelKey;

        public ColorSetting(Func<bool>? shown, Func<Color> get, Action<Color> set, Color defaultValue, string labelKey)
        {
            this.shown = shown;
            this.get = get;
            this.set = set;
            this.defaultValue = defaultValue;
            this.labelKey = labelKey;
        }
    }

    /// <summary>所有可调颜色项（颜色页显示与「重置为默认」共用同一份清单）。</summary>
    private static readonly ColorSetting[] AllColorSettings =
    {
        // 通用颜色（健康条分级与条背景），恒显示。
        new ColorSetting(null, () => fullColor, c => fullColor = c, DefaultFullColor, "BetterInspectPane.ColorFull"),
        new ColorSetting(null, () => midColor, c => midColor = c, DefaultMidColor, "BetterInspectPane.ColorMid"),
        new ColorSetting(null, () => lowColor, c => lowColor = c, DefaultLowColor, "BetterInspectPane.ColorLow"),
        new ColorSetting(null, () => emptyColor, c => emptyColor = c, DefaultEmptyColor, "BetterInspectPane.ColorEmpty"),
        // 各条颜色：对应条开启时显示。
        new ColorSetting(() => enableShieldBar, () => shieldColor, c => shieldColor = c, DefaultShieldColor, "BetterInspectPane.ColorShield"),
        new ColorSetting(() => enableAmmoBar, () => ammoColor, c => ammoColor = c, DefaultAmmoColor, "BetterInspectPane.ColorAmmo"),
        new ColorSetting(() => enableFreshnessBar, () => freshnessColor, c => freshnessColor = c, DefaultFreshnessColor, "BetterInspectPane.ColorFreshness"),
        new ColorSetting(() => enableWorkBar, () => workColor, c => workColor = c, DefaultWorkColor, "BetterInspectPane.ColorWork"),
        new ColorSetting(() => enableGrowthBar, () => growthColor, c => growthColor = c, DefaultGrowthColor, "BetterInspectPane.ColorGrowth"),
        new ColorSetting(() => enableResearchBar, () => researchColor, c => researchColor = c, DefaultResearchColor, "BetterInspectPane.ColorResearch"),
        new ColorSetting(() => enableBatteryBar, () => batteryColor, c => batteryColor = c, DefaultBatteryColor, "BetterInspectPane.ColorBattery"),
        new ColorSetting(() => enablePowerGridBar, () => powerGridColor, c => powerGridColor = c, DefaultPowerGridColor, "BetterInspectPane.ColorPowerGrid"),
        new ColorSetting(() => enableMaintenanceBar, () => maintenanceColor, c => maintenanceColor = c, DefaultMaintenanceColor, "BetterInspectPane.ColorMaintenance"),
        new ColorSetting(() => enablePipeNetBar, () => pipeNetColor, c => pipeNetColor = c, DefaultPipeNetColor, "BetterInspectPane.ColorPipeNet"),
        new ColorSetting(() => enableCooldownBar, () => cooldownColor, c => cooldownColor = c, DefaultCooldownColor, "BetterInspectPane.ColorCooldown"),
        new ColorSetting(() => enableChargeBar, () => chargeColor, c => chargeColor = c, DefaultChargeColor, "BetterInspectPane.ColorCharge"),
        // Pawn 条颜色（对应条开启时显示）。
        new ColorSetting(() => enableBloodBar, () => bloodColor, c => bloodColor = c, DefaultBloodColor, "BetterInspectPane.ColorBlood"),
        new ColorSetting(() => enableMoodBar, () => moodColor, c => moodColor = c, DefaultMoodColor, "BetterInspectPane.ColorMood"),
        new ColorSetting(() => enableFoodBar, () => foodColor, c => foodColor = c, DefaultFoodColor, "BetterInspectPane.ColorFood"),
        new ColorSetting(() => enableRestBar, () => restColor, c => restColor = c, DefaultRestColor, "BetterInspectPane.ColorRest"),
        new ColorSetting(() => enableJoyBar, () => joyColor, c => joyColor = c, DefaultJoyColor, "BetterInspectPane.ColorJoy"),
        new ColorSetting(() => enableMechEnergyBar, () => mechEnergyColor, c => mechEnergyColor = c, DefaultMechEnergyColor, "BetterInspectPane.ColorMechEnergy"),
        // 动物条颜色（对应条开启时显示）。
        new ColorSetting(() => enableMilkBar, () => milkColor, c => milkColor = c, DefaultMilkColor, "BetterInspectPane.ColorMilk"),
        new ColorSetting(() => enableWoolBar, () => woolColor, c => woolColor = c, DefaultWoolColor, "BetterInspectPane.ColorWool"),
        new ColorSetting(() => enableBreedingBar, () => breedingColor, c => breedingColor = c, DefaultBreedingColor, "BetterInspectPane.ColorBreeding"),
        new ColorSetting(() => enablePainBar, () => painColor, c => painColor = c, DefaultPainColor, "BetterInspectPane.ColorPain"),
        // Dubs Bad Hygiene 需求条颜色（对应条开启时显示）。
        new ColorSetting(() => enableBladderBar, () => bladderColor, c => bladderColor = c, DefaultBladderColor, "BetterInspectPane.ColorBladder"),
        new ColorSetting(() => enableHygieneBar, () => hygieneColor, c => hygieneColor = c, DefaultHygieneColor, "BetterInspectPane.ColorHygiene"),
        new ColorSetting(() => enableThirstBar, () => thirstColor, c => thirstColor = c, DefaultThirstColor, "BetterInspectPane.ColorThirst"),
        // Pawn 技能条颜色（技能区开启时显示）。
        new ColorSetting(() => pawnShowSkills, () => pawnSkillColor, c => pawnSkillColor = c, DefaultSkillColor, "BetterInspectPane.ColorSkill"),
        new ColorSetting(() => enableTimeRemainingBar, () => timeRemainingColor, c => timeRemainingColor = c, DefaultTimeRemainingColor, "BetterInspectPane.ColorTimeRemaining"),
        new ColorSetting(() => enableGrowingBar, () => growingColor, c => growingColor = c, DefaultGrowingColor, "BetterInspectPane.ColorGrowing"),
        new ColorSetting(() => enableZoneGrowthBar, () => zoneGrowthColor, c => zoneGrowthColor = c, DefaultZoneGrowthColor, "BetterInspectPane.ColorZoneGrowth"),
        new ColorSetting(() => enableStorageBar, () => storageColor, c => storageColor = c, DefaultStorageColor, "BetterInspectPane.ColorStorage"),
        new ColorSetting(() => enableFishingBar, () => fishingColor, c => fishingColor = c, DefaultFishingColor, "BetterInspectPane.ColorFishing"),
        new ColorSetting(() => enableExplosiveThreshold, () => explosiveColor, c => explosiveColor = c, DefaultExplosiveColor, "BetterInspectPane.ColorExplosiveThreshold"),
        // 多选网格默认单元格边框颜色（多选网格开启时显示）。
        new ColorSetting(() => enableMultiSelectGrid, () => multiSelectCellBorderColor, c => multiSelectCellBorderColor = c, DefaultMultiSelectBorderColor, "BetterInspectPane.ColorMultiSelectBorder"),
        // 多选网格健康背景颜色（「使用健康背景替代健康条」开启时显示）。
        new ColorSetting(() => useHealthBackgroundInsteadOfBar, () => healthBackgroundColor, c => healthBackgroundColor = c, DefaultHealthBackgroundColor, "BetterInspectPane.ColorHealthBackground"),
    };

    /// <summary>把所有颜色恢复为默认值（遍历 AllColorSettings 清单）。</summary>
    private static void ResetColorsToDefault()
    {
        for (int i = 0; i < AllColorSettings.Length; i++)
        {
            AllColorSettings[i].set(AllColorSettings[i].defaultValue);
        }
    }

    // ==================== 序列化 ====================

    public override void ExposeData()
    {
        base.ExposeData();

        // 基础开关。
        Scribe_Values.Look(ref enabled, "enabled", true);
        Scribe_Values.Look(ref enablePawns, "enablePawns", true);
        Scribe_Values.Look(ref enablePawnPolicyRow, "enablePawnPolicyRow", true);
        Scribe_Values.Look(ref overrideInspectPane, "overrideInspectPane", false);

        // 多选单位选择网格。
        Scribe_Values.Look(ref enableMultiSelectGrid, "enableMultiSelectGrid", true);
        Scribe_Values.Look(ref enableMultiSelectGridZones, "enableMultiSelectGridZones", true);
        Scribe_Values.Look(ref enableMultiSelectGridWorldObjects, "enableMultiSelectGridWorldObjects", true);
        Scribe_Values.Look(ref multiSelectGridCellSize, "multiSelectGridCellSize", 48f);
        Scribe_Values.Look(ref multiSelectGridRows, "multiSelectGridRows", 2);
        Scribe_Values.Look(ref multiSelectGridBackgroundExact, "multiSelectGridBackgroundExact", false);
        Scribe_Values.Look(ref multiSelectCellGap, "multiSelectCellGap", 4f);
        Scribe_Values.Look(ref multiSelectCellBorderWidth, "multiSelectCellBorderWidth", 1);
        LookColor(ref multiSelectCellBorderColor, "multiSelectCellBorderColor", DefaultMultiSelectBorderColor);
        Scribe_Values.Look(ref multiSelectCountAbbreviation, "multiSelectCountAbbreviation", true);
        Scribe_Values.Look(ref multiSelectBadgeShowX, "multiSelectBadgeShowX", true);
        Scribe_Values.Look(ref multiSelectBadgeFontSize, "multiSelectBadgeFontSize", 11);
        Scribe_Values.Look(ref multiSelectBadgeScale, "multiSelectBadgeScale", 1f);
        Scribe_Values.Look(ref useShiftKeyForMultiSelect, "useShiftKeyForMultiSelect", false);
        Scribe_Values.Look(ref useCtrlKeyForMultiCancel, "useCtrlKeyForMultiCancel", false);
        Scribe_Values.Look(ref expandSingleGroupPawn, "expandSingleGroupPawn", true);
        Scribe_Values.Look(ref expandSingleGroupNonPawn, "expandSingleGroupNonPawn", false);
        Scribe_Values.Look(ref expandSingleGroupNonThing, "expandSingleGroupNonThing", true);
        Scribe_Values.Look(ref enableMultiSelectHealthBar, "enableMultiSelectHealthBar", true);
        Scribe_Values.Look(ref multiSelectHealthBarHeight, "multiSelectHealthBarHeight", 3f);
        Scribe_Values.Look(ref hideMultiSelectFullHealthBarPawn, "hideMultiSelectFullHealthBarPawn", false);
        Scribe_Values.Look(ref hideMultiSelectFullHealthBarNonPawn, "hideMultiSelectFullHealthBarNonPawn", true);
        Scribe_Values.Look(ref useHealthBackgroundInsteadOfBar, "useHealthBackgroundInsteadOfBar", false);
        Scribe_Values.Look(ref invertHealthBackground, "invertHealthBackground", true);
        LookColor(ref healthBackgroundColor, "healthBackgroundColor", DefaultHealthBackgroundColor);
        Scribe_Values.Look(ref enableMultiSelectMapIndicator, "enableMultiSelectMapIndicator", true);

        // 多选检查面板尺寸。
        Scribe_Values.Look(ref multiSelectOverrideMinWidth, "multiSelectOverrideMinWidth", false);
        Scribe_Values.Look(ref multiSelectMinWidth, "multiSelectMinWidth", 432f);
        Scribe_Values.Look(ref multiSelectOverrideMinHeight, "multiSelectOverrideMinHeight", false);
        Scribe_Values.Look(ref multiSelectMinHeight, "multiSelectMinHeight", 170f);
        Scribe_Values.Look(ref multiSelectAdaptiveHeight, "multiSelectAdaptiveHeight", true);

        // 选择行为（杂项）。
        Scribe_Values.Look(ref enableSelectionLimitModify, "enableSelectionLimitModify", false);
        Scribe_Values.Look(ref selectionLimit, "selectionLimit", 200);
        Scribe_Values.Look(ref enableImprovedShiftSelection, "enableImprovedShiftSelection", false);
        Scribe_Values.Look(ref enableCtrlSelection, "enableCtrlSelection", false);
        Scribe_Values.Look(ref rememberOpenTab, "rememberOpenTab", false);

        // 检查面板整体边距与标题字号。
        Scribe_Values.Look(ref paneMargin, "paneMargin", 8f);
        Scribe_Values.Look(ref titleFontSize, "titleFontSize", 23);
        Scribe_Values.Look(ref pawnShowFullName, "pawnShowFullName", false);
        Scribe_Values.Look(ref pawnShowExtraButtons, "pawnShowExtraButtons", false);
        Scribe_Values.Look(ref pawnTitleFontSize, "pawnTitleFontSize", 23);

        // Pawn 条悬浮提示（技能条与需求条统一开关）。
        Scribe_Values.Look(ref pawnBarTooltips, "pawnBarTooltips", true);

        // Pawn 技能区。
        Scribe_Values.Look(ref pawnShowSkills, "pawnShowSkills", true);
        Scribe_Values.Look(ref pawnSkillColumns, "pawnSkillColumns", PawnSkillColumns.Six);
        Scribe_Values.Look(ref pawnSkillFontSize, "pawnSkillFontSize", 12);
        Scribe_Values.Look(ref pawnSkillBarHeight, "pawnSkillBarHeight", 13f);
        Scribe_Values.Look(ref pawnSkillColumnGap, "pawnSkillColumnGap", 1f);
        Scribe_Values.Look(ref pawnSkillSpacing, "pawnSkillSpacing", 2f);
        LookColor(ref pawnSkillColor, "pawnSkillColor", DefaultSkillColor);
        Scribe_Values.Look(ref pawnSkillWhiteFont, "pawnSkillWhiteFont", true);
        Scribe_Values.Look(ref pawnSkillTotalProgress, "pawnSkillTotalProgress", false);
        Scribe_Values.Look(ref pawnSkillPassionStyle, "pawnSkillPassionStyle", SkillPassionStyle.Text);
        Scribe_Values.Look(ref pawnSkillPassionIconAfterName, "pawnSkillPassionIconAfterName", false);
        Scribe_Values.Look(ref pawnSkillPlainText, "pawnSkillPlainText", false);
        Scribe_Values.Look(ref pawnSkillShowProgressText, "pawnSkillShowProgressText", false);

        // 进度条通用外观。
        Scribe_Values.Look(ref defaultBarHeight, "defaultBarHeight", 14f);
        Scribe_Values.Look(ref showSeparators, "showSeparators", true);
        Scribe_Values.Look(ref separatorHeight, "separatorHeight", 1f);
        Scribe_Values.Look(ref rowSpacing, "rowSpacing", 3f);

        // 标签/数值固定宽度。
        Scribe_Values.Look(ref labelWidth, "labelWidth", 64f);
        Scribe_Values.Look(ref valueWidth, "valueWidth", 80f);

        // 标签/数值的条外显示样式（嵌入/隐藏）。
        Scribe_Values.Look(ref labelStyle, "labelStyle", LabelStyle.Embedded);
        Scribe_Values.Look(ref barValueStyle, "barValueStyle", BarValueStyle.Embedded);
        Scribe_Values.Look(ref embedInsetLabel, "embedInsetLabel", 4f);
        Scribe_Values.Look(ref embedInsetValue, "embedInsetValue", 4f);

        // 字号。
        Scribe_Values.Look(ref labelFontSize, "labelFontSize", 14);
        Scribe_Values.Look(ref valueFontSize, "valueFontSize", 14);
        Scribe_Values.Look(ref infoFontSize, "infoFontSize", 14);
        Scribe_Values.Look(ref policyRowFontSize, "policyRowFontSize", 14);
        Scribe_Values.Look(ref policyRowHeight, "policyRowHeight", 16f);

        // 检查面板尺寸。
        Scribe_Values.Look(ref paneWidth, "paneWidth", 432f);
        Scribe_Values.Look(ref paneHeight, "paneHeight", 170f);

        // 覆盖 InspectString 绘制与自适应面板高度（requiredPaneHeight 为运行时值，不持久化）。
        Scribe_Values.Look(ref overrideInspectString, "overrideInspectString", true);
        Scribe_Values.Look(ref adaptivePaneHeight, "adaptivePaneHeight", true);

        // 布局（旧版仅有两列开关，加载旧档案时把 twoColumnLayout=true 迁移为两列布局）。
        Scribe_Values.Look(ref layoutMode, "layoutMode", LayoutMode.Single);
        bool legacyTwoColumnLayout = false;
        Scribe_Values.Look(ref legacyTwoColumnLayout, "twoColumnLayout", false);
        if (legacyTwoColumnLayout && layoutMode == LayoutMode.Single)
        {
            layoutMode = LayoutMode.Two;
        }
        Scribe_Values.Look(ref columnGap, "columnGap", 4f);
        Scribe_Values.Look(ref evenSplitTwoBarRows, "evenSplitTwoBarRows", false);

        // Pawn 面板独立布局。
        Scribe_Values.Look(ref pawnLayoutMode, "pawnLayoutMode", LayoutMode.Two);
        Scribe_Values.Look(ref pawnColumnGap, "pawnColumnGap", 4f);
        Scribe_Values.Look(ref pawnEvenSplitTwoBarRows, "pawnEvenSplitTwoBarRows", false);

        // 缓动效果。
        Scribe_Values.Look(ref enableEaseEffect, "enableEaseEffect", true);
        Scribe_Values.Look(ref easeSpeed, "easeSpeed", 0.2f);
        Scribe_Values.Look(ref easingStyle, "easingStyle", EasingStyle.Linear);

        // 健康条。
        Scribe_Values.Look(ref healthBarHeightOffset, "healthBarHeightOffset", 2f);
        Scribe_Values.Look(ref healthFontScale, "healthFontScale", 1f);
        Scribe_Values.Look(ref healthBarSpan, "healthBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref healthValueStyle, "healthValueStyle", ValueDisplayStyle.Numeric);
        Scribe_Values.Look(ref showInfiniteDurabilityBar, "showInfiniteDurabilityBar", false);

        // 护盾条。
        Scribe_Values.Look(ref enableShieldBar, "enableShieldBar", true);
        Scribe_Values.Look(ref autoHideShieldAtZero, "autoHideShieldAtZero", true);
        Scribe_Values.Look(ref shieldBarHeightOffset, "shieldBarHeightOffset", 0f);
        Scribe_Values.Look(ref shieldFontScale, "shieldFontScale", 1f);
        LookColor(ref shieldColor, "shieldColor", DefaultShieldColor);
        Scribe_Values.Look(ref shieldBarSpan, "shieldBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref shieldValueStyle, "shieldValueStyle", ValueDisplayStyle.Numeric);

        // 弹药条。
        Scribe_Values.Look(ref enableAmmoBar, "enableAmmoBar", true);
        Scribe_Values.Look(ref ammoBarHeightOffset, "ammoBarHeightOffset", 0f);
        Scribe_Values.Look(ref ammoFontScale, "ammoFontScale", 1f);
        LookColor(ref ammoColor, "ammoColor", DefaultAmmoColor);
        Scribe_Values.Look(ref ammoBarSpan, "ammoBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref ammoValueStyle, "ammoValueStyle", ValueDisplayStyle.Numeric);

        // 新鲜条。
        Scribe_Values.Look(ref enableFreshnessBar, "enableFreshnessBar", true);
        Scribe_Values.Look(ref freshnessBarHeightOffset, "freshnessBarHeightOffset", 0f);
        Scribe_Values.Look(ref freshnessFontScale, "freshnessFontScale", 1f);
        LookColor(ref freshnessColor, "freshnessColor", DefaultFreshnessColor);
        Scribe_Values.Look(ref freshnessBarSpan, "freshnessBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref freshnessValueStyle, "freshnessValueStyle", ValueDisplayStyle.Numeric);

        // 工作条。
        Scribe_Values.Look(ref enableWorkBar, "enableWorkBar", true);
        Scribe_Values.Look(ref workBarHeightOffset, "workBarHeightOffset", 0f);
        Scribe_Values.Look(ref workFontScale, "workFontScale", 1f);
        LookColor(ref workColor, "workColor", DefaultWorkColor);
        Scribe_Values.Look(ref workBarSpan, "workBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref workValueStyle, "workValueStyle", ValueDisplayStyle.Numeric);

        // 生长条。
        Scribe_Values.Look(ref enableGrowthBar, "enableGrowthBar", true);
        Scribe_Values.Look(ref growthBarHeightOffset, "growthBarHeightOffset", 0f);
        Scribe_Values.Look(ref growthFontScale, "growthFontScale", 1f);
        LookColor(ref growthColor, "growthColor", DefaultGrowthColor);
        Scribe_Values.Look(ref growthBarSpan, "growthBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref growthValueStyle, "growthValueStyle", ValueDisplayStyle.Numeric);

        // 研究条。
        Scribe_Values.Look(ref enableResearchBar, "enableResearchBar", true);
        Scribe_Values.Look(ref autoHideResearchAtHundred, "autoHideResearchAtHundred", true);
        Scribe_Values.Look(ref researchBarHeightOffset, "researchBarHeightOffset", 0f);
        Scribe_Values.Look(ref researchFontScale, "researchFontScale", 1f);
        LookColor(ref researchColor, "researchColor", DefaultResearchColor);
        Scribe_Values.Look(ref researchBarSpan, "researchBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref researchValueStyle, "researchValueStyle", ValueDisplayStyle.Numeric);

        // 世界地图对象。
        Scribe_Values.Look(ref enableWorldObjects, "enableWorldObjects", true);

        // 剩余时间条（世界地图对象）。
        Scribe_Values.Look(ref enableTimeRemainingBar, "enableTimeRemainingBar", true);
        Scribe_Values.Look(ref timeRemainingBarHeightOffset, "timeRemainingBarHeightOffset", 0f);
        Scribe_Values.Look(ref timeRemainingFontScale, "timeRemainingFontScale", 1f);
        LookColor(ref timeRemainingColor, "timeRemainingColor", DefaultTimeRemainingColor);
        Scribe_Values.Look(ref timeRemainingBarSpan, "timeRemainingBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref timeRemainingValueStyle, "timeRemainingValueStyle", ValueDisplayStyle.Numeric);

        // 阵营关系条（世界地图对象）。
        Scribe_Values.Look(ref enableFactionRelationBar, "enableFactionRelationBar", true);
        Scribe_Values.Look(ref factionRelationBarHeightOffset, "factionRelationBarHeightOffset", 0f);
        Scribe_Values.Look(ref factionRelationFontScale, "factionRelationFontScale", 1f);
        Scribe_Values.Look(ref factionRelationBarSpan, "factionRelationBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref factionRelationValueStyle, "factionRelationValueStyle", ValueDisplayStyle.Numeric);
        Scribe_Values.Look(ref showRelationThresholdMarker, "showRelationThresholdMarker", true);

        // 强度条（World Domination 2）。
        Scribe_Values.Look(ref enableStrengthBar, "enableStrengthBar", true);
        Scribe_Values.Look(ref strengthBarHeightOffset, "strengthBarHeightOffset", 0f);
        Scribe_Values.Look(ref strengthFontScale, "strengthFontScale", 1f);
        LookColor(ref strengthColor, "strengthColor", DefaultStrengthColor);
        Scribe_Values.Look(ref strengthBarSpan, "strengthBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref strengthValueStyle, "strengthValueStyle", ValueDisplayStyle.Numeric);

        // 区域。
        Scribe_Values.Look(ref enableZones, "enableZones", true);

        // 已种植条（种植区）。
        Scribe_Values.Look(ref enableGrowingBar, "enableGrowingBar", true);
        Scribe_Values.Look(ref growingBarHeightOffset, "growingBarHeightOffset", 0f);
        Scribe_Values.Look(ref growingFontScale, "growingFontScale", 1f);
        LookColor(ref growingColor, "growingColor", DefaultGrowingColor);
        Scribe_Values.Look(ref growingBarSpan, "growingBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref growingValueStyle, "growingValueStyle", ValueDisplayStyle.Numeric);

        // 总体生长条（种植区）。
        Scribe_Values.Look(ref enableZoneGrowthBar, "enableZoneGrowthBar", true);
        Scribe_Values.Look(ref zoneGrowthBarHeightOffset, "zoneGrowthBarHeightOffset", 0f);
        Scribe_Values.Look(ref zoneGrowthFontScale, "zoneGrowthFontScale", 1f);
        LookColor(ref zoneGrowthColor, "zoneGrowthColor", DefaultZoneGrowthColor);
        Scribe_Values.Look(ref zoneGrowthBarSpan, "zoneGrowthBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref zoneGrowthValueStyle, "zoneGrowthValueStyle", ValueDisplayStyle.Numeric);

        // 剩余容量条（储存区）。
        Scribe_Values.Look(ref enableStorageBar, "enableStorageBar", true);
        Scribe_Values.Look(ref storageBarHeightOffset, "storageBarHeightOffset", 0f);
        Scribe_Values.Look(ref storageFontScale, "storageFontScale", 1f);
        LookColor(ref storageColor, "storageColor", DefaultStorageColor);
        Scribe_Values.Look(ref storageBarSpan, "storageBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref storageValueStyle, "storageValueStyle", ValueDisplayStyle.Numeric);

        // 剩余鱼条（钓鱼区）。
        Scribe_Values.Look(ref enableFishingBar, "enableFishingBar", true);
        Scribe_Values.Look(ref fishingBarHeightOffset, "fishingBarHeightOffset", 0f);
        Scribe_Values.Look(ref fishingFontScale, "fishingFontScale", 1f);
        LookColor(ref fishingColor, "fishingColor", DefaultFishingColor);
        Scribe_Values.Look(ref fishingBarSpan, "fishingBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref fishingValueStyle, "fishingValueStyle", ValueDisplayStyle.Numeric);
        Scribe_Values.Look(ref showFishingTargetPctMarker, "showFishingTargetPctMarker", true);

        // 电池蓄电条（电池）。
        Scribe_Values.Look(ref enableBatteryBar, "enableBatteryBar", true);
        Scribe_Values.Look(ref batteryBarHeightOffset, "batteryBarHeightOffset", 0f);
        Scribe_Values.Look(ref batteryFontScale, "batteryFontScale", 1f);
        LookColor(ref batteryColor, "batteryColor", DefaultBatteryColor);
        Scribe_Values.Look(ref batteryBarSpan, "batteryBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref batteryValueStyle, "batteryValueStyle", ValueDisplayStyle.Numeric);

        // 电网蓄电条（电力设备）。
        Scribe_Values.Look(ref enablePowerGridBar, "enablePowerGridBar", true);
        Scribe_Values.Look(ref powerGridBarHeightOffset, "powerGridBarHeightOffset", 0f);
        Scribe_Values.Look(ref powerGridFontScale, "powerGridFontScale", 1f);
        LookColor(ref powerGridColor, "powerGridColor", DefaultPowerGridColor);
        Scribe_Values.Look(ref powerGridBarSpan, "powerGridBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref powerGridValueStyle, "powerGridValueStyle", ValueDisplayStyle.Numeric);

        // 维护条（Vanilla Gravship Expanded）。
        Scribe_Values.Look(ref enableMaintenanceBar, "enableMaintenanceBar", true);
        Scribe_Values.Look(ref maintenanceBarHeightOffset, "maintenanceBarHeightOffset", 0f);
        Scribe_Values.Look(ref maintenanceFontScale, "maintenanceFontScale", 1f);
        LookColor(ref maintenanceColor, "maintenanceColor", DefaultMaintenanceColor);
        Scribe_Values.Look(ref maintenanceBarSpan, "maintenanceBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref maintenanceValueStyle, "maintenanceValueStyle", ValueDisplayStyle.PercentTwoDecimals);

        // 管道网络条（Vanilla Expanded Framework 的管道系统）。
        Scribe_Values.Look(ref useResourceColor, "useResourceColor", true);
        Scribe_Values.Look(ref enablePipeNetBar, "enablePipeNetBar", true);
        Scribe_Values.Look(ref pipeNetBarHeightOffset, "pipeNetBarHeightOffset", 0f);
        Scribe_Values.Look(ref pipeNetFontScale, "pipeNetFontScale", 1f);
        LookColor(ref pipeNetColor, "pipeNetColor", DefaultPipeNetColor);
        Scribe_Values.Look(ref pipeNetBarSpan, "pipeNetBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref pipeNetValueStyle, "pipeNetValueStyle", ValueDisplayStyle.Numeric);

        // 冷却条（迫击炮）。
        Scribe_Values.Look(ref enableCooldownBar, "enableCooldownBar", true);
        Scribe_Values.Look(ref cooldownBarHeightOffset, "cooldownBarHeightOffset", 0f);
        Scribe_Values.Look(ref cooldownFontScale, "cooldownFontScale", 1f);
        LookColor(ref cooldownColor, "cooldownColor", DefaultCooldownColor);
        Scribe_Values.Look(ref cooldownBarSpan, "cooldownBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref cooldownValueStyle, "cooldownValueStyle", ValueDisplayStyle.Numeric);
        Scribe_Values.Look(ref invertCooldownFill, "invertCooldownFill", false);
        Scribe_Values.Look(ref autoHideCooldownAtHundred, "autoHideCooldownAtHundred", true);
        Scribe_Values.Look(ref minCooldownSeconds, "minCooldownSeconds", 3f);

        // 充能条（飞船反应堆 / 机械孕育器 / 定时激活器 / 清污泵 / 排水泵）。
        Scribe_Values.Look(ref enableChargeBar, "enableChargeBar", true);
        Scribe_Values.Look(ref autoHideChargeAtHundred, "autoHideChargeAtHundred", true);
        Scribe_Values.Look(ref chargeBarHeightOffset, "chargeBarHeightOffset", 0f);
        Scribe_Values.Look(ref chargeFontScale, "chargeFontScale", 1f);
        LookColor(ref chargeColor, "chargeColor", DefaultChargeColor);
        Scribe_Values.Look(ref chargeBarSpan, "chargeBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref chargeValueStyle, "chargeValueStyle", ValueDisplayStyle.Numeric);

        // Pawn 条（Pawn 单选检查面板）。
        // Pawn 健康条（独立于物品/建筑健康条设置）。
        Scribe_Values.Look(ref pawnHealthBarHeightOffset, "pawnHealthBarHeightOffset", 0f);
        Scribe_Values.Look(ref pawnHealthFontScale, "pawnHealthFontScale", 1f);
        Scribe_Values.Look(ref pawnHealthBarSpan, "pawnHealthBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref pawnHealthValueStyle, "pawnHealthValueStyle", ValueDisplayStyle.Percent);

        // 血液条。
        Scribe_Values.Look(ref enableBloodBar, "enableBloodBar", true);
        Scribe_Values.Look(ref autoHideBloodAtFull, "autoHideBloodAtFull", true);
        Scribe_Values.Look(ref bloodBarHeightOffset, "bloodBarHeightOffset", 0f);
        Scribe_Values.Look(ref bloodFontScale, "bloodFontScale", 1f);
        LookColor(ref bloodColor, "bloodColor", DefaultBloodColor);
        Scribe_Values.Look(ref bloodBarSpan, "bloodBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref bloodValueStyle, "bloodValueStyle", ValueDisplayStyle.Numeric);

        // 心情条。
        Scribe_Values.Look(ref enableMoodBar, "enableMoodBar", true);
        Scribe_Values.Look(ref moodBarHeightOffset, "moodBarHeightOffset", 0f);
        Scribe_Values.Look(ref moodFontScale, "moodFontScale", 1f);
        LookColor(ref moodColor, "moodColor", DefaultMoodColor);
        Scribe_Values.Look(ref moodBarSpan, "moodBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref moodValueStyle, "moodValueStyle", ValueDisplayStyle.Percent);

        // 食物条。
        Scribe_Values.Look(ref enableFoodBar, "enableFoodBar", true);
        Scribe_Values.Look(ref foodBarHeightOffset, "foodBarHeightOffset", 0f);
        Scribe_Values.Look(ref foodFontScale, "foodFontScale", 1f);
        LookColor(ref foodColor, "foodColor", DefaultFoodColor);
        Scribe_Values.Look(ref foodBarSpan, "foodBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref foodValueStyle, "foodValueStyle", ValueDisplayStyle.Percent);

        // 休息条。
        Scribe_Values.Look(ref enableRestBar, "enableRestBar", true);
        Scribe_Values.Look(ref restBarHeightOffset, "restBarHeightOffset", 0f);
        Scribe_Values.Look(ref restFontScale, "restFontScale", 1f);
        LookColor(ref restColor, "restColor", DefaultRestColor);
        Scribe_Values.Look(ref restBarSpan, "restBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref restValueStyle, "restValueStyle", ValueDisplayStyle.Percent);

        // 娱乐条。
        Scribe_Values.Look(ref enableJoyBar, "enableJoyBar", true);
        Scribe_Values.Look(ref joyBarHeightOffset, "joyBarHeightOffset", 0f);
        Scribe_Values.Look(ref joyFontScale, "joyFontScale", 1f);
        LookColor(ref joyColor, "joyColor", DefaultJoyColor);
        Scribe_Values.Look(ref joyBarSpan, "joyBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref joyValueStyle, "joyValueStyle", ValueDisplayStyle.Percent);

        // 机械能量条。
        Scribe_Values.Look(ref enableMechEnergyBar, "enableMechEnergyBar", true);
        Scribe_Values.Look(ref mechEnergyBarHeightOffset, "mechEnergyBarHeightOffset", 0f);
        Scribe_Values.Look(ref mechEnergyFontScale, "mechEnergyFontScale", 1f);
        LookColor(ref mechEnergyColor, "mechEnergyColor", DefaultMechEnergyColor);
        Scribe_Values.Look(ref mechEnergyBarSpan, "mechEnergyBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref mechEnergyValueStyle, "mechEnergyValueStyle", ValueDisplayStyle.Percent);

        // 产物条（可挤奶动物）。
        Scribe_Values.Look(ref enableMilkBar, "enableMilkBar", true);
        Scribe_Values.Look(ref milkBarHeightOffset, "milkBarHeightOffset", 0f);
        Scribe_Values.Look(ref milkFontScale, "milkFontScale", 1f);
        LookColor(ref milkColor, "milkColor", DefaultMilkColor);
        Scribe_Values.Look(ref milkBarSpan, "milkBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref milkValueStyle, "milkValueStyle", ValueDisplayStyle.Percent);

        // 产毛条（可剪毛动物）。
        Scribe_Values.Look(ref enableWoolBar, "enableWoolBar", true);
        Scribe_Values.Look(ref woolBarHeightOffset, "woolBarHeightOffset", 0f);
        Scribe_Values.Look(ref woolFontScale, "woolFontScale", 1f);
        LookColor(ref woolColor, "woolColor", DefaultWoolColor);
        Scribe_Values.Look(ref woolBarSpan, "woolBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref woolValueStyle, "woolValueStyle", ValueDisplayStyle.Percent);

        // 繁殖条（下蛋 / 怀孕）。
        Scribe_Values.Look(ref enableBreedingBar, "enableBreedingBar", true);
        Scribe_Values.Look(ref breedingBarHeightOffset, "breedingBarHeightOffset", 0f);
        Scribe_Values.Look(ref breedingFontScale, "breedingFontScale", 1f);
        LookColor(ref breedingColor, "breedingColor", DefaultBreedingColor);
        Scribe_Values.Look(ref breedingBarSpan, "breedingBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref breedingValueStyle, "breedingValueStyle", ValueDisplayStyle.Percent);

        // 疼痛条。
        Scribe_Values.Look(ref enablePainBar, "enablePainBar", true);
        Scribe_Values.Look(ref autoHidePainAtZero, "autoHidePainAtZero", true);
        Scribe_Values.Look(ref invertPainFill, "invertPainFill", false);
        Scribe_Values.Look(ref painBarHeightOffset, "painBarHeightOffset", 0f);
        Scribe_Values.Look(ref painFontScale, "painFontScale", 1f);
        LookColor(ref painColor, "painColor", DefaultPainColor);
        Scribe_Values.Look(ref painBarSpan, "painBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref painValueStyle, "painValueStyle", ValueDisplayStyle.Percent);

        // 膀胱条（Dubs Bad Hygiene 等模组需求）。
        Scribe_Values.Look(ref enableBladderBar, "enableBladderBar", true);
        Scribe_Values.Look(ref invertBladderFill, "invertBladderFill", false);
        Scribe_Values.Look(ref bladderBarHeightOffset, "bladderBarHeightOffset", 0f);
        Scribe_Values.Look(ref bladderFontScale, "bladderFontScale", 1f);
        LookColor(ref bladderColor, "bladderColor", DefaultBladderColor);
        Scribe_Values.Look(ref bladderBarSpan, "bladderBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref bladderValueStyle, "bladderValueStyle", ValueDisplayStyle.Percent);

        // 卫生条（Dubs Bad Hygiene 等模组需求）。
        Scribe_Values.Look(ref enableHygieneBar, "enableHygieneBar", true);
        Scribe_Values.Look(ref hygieneBarHeightOffset, "hygieneBarHeightOffset", 0f);
        Scribe_Values.Look(ref hygieneFontScale, "hygieneFontScale", 1f);
        LookColor(ref hygieneColor, "hygieneColor", DefaultHygieneColor);
        Scribe_Values.Look(ref hygieneBarSpan, "hygieneBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref hygieneValueStyle, "hygieneValueStyle", ValueDisplayStyle.Percent);

        // 口渴条（Dubs Bad Hygiene 等模组需求）。
        Scribe_Values.Look(ref enableThirstBar, "enableThirstBar", true);
        Scribe_Values.Look(ref invertThirstFill, "invertThirstFill", false);
        Scribe_Values.Look(ref thirstBarHeightOffset, "thirstBarHeightOffset", 0f);
        Scribe_Values.Look(ref thirstFontScale, "thirstFontScale", 1f);
        LookColor(ref thirstColor, "thirstColor", DefaultThirstColor);
        Scribe_Values.Look(ref thirstBarSpan, "thirstBarSpan", BarSpan.Column);
        Scribe_Values.Look(ref thirstValueStyle, "thirstValueStyle", ValueDisplayStyle.Percent);

        // 显示顺序（列表下标 = 顺序）。加载后把各默认成员补齐，新加入的条自动排到队尾。
        Scribe_Collections.Look(ref mainBarOrder, "mainBarOrder", LookMode.Value);
        mainBarOrder ??= new List<BarType>();
        Scribe_Collections.Look(ref worldObjectBarOrder, "worldObjectBarOrder", LookMode.Value);
        worldObjectBarOrder ??= new List<BarType>();
        Scribe_Collections.Look(ref zoneBarOrder, "zoneBarOrder", LookMode.Value);
        zoneBarOrder ??= new List<BarType>();
        Scribe_Collections.Look(ref pawnBarOrder, "pawnBarOrder", LookMode.Value);
        pawnBarOrder ??= new List<BarType>();
        EnsureOrderListValid(mainBarOrder, DefaultMainBarOrder);
        EnsureOrderListValid(worldObjectBarOrder, DefaultWorldObjectBarOrder);
        EnsureOrderListValid(zoneBarOrder, DefaultZoneBarOrder);
        EnsureOrderListValid(pawnBarOrder, DefaultPawnBarOrder);

        // 白色字体开关（按条，HashSet 仅存开启的条）。
        Scribe_Collections.Look(ref barWhiteFont, "barWhiteFont", LookMode.Value);
        EnsureWhiteFontReady();

        // Pawn 条阈值标记。
        Scribe_Values.Look(ref enableThresholdMarkers, "enableThresholdMarkers", true);
        Scribe_Values.Look(ref thresholdMarkerHeight, "thresholdMarkerHeight", 3f);

        // 自爆阈值标记。
        Scribe_Values.Look(ref enableExplosiveThreshold, "enableExplosiveThreshold", true);
        Scribe_Values.Look(ref thinExplosiveMarker, "thinExplosiveMarker", false);
        LookColor(ref explosiveColor, "explosiveColor", DefaultExplosiveColor);

        // 额外检查文字。
        Scribe_Values.Look(ref enableExtraNormalObjectText, "enableExtraNormalObjectText", true);
        Scribe_Values.Look(ref enableExtraFactionText, "enableExtraFactionText", true);
        Scribe_Values.Look(ref hideFactionPlayerText, "hideFactionPlayerText", true);
        Scribe_Values.Look(ref hideFactionNoneText, "hideFactionNoneText", true);
        Scribe_Values.Look(ref enableExtraNaturalGoodwillText, "enableExtraNaturalGoodwillText", true);
        Scribe_Values.Look(ref enableExtraFactionDefLabelText, "enableExtraFactionDefLabelText", false);
        Scribe_Values.Look(ref enableExtraMarketValueText, "enableExtraMarketValueText", true);
        Scribe_Values.Look(ref enableExtraWeaponText, "enableExtraWeaponText", true);
        Scribe_Values.Look(ref enableExtraWeaponDamageText, "enableExtraWeaponDamageText", true);
        Scribe_Values.Look(ref enableExtraWeaponRangeText, "enableExtraWeaponRangeText", true);
        Scribe_Values.Look(ref enableExtraWeaponCooldownText, "enableExtraWeaponCooldownText", true);
        Scribe_Values.Look(ref enableExtraWeaponDpsText, "enableExtraWeaponDpsText", true);
        Scribe_Values.Look(ref enableExtraWeaponArmorPenText, "enableExtraWeaponArmorPenText", true);
        Scribe_Values.Look(ref enableExtraApparelText, "enableExtraApparelText", true);
        Scribe_Values.Look(ref enableExtraApparelLayerText, "enableExtraApparelLayerText", true);
        Scribe_Values.Look(ref enableExtraApparelSharpArmorText, "enableExtraApparelSharpArmorText", true);
        Scribe_Values.Look(ref enableExtraApparelBluntArmorText, "enableExtraApparelBluntArmorText", true);
        Scribe_Values.Look(ref enableExtraApparelHeatArmorText, "enableExtraApparelHeatArmorText", true);
        Scribe_Values.Look(ref enableExtraNutritionText, "enableExtraNutritionText", true);
        Scribe_Values.Look(ref enableExtraBeautyText, "enableExtraBeautyText", true);
        Scribe_Values.Look(ref enableExtraComfortText, "enableExtraComfortText", true);
        Scribe_Values.Look(ref enableExtraCeHeightText, "enableExtraCeHeightText", true);
        Scribe_Values.Look(ref enableExtraPawnText, "enableExtraPawnText", true);
        Scribe_Values.Look(ref enableExtraPawnTraitText, "enableExtraPawnTraitText", true);
        Scribe_Values.Look(ref enableExtraCeHeightPawnText, "enableExtraCeHeightPawnText", true);
        Scribe_Values.Look(ref enableExtraMapTileText, "enableExtraMapTileText", true);

        // Info Card Plus 收藏 Stat 兼容。
        Scribe_Values.Look(ref enableInfoCardPlusPins, "enableInfoCardPlusPins", true);
        Scribe_Values.Look(ref enableInfoCardPlusPinsPawn, "enableInfoCardPlusPinsPawn", true);
        Scribe_Values.Look(ref enableInfoCardPlusPinsHighlight, "enableInfoCardPlusPinsHighlight", true);
        Scribe_Values.Look(ref infoCardPlusPinnedPosition, "infoCardPlusPinnedPosition", InfoCardPlusPinnedPosition.Before);

        // 通用颜色。
        LookColor(ref fullColor, "fullColor", DefaultFullColor);
        LookColor(ref midColor, "midColor", DefaultMidColor);
        LookColor(ref lowColor, "lowColor", DefaultLowColor);
        LookColor(ref emptyColor, "emptyColor", DefaultEmptyColor);
    }

    /// <summary>以 HTML 十六进制字符串形式读写颜色（颜色无法直接用 Scribe_Values 序列化）。
    /// 注意 ToHtmlStringRGBA 输出不带 '#'，而 TryParseHtmlString 只把以 '#' 开头的字符串当作十六进制解析，
    /// 否则会当作颜色名（如 "red"），因此必须手动补上 '#' 前缀才能正确读回。</summary>
    private static void LookColor(ref Color color, string label, Color defaultColor)
    {
        string text = "#" + ColorUtility.ToHtmlStringRGBA(color);
        Scribe_Values.Look(ref text, label, "#" + ColorUtility.ToHtmlStringRGBA(defaultColor));
        if (ColorUtility.TryParseHtmlString(text, out Color parsed))
        {
            color = parsed;
        }
        else
        {
            color = defaultColor;
        }
    }

}