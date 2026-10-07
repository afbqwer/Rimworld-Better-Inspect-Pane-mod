using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using HarmonyLib;
using static ASQBetterInspectPane.MyModTemplateSettings;

namespace ASQBetterInspectPane;

/// <summary>
/// 检查面板的自定义重绘：接管 InspectPaneFiller.DoPaneContentsFor，
/// 按设置绘制健康 / 护盾 / 弹药 / 新鲜 / 工作 / 生长进度条、分隔线与右下角设置按钮。
/// 未启用重绘或选中 Pawn 时保持原版绘制（Prefix 返回 true）。
/// 本文件为条的绘制部分；数据计算（条数据 / 数值文字 / 护盾 / 缓动 / 缓存）见 InspectPanePatch.Logic.cs
/// </summary>
[HarmonyPatch(typeof(InspectPaneFiller), "DoPaneContentsFor")]
[StaticConstructorOnStartup]
public static partial class InspectPanePatch
{
    #region 常量与共享缓存字段

    private static readonly Color SeparatorColor = new Color(0.4f, 0.4f, 0.4f);

    // 右下角设置按钮（图标按钮，样式同 RimHUD）：边距与按钮边长。
    private const float SettingsButtonMargin = 2f;
    private const float SettingsButtonHeight = 16f;

    // 设置按钮图标材质
    private static Texture2D? SettingsIconTex = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGeneral", true);
        
    // 缓动状态：按条标识缓存 EaseState，切换选中单位时整体重置（见 Logic.cs #region 缓动）。

    // 自爆阈值缓存（-1 = 未计算，选中单位切换时重置）。
    private static int cachedExplosiveThreshold = -1;

    // 拦截器激活刻（private 字段）。
    private static readonly FieldInfo ActivatedTickField =
        typeof(CompProjectileInterceptor).GetField("activatedTick",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 扫描组件累计扫描天数（CompScanner.daysWorkingSinceLastFinding 为 protected 字段，反射读取；
    // 地质扫描仪 CompDeepScanner 与远距离矿物扫描仪 CompLongRangeMineralScanner 共用此字段）。
    private static readonly FieldInfo ScannerDaysWorkedField =
        typeof(CompScanner).GetField("daysWorkingSinceLastFinding",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 酿酒桶理论剩余刻数（Building_FermentingBarrel.EstimatedTicksLeft 为 private 属性，反射读取）。
    // 酿酒桶的进度仅在使用 tickRare 的每约 250 刻刷新一次，相邻两刻采样无法测速且跨隔采样会严重偏小，
    // 因此不回退到采样估算，而直接取原版按当前进度与温度速度计算的理论剩余刻数。
    private static readonly PropertyInfo FermentingBarrelEstimatedTicksProp =
        typeof(Building_FermentingBarrel).GetProperty("EstimatedTicksLeft",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 塑形舱当前周期剩余刻数（CompBiosculpterPod.currentCycleTicksRemaining 为 private 字段，反射读取；
    // 总时长 = CurrentCycle.Props.durationDays × 60000，进度口径与原版 CompTick 进度条一致）。
    private static readonly FieldInfo BiosculpterTicksRemainingField =
        typeof(CompBiosculpterPod).GetField("currentCycleTicksRemaining",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 次核扫描仪剩余制造刻数（Building_SubcoreScanner.fabricationTicksLeft 为 private 字段，反射读取；
    // 总时长 = def.building.subcoreScannerTicks，进度口径与原版 Tick 进度条一致）。
    private static readonly FieldInfo SubcoreScannerFabricationTicksField =
        typeof(Building_SubcoreScanner).GetField("fabricationTicksLeft",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 基因提取器剩余刻数（Building_GeneExtractor.ticksRemaining 为 private 字段，反射读取；
    // TryAcceptPawn 置 30000 后每 tick 递减，进度口径与原版进度条（1 - ticksRemaining/30000）一致）。
    private static readonly FieldInfo GeneExtractorTicksRemainingField =
        typeof(Building_GeneExtractor).GetField("ticksRemaining",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 炮塔开火冷却剩余刻数（Building_TurretGun.burstCooldownTicksLeft 为 protected 字段，反射读取；
    // 迫击炮开火后/刚建造时进入冷却，进度口径与原版进度条（1 - 剩余/总冷却）一致）。
    private static readonly FieldInfo TurretCooldownTicksField =
        typeof(Building_TurretGun).GetField("burstCooldownTicksLeft",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 树精茧完成时刻（CompDryadHolder.tickComplete 为 protected 字段，反射读取；
    // TryAcceptPawn 时置为 当前刻 + 60000 × daysToComplete，进度口径与原版 InspectString 的 TimeLeft 一致）。
    private static readonly FieldInfo DryadHolderTickCompleteField =
        typeof(CompDryadHolder).GetField("tickComplete",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 下蛋进度（CompEggLayer.eggProgress 为 private 字段，反射读取；进度口径与原版
    // CompInspectStringExtra 的 EggProgress 一致，1 = 可以下蛋/已满）。
    private static readonly FieldInfo EggLayerProgressField =
        typeof(CompEggLayer).GetField("eggProgress",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 飞船反应堆休眠启动结束刻（CompHibernatable.endStartupTick 为 private 字段，反射读取；
    // 用于反应堆启动过程的充能进度，总时长 = Props.startupDays × 60000）。
    private static readonly FieldInfo HibernatableEndStartupTickField =
        typeof(CompHibernatable).GetField("endStartupTick",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 清污泵距下一次抽污刻数（CompPollutionPump.ticksUntilPump 为 private 字段，反射读取；
    // 用于抽污循环的充能进度，总时长 = Props.intervalTicks）。
    private static readonly FieldInfo PollutionPumpTicksUntilPumpField =
        typeof(CompPollutionPump).GetField("ticksUntilPump",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 排水泵排干进度刻数（CompTerrainPump.progressTicks 为 private 字段，反射读取；
    // 用于排干半径扩展的充能进度，总时长 = ((CompProperties_TerrainPump)props).daysToRadius × 60000）。
    private static readonly FieldInfo TerrainPumpProgressTicksField =
        typeof(CompTerrainPump).GetField("progressTicks",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // 额外文字滚动位置（InspectPaneFiller 的 private static 字段，接管 DrawInspectString 绘制时使用）。
    internal static readonly FieldInfo InspectStringScrollPosField =
        typeof(InspectPaneFiller).GetField("inspectStringScrollPos",
            BindingFlags.Static | BindingFlags.NonPublic);

    // Comp 缓存：GetComp 走类型匹配，面板每帧重绘时开销较高；
    // 检查面板同一时间只显示一个单位，选中单位切换时整体重建。
    // 「管制」标签页（MainButtonDef defName = Schedule，tabWindowClass = MainTabWindow_Schedule），首次点击时解析并缓存。
    private static MainButtonDef? cachedScheduleTab;
    private static Thing? cachedCompOwner;
    private static CompProjectileInterceptor? cachedInterceptor;
    private static CompDestroyAfterDelay? cachedDestroyAfterDelay; // 自毁计时组件（被动常开且会自毁的拦截器，如护盾投射仪）
    private static CompRefuelable? cachedRefuelable;
    private static CompRottable? cachedRottable;
    private static CompExplosive? cachedExplosive;
    private static CompPowerBattery? cachedBattery; // 电池：自身蓄电量
    private static CompPower? cachedPower;          // 电力组件：定位所连电网（电池亦为 CompPower）
    private static ThingComp? cachedGravMaintainable; // 维护组件（Vanilla Gravship Expanded，反射读取）
    private static ThingComp? cachedVefProcessor;     // VEF 处理系统组件（Vanilla Expanded Framework，反射读取）
    private static ThingComp? cachedPipeResource;     // VFE 管道网络组件（PipeSystem.CompResource 及其派生，反射读取）
    private static CompScanner? cachedScanner;        // 扫描组件（CompScanner 基类：地质 / 远距离矿物扫描仪，进度走反射读取）
    private static CompDeepDrill? cachedDeepDrill;         // 深钻井：挖掘进度（ProgressToNextPortionPercent）
    private static CompBiosculpterPod? cachedBiosculpter;  // 塑形舱：当前周期进度（State / currentCycleTicksRemaining）
    private static CompAtomizer? cachedAtomizer;           // 垃圾分解器原子组件（WastepackAtomizer：整批分解进度）
    private static Building_TurretGun? cachedTurretGun;    // 选中炮塔（迫击炮冷却条，反射读取 burstCooldownTicksLeft）
    private static CompHibernatable? cachedHibernatable;          // 飞船反应堆：休眠启动充能（State / endStartupTick）
    private static CompSpawnerPawn? cachedSpawnerPawn;            // 机械孕育器：机械装配充能（nextPawnSpawnTick）
    private static CompSendSignalOnCountdown? cachedCountdown;    // 定时激活器：倒计时充能（ticksLeft）
    private static CompPollutionPump? cachedPollutionPump;        // 清污泵：抽污循环充能（ticksUntilPump）
    private static CompTerrainPumpDry? cachedTerrainPumpDry;      // 排水泵：排干半径扩展充能（progressTicks）
    private static Building_GravEngine? cachedGravEngine;         // 逆重飞船引擎（原版 GravEngine / Vanilla Gravship Expanded）：起飞冷却充能（cooldownCompleteTick，公开字段）
    private static CompPilotConsole? cachedPilotConsole;          // 驾驶控制台（Odyssey）：显示所连引擎的起飞冷却充能（engine.cooldownCompleteTick）
    private static ThingComp? cachedHeatsink;                     // 散热器（Vanilla Gravship Expanded，CompHeatsink 反射读取）：冷却条蓄热（ActualStoredHeat / CachedStats.maxHeat）
    private static CompDryadHolder? cachedDryadHolder;            // 树精茧类建筑（CompDryadHolder 基类匹配）：daysToComplete 蜕变进度走生长条（tickComplete 反射读取）

    // 动物产物组件（仅 Pawn 有，产物/产毛/繁殖条数据）：选中 Pawn 时缓存。
    private static CompMilkable? cachedMilkable;   // 产物条：奶水充盈（Fullness 0..1）
    private static CompShearable? cachedShearable; // 产毛条：羊毛生长（Fullness 0..1）
    private static CompEggLayer? cachedEggLayer;   // 繁殖条：产蛋进度（eggProgress，private 反射读取）

    // Pawn 模组需求缓存（Dubs Bad Hygiene 等，按 NeedDef.defName 匹配；不硬依赖该模组）。
    // DBH 可能有多个组件、难以按 mod 判断，因此直接从对应 Pawn 的需求列表里按 defName 找：
    // 只要 Pawn 有对应需求就显示对应条。需求随 Pawn 生成后基本稳定，选中 Pawn 切换时一次性缓存。
    private static Pawn? cachedNeedOwner;
    private static Need? cachedBladderNeed; // defName "Bladder"（Dubs Bad Hygiene：膀胱/排泄）
    private static Need? cachedHygieneNeed; // defName "Hygiene"（Dubs Bad Hygiene：卫生）
    private static Need? cachedThirstNeed;  // defName "DBHThirst"（Dubs Bad Hygiene：口渴/饮水）

    // 自主工作台（Fortified.Building_WorkTableAutonomous，如 Machine Printer）剩余工作量基数缓存：
    // 该建筑不直接暴露 0..1 进度，工作条需要「初始剩余量」作为总量基数来计算填充；
    // 由于仅缓存剩余量数组，首次观察到该建筑或进入新配方时以当前剩余量作为基数（取观察最大值）。
    private static Thing? cachedAutoWorkOwner;
    private static float cachedAutoWorkTotal;

    // 充能条总量基数（观察最大值）缓存：机械孕育器 / 定时激活器不暴露精确总量，
    // 以首次观察到该建筑时的剩余量为基数，剩余量回升（进入新周期）时更新。
    private static Thing? cachedChargeOwner;
    private static float cachedChargeTotal;

    // 帧数缓存
    // 区域/储存统计每 30 帧重算一次（选中对象切换时立即重算）。
    private const int CacheRefreshIntervalFrames = 30;
    private static int currentFrame;

    // 区域统计缓存（种植/钓鱼）：种植/钓鱼两条都涉及整片区域遍历
    // （GetPlant / FishPopulationAt），按帧缓存。
    private static Zone? cachedZoneOwner;
    private static int cachedZoneRefreshFrame = -1000;
    private static int cachedGrowingPlanted;   // 种植区：已种植（与设定作物一致）的格数
    private static int cachedGrowingTotal;     // 种植区：总格数
    private static float cachedZoneGrowth = -1f; // 种植区：已种植作物的平均生长水平（-1 = 无作物）
    private static int cachedZoneGrowthCount;    // 种植区：参与平均的作物数（0 = 无作物）
    private static int cachedFishingRemaining; // 钓鱼区：水区剩余鱼数（该水域当前鱼类种群，四舍五入取整）
    private static int cachedFishingTarget;    // 钓鱼区：目标数量

    // 树区静态上限（贪心独立集）的封锁集合：估算某种植区最多能隔格种下的树数时，
    // 每放一个虚拟可种位即把其 8 邻加入集合；每次重算前清空复用，避免每帧分配。
    private static readonly HashSet<IntVec3> spacingBlockedCells = new HashSet<IntVec3>();

    // 树区动态口径的「邻格阻挡」缓存：某种植区各格位是否阻挡相邻播种
    // （plant.blockAdjacentSow 或已播种植物，与 PlantUtility.AdjacentSowBlocker 判定一致）。
    // 供计算「当前可播种空格」时复用——每格只取一次 GetPlant，避免对每格的 8 邻再各取一次植物；
    // TryGetValue 失败即该邻格不在本区域内，退回直接取植物（处理区域外阻挡）。每次重算前清空复用。
    private static readonly Dictionary<IntVec3, bool> sowBlockMap = new Dictionary<IntVec3, bool>();

    // 储存容量统计缓存（储存区与储存容器建筑共用）：SpaceRemaining / HeldThingsCount
    // 都会遍历所属格位，按帧缓存。
    private static ISelectable? cachedStorageOwner;
    private static int cachedStorageRefreshFrame = -1000;
    private static int cachedStorageUsed;  // 已用堆叠数（储存区为已用格数）
    private static int cachedStorageTotal; // 总容量（储存区为格数，建筑为 maxItemsInCell × 占地格数，书架为 MaximumBooks）

    // 电网蓄电统计缓存（电力设备的电网蓄电条）：CurrentStoredEnergy 与各电池容量总和
    // 都涉及整网遍历，按帧缓存（选中对象切换时立即重算）。
    private static PowerNet? cachedGridOwner;
    private static int cachedGridRefreshFrame = -1000;
    private static float cachedGridStored; // 电网当前蓄电量（Wd）
    private static float cachedGridMax;    // 电网蓄电上限（Wd，全网电池容量之和）

    // 进度采样缓存（工作条 / 研究条共用）：只在观察到「进展」时测量，以「测速窗口起点 →
    // 当前进展点」计算每刻进度（剩余量下降量 / 窗口刻数），并对其做 EMA 作为速度估计。
    //   - 跨整窗口测速是因为 1.6 用 TickInterval(delta) 把工作量按 delta 批量施加（每
    //     UpdateRateTicks 刻才推进一次，该间隔随镜头缩放为 1..15，优化 mod 还可能更大），
    //     逐刻点采样会把批量尖峰误当常速而高估速率；
    //   - 停顿判定用「相对间隔」（本次间隔远大于上次）而非固定刻阈值，兼容任意 TickInterval；
    //   - 窗口还能按起点剩余量的相对阈值自适应加长（研究条），把「大数相减」的 float 分辨率
    //     噪声压到可忽略；工作条增量大，阈值传 0 即每段测速；
    //   - shownTicks 保存显示值：默认「只减不增」（真实剩余时间随进度单调不增），抑制噪声回升；
    //     仅当估计值持续偏高达到滞回窗口时才确认「真实变慢」并一次性回升，故既稳又能反映变慢，
    //     同时避免在「时间」与「剩余量」格式之间来回闪烁。
    // 工作条随选中物体切换清空；研究条是全局状态，不随选中切换清空（项目变化时单独清）。
    private struct ProgressSample
    {
        public Thing? owner;       // 所属物体（检查面板同一时间只显示一个单位，切换时整体重置）
        public int lastTick;       // 上一次采样的游戏刻（默认 0 表示尚未采样，首次调用由 AddProgressSample 建立基准）
        public float lastLeft;     // 上一次采样的剩余量（同刻重绘时仅刷新此值）
        public int segTick;        // 当前测速窗口起点刻
        public float segLeft;      // 当前测速窗口起点的剩余量
        public int progTick;       // 最近一次观察到进展的游戏刻
        public int lastGap;        // 上次进展间隔（刻），用于相对判定停顿（不依赖固定刻阈值）
        public bool segValid;      // 窗口起点是否可用（刚重置 / 刚从停顿恢复时为 false，下一个进展点仅作起点）
        public int activeTicks;      // 已参与平均的游戏刻数（EMA 预热，封顶本条的 smoothTicks）
        public int shownTicks;       // 已显示的最短剩余刻数（-1 = 无）；默认只减不增，真实变慢需确认后才回升
        public int raiseSinceTick;   // 估计值持续高于显示值的起始刻（0 = 未在计数）；用于变慢回升的滞回确认
        public int raiseConfirmTicks; // 本条允许「回升」的确认刻数（= max(RemainingRaiseConfirmTicks, smoothTicks)）
        public float avgRate;        // 每刻剩余量下降速度的指数滑动平均
    }
    private static ProgressSample workSample;
    private static ProgressSample researchSample;
    private static ResearchProjectDef? cachedResearchProject; // 研究条：上一帧的当前项目，用于变化时清空显示缓存

    #endregion

    #region Harmony 入口

    public static bool Prefix(ISelectable sel, Rect rect)
    {
        // Prefix 开头记录当前帧数：区域/储存统计缓存按每 30 帧重算一次（刻速总高于帧率，按刻缓存会每帧失效）。
        currentFrame = Time.frameCount;

        if (!ShouldDrawCustomPane(sel, out Thing? thing, out WorldObject? worldObject, out Zone? zone))
        {
            return true;
        }

        // 组件读取统一用 ThingWithComps，切换选中单位时一次性缓存（GetComp 每帧重绘开销较高）。
        if (thing != null)
        {
            RefreshCompCache(thing, thing as ThingWithComps);
            // 电网蓄电统计（电力设备的电网蓄电条）按帧缓存（涉及整网遍历）。
            RefreshGridCache(cachedPower?.PowerNet);
        }
        // Pawn 模组需求条（Dubs Bad Hygiene 等）按需缓存：选中 Pawn 时重建（需求列表查询成本极低）。
        if (thing is Pawn cachePawn)
        {
            RefreshPawnNeedCache(cachePawn);
        }
        // 区域统计按游戏刻缓存（重绘每帧进行，区域统计涉及整片区域遍历）。
        if (zone != null)
        {
            RefreshZoneCache(zone);
        }
        // 储存容量统计（储存区 / 储存容器建筑 / 书架）按 (选中对象, 游戏刻) 缓存。
        if (sel is Zone_Stockpile || sel is Building_Storage || sel is Building_Bookcase)
        {
            RefreshStorageCache(sel);
        }

        Widgets.BeginGroup(rect);
        try
        {
            // (1) top separator: name/title -> bars.
            float rowSpacing = MyModTemplateSettings.rowSpacing;
            float y = DrawSeparator(rect.width, rowSpacing) + rowSpacing;

            // (1.5) Pawn 专用：策略行（食物 / 管制 / 着装 / 区域，自行绘制的四组件）。
            // 组件均不适用（或开关关闭）时不预留任何高度与间距（方法内直接返回原 y）；
            // 行尾自带与进度条区之间的分隔线。
            if (thing is Pawn pawn && enablePawnPolicyRow)
            {
                y = DrawPawnPolicyRow(rect, pawn, y, out bool policyRowDrawn);
                if (policyRowDrawn)
                {
                    y += rowSpacing;
                }
            }

            // (2)-(6) 进度条区：收集可见条并按显示顺序排布（可选两列布局）。
            // Thing 走原有条，世界地图对象走剩余时间 / 阵营关系条，区域走已种植 / 容量 / 鱼条。
            y = thing != null
                ? DrawBarBlock(rect, thing, y, out bool anyBars)
                : (worldObject != null
                    ? DrawWorldObjectBarBlock(rect, worldObject, y, out anyBars)
                    : DrawZoneBarBlock(rect, zone!, y, out anyBars));

            // (6.5) Pawn 技能区：在普通条区域下方（以分隔线相隔）绘制多列技能等级条。
            bool skillDrawn = false;
            if (thing is Pawn skillPawn)
            {
                y = DrawPawnSkillBlock(rect, skillPawn, y, out skillDrawn);
            }

            // (7) bottom separator: bars/skills -> info. 若无任何条可显示，则不绘制该分隔线。
            if (anyBars || skillDrawn)
            {
                y = DrawSeparator(rect.width, y);
            }

            // (8) vanilla inspect string below. 直接铺满剩余高度，设置按钮绘制在其上（通常不会重叠）。
            Rect info = rect.AtZero();
            info.yMin = y;
            info.yMax = rect.height;
            // 开启「覆盖 InspectString 绘制」时用本模组自己的实现（帧缓存 + 自适应高度）。
            if (overrideInspectString)
            {
                DrawInspectStringFor(sel, info);
            }
            else
            {
                InspectPaneFiller.DrawInspectStringFor(sel, info);
            }

            // (9) 右下角设置按钮：游戏进行中打开独立设置窗口（图标按钮）。
            if (Mouse.IsOver(rect))
            {
                Rect settingsBtn = new Rect(
                    rect.width - SettingsButtonHeight - SettingsButtonMargin,
                    rect.height - SettingsButtonHeight - SettingsButtonMargin,
                    SettingsButtonHeight, SettingsButtonHeight);
                if (Widgets.ButtonImage(settingsBtn, SettingsIconTex))
                {
                    Find.WindowStack.Add(new Dialog_MyModSettings());
                }
                TooltipHandler.TipRegion(settingsBtn, "BetterInspectPane.OpenSettingsTip".Translate());
            }
        }
        finally
        {
            Widgets.EndGroup();
        }
        return false;
    }

    #endregion

    #region 绘制管线

    internal static bool ShouldDrawCustomPane(ISelectable? sel,
        out Thing? thing, out WorldObject? worldObject, out Zone? zone)
    {
        thing = sel as Thing;
        worldObject = sel as WorldObject;
        zone = sel as Zone;
        if (!enabled)
        {
            return false;
        }
        // Pawn：总开关开启时用自定义面板替代原版内容（默认关闭，保持原版 Pawn 面板）。
        if (thing is Pawn && enablePawns)
        {
            return true;
        }
        if (thing != null && thing is not Pawn && !thing.def.onlyShowInspectString)
        {
            return true;
        }
        if (worldObject != null && enableWorldObjects)
        {
            return true;
        }
        if (zone != null && enableZones)
        {
            return true;
        }
        return false;
    }

    /// <summary>分隔线（未启用时原样返回 y）。</summary>
    private static float DrawSeparator(float width, float y)
    {
        if (!showSeparators)
        {
            return y;
        }
        Widgets.DrawBoxSolid(new Rect(0f, y, width, separatorHeight), SeparatorColor);
        return y + separatorHeight;
    }

    /// <summary>
    /// Pawn 专用策略行：食物（食物方案）/ 管制（时间表安排）/ 着装（服装方案）/ 区域（允许区）
    /// 四个自行绘制的单行色条组件，适用者平分整行（全部不适用时不预留任何高度与间距，返回原 y，
    /// 由调用方以 drawn 判定是否补行距）。
    /// 组件均带原版等价交互：
    ///   食物 / 着装：点击浮窗选择方案 + 「编辑...」打开原版方案管理窗口（任务租客的着装显示「不可更改」且不可点击）；
    ///   管制：点击打开原版「管制」（Schedule）标签页；
    ///   区域：悬停高亮 + 地图区域标记（原版行为），点击浮出允许区域菜单。
    /// 行绘制后在其与进度条区之间补一条分隔线（跟随分隔线设置；未启用时仅返回行底）。
    /// 需在 Widgets.BeginGroup 内调用（鼠标交互依赖当前组）。
    /// </summary>
    private static float DrawPawnPolicyRow(Rect rect, Pawn pawn, float y, out bool drawn)
    {
        // 各追踪器先取局部变量：lambda 内使用时编译器无法沿用上方判空，避免可空性告警。
        Pawn_FoodRestrictionTracker? foodTracker = pawn.foodRestriction;
        Pawn_OutfitTracker? outfitTracker = pawn.outfits;
        Pawn_PlayerSettings? playerSettings = pawn.playerSettings;
        Pawn_TimetableTracker? timetable = pawn.timetable;

        // Bounded Rationality 同步：BR 隐藏原版 DrawTimetableSetting（Basic）时，
        // 同口径隐藏策略行的食物 / 管制 / 着装组件；未装 BR 时恒为已知，不影响原行为。
        bool brBasicKnown = BoundedRationalityReflection.IsBasicKnown(pawn);

        // 食物适用条件：有食物方案追踪器且有可用方案（变异体禁用方案时 CurrentFoodPolicy 为 null）。
        FoodPolicy? foodPolicy = foodTracker?.CurrentFoodPolicy;
        bool foodApplicable = foodPolicy != null && brBasicKnown;

        // 管制适用条件（原版 DoPaneContentsFor 的判定）。
        bool timetableApplicable = timetable != null && !pawn.IsPrisonerOfColony && brBasicKnown;

        // 着装适用条件：有服装追踪器且有可用方案（变异体禁用服装/方案时 CurrentApparelPolicy 为 null）。
        ApparelPolicy? outfitPolicy = outfitTracker?.CurrentApparelPolicy;
        bool outfitApplicable = outfitPolicy != null && brBasicKnown;

        // 区域适用条件：原版 DrawAreaAllowed 全部早退条件取反（SupportsAllowedAreas 为 public 属性）；
        // 同步 BR：区域可点击（Basic + Control 口径）且信息未知时一并隐藏。
        bool areaApplicable = playerSettings != null
            && playerSettings.SupportsAllowedAreas
            && pawn.Faction == Faction.OfPlayer
            && pawn.HostFaction == null
            && !(pawn.IsMutant && !pawn.mutant.Def.respectsAllowedArea)
            && BoundedRationalityReflection.IsAreaKnown(pawn);

        int count = (foodApplicable ? 1 : 0) + (timetableApplicable ? 1 : 0)
            + (outfitApplicable ? 1 : 0) + (areaApplicable ? 1 : 0);
        if (count == 0)
        {
            drawn = false;
            return y;
        }
        drawn = true;

        // 适用者平分整行；行高取设置下限（默认随字号自适应）。
        float gap = pawnColumnGap;
        float cellWidth = (rect.width - gap * (count - 1)) / count;
        float rowHeight = Mathf.Max(policyRowHeight, LineHeightForSize(policyRowFontSize));
        float x = 0f;

        if (foodApplicable)
        {
            Pawn_FoodRestrictionTracker tracker = foodTracker!;
            // 与原版 Assign 列一致：全部食物方案 + 「编辑...」打开方案管理窗口。
            DrawPolicyCell(new Rect(x, y, cellWidth, rowHeight), "BetterInspectPane.PolicyRow.Food",
                foodPolicy!.label, null, _ =>
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    foreach (FoodPolicy policy in Current.Game.foodRestrictionDatabase.AllFoodRestrictions)
                    {
                        FoodPolicy captured = policy;
                        options.Add(new FloatMenuOption(policy.label,
                            () => tracker.CurrentFoodPolicy = captured));
                    }
                    options.Add(new FloatMenuOption("AssignTabEdit".Translate(),
                        () => Find.WindowStack.Add(new Dialog_ManageFoodPolicies(tracker.CurrentFoodPolicy))));
                    Find.WindowStack.Add(new FloatMenu(options));
                });
            x += cellWidth + gap;
        }

        if (timetableApplicable)
        {
            TimeAssignmentDef assignment = timetable!.CurrentAssignment;
            DrawPolicyCell(new Rect(x, y, cellWidth, rowHeight), "BetterInspectPane.PolicyRow.Timetable",
                assignment.LabelCap, assignment.ColorTexture, _ => OpenScheduleTab());
            x += cellWidth + gap;
        }

        if (outfitApplicable)
        {
            Pawn_OutfitTracker tracker = outfitTracker!;
            // 任务租客不可更改着装（原版 Assign 列行为）：仅显示「不可更改」，不注册点击。
            bool lodger = pawn.IsQuestLodger();
            string outfitValue = lodger ? "Unchangeable".Translate() : outfitPolicy!.label;
            DrawPolicyCell(new Rect(x, y, cellWidth, rowHeight), "BetterInspectPane.PolicyRow.Outfit",
                outfitValue, null, lodger ? null : _ =>
                {
                    // 与原版 Assign 列一致：全部服装方案 + 「编辑...」打开方案管理窗口。
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    foreach (ApparelPolicy policy in Current.Game.outfitDatabase.AllOutfits)
                    {
                        ApparelPolicy captured = policy;
                        options.Add(new FloatMenuOption(policy.label,
                            () => tracker.CurrentApparelPolicy = captured));
                    }
                    options.Add(new FloatMenuOption("AssignTabEdit".Translate(),
                        () => Find.WindowStack.Add(new Dialog_ManageApparelPolicies(tracker.CurrentApparelPolicy))));
                    Find.WindowStack.Add(new FloatMenu(options));
                });
            x += cellWidth + gap;
        }

        if (areaApplicable)
        {
            Pawn_PlayerSettings settings = playerSettings!;
            Area? currentArea = settings.AreaRestrictionInPawnCurrentMap;
            Texture2D areaTex = currentArea != null ? currentArea.ColorTexture : BaseContent.GreyTex;
            DrawPolicyCell(new Rect(x, y, cellWidth, rowHeight), "BetterInspectPane.PolicyRow.Area",
                AreaUtility.AreaAllowedLabel(pawn), areaTex, _ =>
                {
                    AreaUtility.MakeAllowedAreaListFloatMenu(
                        a => settings.AreaRestrictionInPawnCurrentMap = a,
                        addNullAreaOption: true, addManageOption: true, pawn.MapHeld);
                },
                hover: () =>
                {
                    // 原版行为：悬停时在地图上标记该区域（悬停边框由 DrawPolicyCell 对所有组件统一绘制）。
                    settings.AreaRestrictionInPawnCurrentMap?.MarkForDraw();
                });
        }

        // 行尾分隔线：策略行与下方进度条区隔开（跟随分隔线设置；关闭时不绘制、仅保留行底），
        // 启用时先空出行距再画线，与行距/分隔线的整体节奏一致（调用方在 drawn 为真时再补一个 rowSpacing）。
        y += rowHeight;
        return showSeparators ? DrawSeparator(rect.width, y + rowSpacing) : y;
    }

    /// <summary>
    /// 绘制单个策略组件：单行色条（底色 / 填充纹理铺满），条内单行「标题：值」文本（可截断）。
    /// onCell 为点击回调（仅单元格被点击时触发；null 表示不可点击）；hover 为悬停附加行为
    /// （仅鼠标悬停时触发，如区域组件的地图区域标记）。
    /// 悬停时所有组件统一绘制 1px 悬停边框（画在单元格内部，四边均不越出面板被裁切）。
    /// </summary>
    private static void DrawPolicyCell(Rect cellRect, string titleKey, string value, Texture2D? fillTex,
        Action<Rect>? onCell, Action? hover = null)
    {
        string title = titleKey.Translate();
        Widgets.DrawBoxSolid(cellRect, emptyColor);
        if (fillTex != null)
        {
            Widgets.FillableBar(cellRect, 1f, fillTex, null, doBorder: false);
        }

        // 悬停：附加行为（如区域组件的地图标记）+ 1px 悬停边框（DrawBox 沿单元格四边各画 1px，不外扩）。
        if (Mouse.IsOver(cellRect))
        {
            hover?.Invoke();
            Widgets.DrawBox(cellRect);
        }
        var textRect = new Rect(cellRect.x + 4f, cellRect.y - 4f, cellRect.width - 4f , cellRect.height + 8f);
        // 条内单行文本：左侧缩进 4px（textRect），增加4px高度防显示不全，像素字号作用域 + 不换行 + 垂直居中，
        // 超出按缩进后的宽度截断。
        // 冒号并入标题翻译（标题键自带全角/半角冒号），此处仅做标题 + 值 的拼接。
        string text = $"{title}{value}";
        GUI.color = Color.white;
        bool prevWordWrap = Text.WordWrap;
        Text.WordWrap = false;
        Text.Anchor = TextAnchor.MiddleLeft;
        using (new FontSizeScope(policyRowFontSize))
        {
            Widgets.Label(textRect, text.Truncate(Mathf.Max(1f, textRect.width)));
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Text.WordWrap = prevWordWrap;

        // 点击回调：仅单元格被点击时触发（不可点击组件不注册隐形按钮）。
        if (onCell != null && Widgets.ButtonInvisible(cellRect))
        {
            onCell(cellRect);
        }
        TooltipHandler.TipRegion(cellRect, text);
    }

    /// <summary>打开原版「管制」（Schedule）标签页：按 defName 解析 MainButtonDef，
    /// 缺失时回退按 tabWindowClass 扫描（tabWindowClass = MainTabWindow_Schedule）；解析失败时静默跳过。</summary>
    private static void OpenScheduleTab()
    {
        cachedScheduleTab ??= ResolveScheduleTab();
        if (cachedScheduleTab == null)
        {
            return;
        }
        Find.MainTabsRoot.SetCurrentTab(cachedScheduleTab, playSound: true);
    }

    private static MainButtonDef? ResolveScheduleTab()
    {
        MainButtonDef? def = DefDatabase<MainButtonDef>.GetNamed("Schedule", false);
        if (def != null)
        {
            return def;
        }
        foreach (MainButtonDef candidate in DefDatabase<MainButtonDef>.AllDefs)
        {
            if (candidate.tabWindowClass == typeof(MainTabWindow_Schedule))
            {
                return candidate;
            }
        }
        return null;
    }

    #endregion

    #region 布局与条绘制

    /// <summary>一行进度条的数据。</summary>
    private struct BarRowInfo
    {
        public string label;
        public string value;
        public float fill;   // 目标填充比例（0..1）
        public float eased;  // 实际绘制填充（无缓动时等于 fill）
        public float height;
        public float fontScale; // 本条字体缩放（标签与数值相对全局字号的缩放比例）
        public bool whiteFont;  // 本条白色字体：开启后标签与数值无视进度条比例，总是以白色显示
        public Color color;
        public BarSpan span;  // 多列布局下的占位（整行/单列/两列/适应）
        public BarType type;  // 条标识：缓动按条标识独立跟踪
        public Action<Rect>? onBarDrawn; // 条上额外标记（如自爆阈值）
    }

    /// <summary>
    /// 进度条区：收集当前可见条并按显示顺序排布后绘制，返回新的 y。
    /// 布局按设置（单列 / 两列 / 三列 / 四列）：单列全宽逐条排布；多列按网格排布
    /// （整行条独占一行，单列条先左后右填充，两列条占两列，适应条右侧无条时增宽填满本行）。
    /// </summary>
    private static float DrawBarBlock(Rect rect, Thing thing, float y, out bool anyBars)
    {
        // Pawn 走 Pawn 专用条（健康/血液/心情/食物/休息/娱乐/机械能量），布局用 Pawn 独立设置。
        if (thing is Pawn pawn)
        {
            List<BarRowInfo> pawnBars = CollectPawnBars(pawn);
            anyBars = pawnBars.Count > 0;
            if (pawnBars.Count == 0)
            {
                return y;
            }
            int pawnColumns = PawnLayoutColumns;
            return pawnColumns == 1
                ? DrawBarsSingleColumn(rect, pawnBars, y)
                : DrawBarsGrid(rect, pawnBars, y, pawnColumns, pawnColumnGap, pawnEvenSplitTwoBarRows);
        }

        List<BarRowInfo> bars = CollectBars(thing);
        anyBars = bars.Count > 0;
        if (bars.Count == 0)
        {
            return y;
        }
        int columns = LayoutColumns;
        return columns == 1
            ? DrawBarsSingleColumn(rect, bars, y)
            : DrawBarsGrid(rect, bars, y, columns, columnGap, evenSplitTwoBarRows);
    }

    /// <summary>
    /// 世界地图对象进度条区：收集剩余时间 / 阵营关系条并按显示顺序排布后绘制，返回新的 y。
    /// 布局（单列/两列/三列/四列）与 Thing 条共用。
    /// </summary>
    private static float DrawWorldObjectBarBlock(Rect rect, WorldObject wo, float y, out bool anyBars)
    {
        List<BarRowInfo> bars = CollectWorldObjectBars(wo);
        anyBars = bars.Count > 0;
        if (bars.Count == 0)
        {
            return y;
        }
        int columns = LayoutColumns;
        return columns == 1
            ? DrawBarsSingleColumn(rect, bars, y)
            : DrawBarsGrid(rect, bars, y, columns, columnGap, evenSplitTwoBarRows);
    }

    /// <summary>
    /// 区域进度条区：按区域类型收集对应条（已种植 / 剩余容量 / 剩余鱼）并按显示顺序排布后绘制，返回新的 y。
    /// 布局（单列/两列/三列/四列）与 Thing 条共用。
    /// </summary>
    private static float DrawZoneBarBlock(Rect rect, Zone zone, float y, out bool anyBars)
    {
        List<BarRowInfo> bars = CollectZoneBars(zone);
        anyBars = bars.Count > 0;
        if (bars.Count == 0)
        {
            return y;
        }
        int columns = LayoutColumns;
        return columns == 1
            ? DrawBarsSingleColumn(rect, bars, y)
            : DrawBarsGrid(rect, bars, y, columns, columnGap, evenSplitTwoBarRows);
    }

    /// <summary>单列布局：按顺序逐条全宽绘制，返回新的 y。</summary>
    private static float DrawBarsSingleColumn(Rect rect, List<BarRowInfo> bars, float y)
    {
        foreach (BarRowInfo bar in bars)
        {
            y = DrawBarRow(rect, y, bar);
        }
        return y;
    }

    /// <summary>
    /// 多列网格布局：按列数排布进度条，条按占位（整行/单列/两列/适应）占据相应宽度。
    /// 所有条从左到右、从上到下排列；「适应」条默认占单列，若其右侧没有条
    /// （下一个条放不进本行剩余格，或已是最后一个条）则增宽填满本行剩余列数。
    /// 三列/四列布局开启「双条行均分」时：同一行仅 1~2 个单列宽条（占不满整行）→ 该行两列平分显示；
    ///（避免「单列+适应+单列」挤在同一行）。
    /// gap 与 evenSplit 由调用方传入（普通面板与 Pawn 面板各用独立设置）。
    /// 行高取同行最大条高，短条垂直居中。返回新的 y。
    /// </summary>
    private static float DrawBarsGrid(Rect rect, List<BarRowInfo> bars, float y, int columns,
        float gap, bool evenSplit)
    {
        // 当前行缓冲：已放置的条、起始列与占宽（列数）。
        List<(BarRowInfo bar, int col, int width)> rowBars = new List<(BarRowInfo, int, int)>();
        float rowHeight = 0f;
        int col = 0;

        // 「双条行均分」仅在 3 列 / 4 列布局生效（两列布局本身即两列平分，无需再均分）。
        bool rebalanceRows = evenSplit && columns >= 3;

        for (int i = 0; i < bars.Count; i++)
        {
            BarRowInfo bar = bars[i];
            int w = SpanBaseWidth(bar.span, columns);
            // 放不下则换行（整行条在非行首时必然触发，保证独占一行）。
            if (col + w > columns)
            {
                y = DrawRow(rect, y, rowBars, rowHeight, columns, rebalanceRows, gap);
                rowBars.Clear();
                rowHeight = 0f;
                col = 0;
            }
            // 适应条：默认单列；右侧无条时按情况增宽填满本行剩余列，或保持单列与行首单列条配对成均分行。
            if (bar.span == BarSpan.Adaptive)
            {
                bool hasRightNeighbor = i + 1 < bars.Count
                    && SpanBaseWidth(bars[i + 1].span, columns) <= columns - (col + 1);
                if (hasRightNeighbor)
                {
                    // 右侧仍有可放入的条：保持单列宽（若为「双条行均分」配对场景，见下方 pairAdaptive，
                    // 会在本行放置后强制断行，把后续条挤到下一行）。
                    w = 1;
                }
                else if (col == 0)
                {
                    // 独占一行：填满整行。
                    w = columns;
                }
                else if (rebalanceRows && col == 1)
                {
                    // 「双条行均分」且行内恰有 1 个单列条：保持单列宽，与它配对成「两列均分」行
                    //（避免该行只剩一个 1/3 或 1/4 宽的窄条 + 大片空白）。
                    w = 1;
                }
                else
                {
                    // 行尾增宽填满本行剩余列数（均分行下行内已有 2 个以上条时同样填满，避免行尾留空列）。
                    w = columns - col;
                }
            }
            // 均分行配对：适应条与行内唯一的单列条（col==1）组成「两列均分」行后强制结束本行，
            // 把后续条挤到下一行（避免「单列+适应+单列」挤在同一行）。
            bool pairAdaptive = rebalanceRows && bar.span == BarSpan.Adaptive && col == 1;
            rowBars.Add((bar, col, w));
            rowHeight = Mathf.Max(rowHeight, bar.height);
            col += w;
            if (pairAdaptive)
            {
                col = columns;
            }
        }

        if (rowBars.Count > 0)
        {
            y = DrawRow(rect, y, rowBars, rowHeight, columns, rebalanceRows, gap);
        }
        return y;
    }

    /// <summary>条在网格中的基础占宽（列数）：整行=全部列，两列=2（列数不足时 1），单列/适应=1（适应是否增宽由网格算法决定）。</summary>
    private static int SpanBaseWidth(BarSpan span, int columns)
    {
        switch (span)
        {
            case BarSpan.FullRow: return columns;
            case BarSpan.TwoColumns: return columns >= 2 ? 2 : 1;
            default: return 1;
        }
    }

    /// <summary>
    /// 绘制网格布局中的一行（rowBars 为该行已放置的条、起始列与占宽），返回新的 y。
    /// gap 由调用方传入（普通面板与 Pawn 面板各用独立设置）。
    /// rebalance（三列/四列「双条行均分」）时：该行只有 1~2 个单列宽条（占不满整行）→ 两列平分显示
    ///（两个条各占一半；单个单列条占左半、右半留空），避免列空白过于零碎。
    /// </summary>
    private static float DrawRow(Rect rect, float y, List<(BarRowInfo bar, int col, int width)> rowBars,
        float rowHeight, int columns, bool rebalance, float gap)
    {
        if (rebalance && rowBars.Count <= 2)
        {
            bool allSingle = true;
            foreach ((BarRowInfo _, int _, int width) in rowBars)
            {
                if (width != 1)
                {
                    allSingle = false;
                    break;
                }
            }
            if (allSingle)
            {
                float halfW = (rect.width - gap) * 0.5f;
                for (int i = 0; i < rowBars.Count; i++)
                {
                    (BarRowInfo bar, int _, int _) = rowBars[i];
                    float x = i * (halfW + gap);
                    float barY = y + (rowHeight - bar.height) * 0.5f;
                    DrawBarRow(new Rect(x, barY, halfW, bar.height), barY, bar);
                }
                return y + rowHeight + rowSpacing;
            }
        }
        float colWidth = (rect.width - gap * (columns - 1)) / columns;
        foreach ((BarRowInfo bar, int col, int width) in rowBars)
        {
            float x = col * (colWidth + gap);
            float barWidth = width * colWidth + (width - 1) * gap;
            float barY = y + (rowHeight - bar.height) * 0.5f;
            DrawBarRow(new Rect(x, barY, barWidth, bar.height), barY, bar);
        }
        return y + rowHeight + rowSpacing;
    }

    /// <summary>
    /// 绘制一行进度条：标签 / 底色 / 缓动填充 / 数值。返回新的 y。
    /// info.onBarDrawn 可在条上叠加额外标记（如自爆阈值）。
    /// 所有 x 坐标以 rect.x 为基准，支持在两列布局的半宽 cell 中绘制。
    /// </summary>
    private static float DrawBarRow(Rect rect, float y, BarRowInfo info)
    {
        // 标签与数值宽度（可在设置中调整），保证所有行对齐一致，条随之伸缩；
        // 嵌入/隐藏时不预留条外宽度，条随之变宽（两列布局下条铺满半行单元格）；
        // 当预留总宽超过可用宽度（如两列布局的半行列）时按比例缩小，避免条宽为 0 或文字溢出。
        float labelWidth = labelStyle == LabelStyle.Embedded ? 0f : MyModTemplateSettings.labelWidth;
        float valueWidth = barValueStyle == BarValueStyle.Normal ? MyModTemplateSettings.valueWidth : 0f;
        float fixedWidth = labelWidth + valueWidth;
        if (fixedWidth > rect.width && rect.width > 0f)
        {
            float scale = rect.width / fixedWidth;
            labelWidth *= scale;
            valueWidth *= scale;
        }
        float barWidth = Mathf.Max(0f, rect.width - labelWidth - valueWidth);

        Rect barRect = new Rect(rect.x + labelWidth, y, barWidth, info.height);
        Widgets.DrawBoxSolid(barRect, emptyColor);
        DrawBarFill(barRect, info.eased, info.color);
        info.onBarDrawn?.Invoke(barRect);

        // 文字矩形比条高，垂直居中，避免小号字被条高度裁切；
        // 高度取标签/数值两路字号中较高的行高（按像素字号计算）。
        // 字号按本条字体缩放（fontScale）缩放：缩放同时作用于标签与数值。
        int labelSize = Mathf.RoundToInt(labelFontSize * info.fontScale);
        int valueSize = Mathf.RoundToInt(valueFontSize * info.fontScale);
        float textRectHeight = Mathf.Max(info.height, 24f);
        textRectHeight = Mathf.Max(textRectHeight, LineHeightForSize(labelSize));
        textRectHeight = Mathf.Max(textRectHeight, LineHeightForSize(valueSize));
        float textRectY = y + (info.height - textRectHeight) * 0.5f;

        GUI.color = Color.white;
        // 固定宽度列内文本不换行；WordWrap=false 在 RimWorld 里会把 GUIStyle 裁剪设为 Overflow，
        // 文本会溢出到条上/面板外，因此用 BeginGroup 建立裁剪区域，超宽部分截断在列边界内。
        bool prevWordWrap = Text.WordWrap;
        Text.WordWrap = false;

        // Widgets.Label 走 Text.CurFontStyle（共享样式，只支持 GameFont 三档），
        // 用 FontSizeScope 临时把字号改为像素值、绘制后还原，从而支持任意具体数值。
        // 标签：普通 → 条外固定列；嵌入 → 条内按进度反色绘制（遮罩，纵向取足行高不被条裁切，
        // 两侧按 embedInsetLabel 收缩向中心靠拢）。
        if (labelStyle == LabelStyle.Embedded)
        {
            DrawBarTextMasked(
                new Rect(barRect.x + embedInsetLabel, textRectY + 1f, Mathf.Max(1f, barRect.width - 2f * embedInsetLabel), textRectHeight),
                info.eased, info.label, TextAnchor.MiddleLeft, labelSize, embedInsetLabel, info.whiteFont);
        }
        else
        {
            Rect labelTextRect = new Rect(rect.x, textRectY, labelWidth, textRectHeight);
            Widgets.BeginGroup(labelTextRect);
            try
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                using (new FontSizeScope(labelSize))
                {
                    Widgets.Label(labelTextRect.AtZero(), info.label);
                }
            }
            finally
            {
                Widgets.EndGroup();
            }
        }

        // 数值：普通 → 条外固定列；嵌入 → 条内反色绘制（遮罩）；隐藏 → 不绘制（宽度已在上面释放）。
        if (barValueStyle == BarValueStyle.Embedded)
        {
            DrawBarTextMasked(
                new Rect(barRect.x + embedInsetValue, textRectY + 1f, Mathf.Max(1f, barRect.width - 2f * embedInsetValue), textRectHeight),
                info.eased, info.value, TextAnchor.MiddleRight, valueSize, embedInsetValue, info.whiteFont);
        }
        else if (barValueStyle == BarValueStyle.Normal)
        {
            Rect valueTextRect = new Rect(rect.x + rect.width - valueWidth, textRectY, valueWidth, textRectHeight);
            Widgets.BeginGroup(valueTextRect);
            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                using (new FontSizeScope(valueSize))
                {
                    Widgets.Label(valueTextRect.AtZero(), info.value);
                }
            }
            finally
            {
                Widgets.EndGroup();
            }
        }

        Text.WordWrap = prevWordWrap;
        Text.Anchor = TextAnchor.UpperLeft;

        return y + info.height + rowSpacing;
    }

    /// <summary>
    /// 绘制一条进度条：条本身按缓动值绘制（缓动前/后不额外绘制差量段，
    /// 条宽即当前缓动值，缓动收敛后等于目标填充）。
    /// </summary>
    private static void DrawBarFill(Rect barRect, float easedValue, Color solidColor)
    {
        float easedPx = barRect.width * Mathf.Clamp01(easedValue);
        if (easedPx > 0f)
        {
            Widgets.DrawBoxSolid(new Rect(barRect.x, barRect.y, easedPx, barRect.height), solidColor);
        }
    }

    /// <summary>
    /// 在条内绘制反色文本（遮罩思路）：先以白色文本一次绘制，
    /// 再以 BeginGroup 裁剪到当前填充区域重绘黑色文本，
    /// 实现「未被进度覆盖的文本为白色、被进度覆盖的文本为黑色」。
    /// forceWhite 开启（本条白色字体）时不绘制黑色遮罩，文本无视进度条比例，总是以白色显示。
    /// textRect 横向与条对齐（两侧各缩进 insetX，向条中心靠拢）、纵向取足行高
    /// （textRectY / height 规则与普通模式一致），文本垂直居中并向条外上下延伸，不被条高裁切。
    /// maskLeftInset 为文本在条内的左缘偏移（默认等于 insetX）；当文本被额外内容右移
    /// （如技能条左侧的激情图标）时由调用方传入实际偏移，保证遮罩反色范围与进度对齐。
    /// </summary>
    private static void DrawBarTextMasked(Rect textRect, float easedValue, string text, TextAnchor anchor,
        int fontSize, float insetX, bool forceWhite = false, float maskLeftInset = -1f)
    {
        if (string.IsNullOrEmpty(text) || textRect.width <= 0f)
        {
            return;
        }
        Widgets.BeginGroup(textRect);
        try
        {
            Rect full = textRect.AtZero(); // 与 textRect 同尺寸、原点 (0,0)
            Text.Anchor = anchor;
            GUI.color = Color.white;
            using (new FontSizeScope(fontSize))
            {
                Widgets.Label(full, text);

                // 白色字体：不绘制黑色遮罩，文本无视进度条比例，始终为白色。
                if (!forceWhite)
                {
                    // 遮罩：进度条从左覆盖，文本起点缩进 leftInset（默认 insetX），
                    // 故覆盖到文本的宽度 = 全条覆盖宽 - 文本左缘偏移。
                    float eased = Mathf.Clamp01(easedValue);
                    float leftInset = maskLeftInset >= 0f ? maskLeftInset : insetX;
                    float clipW = Mathf.Clamp((textRect.width + insetX + leftInset) * eased - leftInset, 0f, textRect.width);
                    if (clipW > 0f)
                    {
                        Widgets.BeginGroup(new Rect(0f, 0f, clipW, textRect.height));
                        try
                        {
                            GUI.color = Color.black;
                            Widgets.Label(full, text);
                        }
                        finally
                        {
                            Widgets.EndGroup();
                        }
                        GUI.color = Color.white;
                    }
                }
            }
        }
        finally
        {
            Widgets.EndGroup();
        }
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private static Color GetHealthColor(float fillPct)
    {
        if (fillPct >= 0.95f)
        {
            return fullColor;
        }
        return fillPct > 0.5f ? midColor : lowColor;
    }

    /// <summary>原版 12 个技能（SkillDefOf 引用，编译期解析；技能区只显示这些技能）。</summary>
    private static readonly List<SkillDef> VanillaSkillDefs = new List<SkillDef>
    {
        SkillDefOf.Shooting, SkillDefOf.Melee, SkillDefOf.Construction, SkillDefOf.Mining,
        SkillDefOf.Cooking, SkillDefOf.Plants, SkillDefOf.Animals, SkillDefOf.Crafting,
        SkillDefOf.Artistic, SkillDefOf.Medicine, SkillDefOf.Social, SkillDefOf.Intellectual
    };

    /// <summary>
    /// Pawn 技能区：在普通条区域下方（以分隔线相隔）绘制技能等级条网格（多列平铺的嵌入样式条）。
    /// 每个条：标签 = 技能名（左）、数值 = 技能等级（右）、填充 = 升级进度（XpProgressPercent）。
    /// 只取原版 12 技能，按原版技能页顺序（listOrder 降序）排列。
    /// 技能行之间、以及与上下分隔线之间使用 pawnSkillSpacing 间隔；列宽取整到整数像素、
    /// 余数像素分给前几列，保证每条宽度与列间距均为整数、右缘严丝合缝（无浮点残差）。
    /// 未开启或无技能时直接返回原 y 且 skillDrawn 为 false。
    /// </summary>
    private static float DrawPawnSkillBlock(Rect rect, Pawn pawn, float y, out bool skillDrawn)
    {
        skillDrawn = false;
        // Bounded Rationality 同步：技能信息未知时隐藏技能区（BR 隐藏技能页口径）。
        if (!pawnShowSkills || pawn.skills == null || !BoundedRationalityReflection.IsSkillsKnown(pawn))
        {
            return y;
        }

        // 只取原版 12 技能（缺失的记录跳过；通常全部存在）。
        List<SkillRecord> skills = CollectVanillaSkills(pawn);
        // Bounded Rationality 同步：技能与特性一样逐个解锁（按等级从高到低、随在殖民地时间逐步解锁），
        // 逐条按 BR「技能是否已知」过滤（Skills 类别未知时全部未知）；全部未知时整个技能区不显示。
        skills.RemoveAll(skill => !BoundedRationalityReflection.IsSkillKnown(skill));
        if (skills.Count == 0)
        {
            return y;
        }
        // 按原版技能页顺序（listOrder 降序）排列，保证与角色页一致。
        skills.SortByDescending(s => s.def.listOrder);

        float spacing = pawnSkillSpacing;

        // 分隔线（普通条区域与技能区之间），随后留间隔进入技能网格。
        if (showSeparators)
        {
            y = DrawSeparator(rect.width, y);
        }
        y += spacing;

        // 网格：每行 columns 列、行间留 spacing。列宽先向下取整到整数像素，
        // 除不尽的余数像素分给前几列各 +1px，使每条宽度与列间距都是整数、右缘严丝合缝
        //（列间距滑杆按整数步进、面板宽度亦为整数，故 总宽 = 各列宽之和 + 列间距之和，无浮点残差）。
        int columns = (int)pawnSkillColumns;
        float colGap = pawnSkillColumnGap;
        float barHeight = pawnSkillBarHeight;
        float barSpace = rect.width - colGap * (columns - 1); // 条可用的总宽度
        int baseColWidth = Mathf.Max(1, Mathf.FloorToInt(barSpace / columns));
        int extraPx = Mathf.Max(0, Mathf.FloorToInt(barSpace) - baseColWidth * columns); // 余数：前 extraPx 列各 +1px
        float rowHeight = barHeight + colGap;
        int rows = (skills.Count + columns - 1) / columns;

        for (int i = 0; i < skills.Count; i++)
        {
            int row = i / columns;
            int col = i % columns;
            // 列宽 = 基础宽 + 前 extraPx 列各多 1px；左缘 = 前面各列宽之和 + 前面各列间距之和。
            float width = baseColWidth + (col < extraPx ? 1 : 0);
            float x = col * colGap + col * baseColWidth + Mathf.Min(col, extraPx);
            float rowY = y + row * rowHeight;
            DrawSkillBar(new Rect(x, rowY, width, barHeight), skills[i]);
        }

        // 与下方分隔线的间隔（网格底部）。
        y += rows * barHeight + (rows-1) * colGap + spacing;

        skillDrawn = true;
        return y;
    }

    /// <summary>收集 Pawn 的原版 12 技能记录（SkillDefOf 中为 null 的条目跳过）。</summary>
    private static List<SkillRecord> CollectVanillaSkills(Pawn pawn)
    {
        List<SkillRecord> result = new List<SkillRecord>(VanillaSkillDefs.Count);
        List<SkillRecord> records = pawn.skills.skills;
        for (int i = 0; i < VanillaSkillDefs.Count; i++)
        {
            SkillDef def = VanillaSkillDefs[i];
            if (def == null)
            {
                continue;
            }
            for (int j = 0; j < records.Count; j++)
            {
                if (records[j].def == def)
                {
                    result.Add(records[j]);
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 绘制单个技能嵌入样式条：底色 + 按升级进度填充的颜色 + 嵌入的标签（技能名，左）与数值（等级，右）。
    /// 开启 pawnSkillPlainText 后改为纯文本样式：仅绘制技能名与等级文本，不绘制条背景与进度填充。
    /// 填充比例默认为当前等级内的升级进度（XpProgressPercent）；开启 pawnSkillTotalProgress 后
    /// 改为总进度 = (当前等级 + 当前升级进度) / 技能等级上限 20。
    /// 激情按 pawnSkillPassionStyle 表示：文本样式在技能名后附加 + / ++，图标样式绘制原版激情图标
    ///（SkillUI.PassionMinorIcon / PassionMajorIcon），默认在技能名左侧、开启 pawnSkillPassionIconAfterName 后改在技能名右侧。
    /// 标签与数值均嵌入条内：开启 pawnSkillWhiteFont（默认开启）时文本无视进度条比例、始终为白色，
    /// 关闭后按 DrawBarTextMasked 的遮罩进度反色（未覆盖区白色、已覆盖区黑色）；
    /// 纯文本样式无进度可反色，文本始终为白色。
    /// 开启 pawnSkillTooltip 时悬停条区显示原版角色面板的技能提示（见 DrawSkillTooltip）。
    /// </summary>
    private static void DrawSkillBar(Rect barRect, SkillRecord skill)
    {
        bool plainText = pawnSkillPlainText;
        float fill = 0f;
        if (!plainText)
        {
            Widgets.DrawBoxSolid(barRect, emptyColor);

            fill = pawnSkillTotalProgress
                ? Mathf.Clamp01((skill.Level + skill.XpProgressPercent) / SkillRecord.MaxLevel)
                : Mathf.Clamp01(skill.XpProgressPercent);
            if (fill > 0f)
            {
                Widgets.DrawBoxSolid(new Rect(barRect.x, barRect.y, barRect.width * fill, barRect.height), pawnSkillColor);
            }
        }

        // 文本矩形比条高，垂直居中，避免小号字被条高度裁切（规则与 DrawBarRow 一致）。
        int size = pawnSkillFontSize;
        float textRectHeight = Mathf.Max(barRect.height, 24f);
        textRectHeight = Mathf.Max(textRectHeight, LineHeightForSize(size));
        float textRectY = barRect.y + (barRect.height - textRectHeight) * 0.5f;
        float inset = 3f;
        // 图标样式的激情图标：默认绘制在条内最左侧、技能名相应右移；
        // 开启 pawnSkillPassionIconAfterName 后改绘制在技能名文本右侧（按当前字号实测名称宽度定位）。
        Texture2D? passionIcon = pawnSkillPassionStyle == SkillPassionStyle.Icon ? PassionIconFor(skill) : null;
        bool passionIconAfterName = passionIcon != null && pawnSkillPassionIconAfterName;
        float labelLeft = inset;
        if (passionIcon != null && !passionIconAfterName)
        {
            labelLeft = 0f;
            float iconSize = Mathf.Max(1f, Mathf.Min(barRect.height, LineHeightForSize(size)));
            float iconY = barRect.y + (barRect.height - iconSize) * 0.5f;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(barRect.x + labelLeft, iconY, iconSize, iconSize), passionIcon);
            labelLeft += iconSize + 0f;
        }

        // 纯文本样式无进度可反色，文本始终为白色（与白色字体设置等效）。
        bool whiteFont = plainText || pawnSkillWhiteFont;

        // 数值（等级）：条内右侧，位置与宽度不随图标变化。
        Rect valueRect = new Rect(barRect.x + inset, textRectY, Mathf.Max(1f, barRect.width - 2f * inset), textRectHeight);
        DrawBarTextMasked(valueRect, fill, SkillLevelText(skill), TextAnchor.MiddleRight, size, inset, forceWhite: whiteFont);

        // 技能名：自图标右侧起绘制；图标模式把标签左缘偏移传给遮罩，保证反色范围与进度对齐。
        string skillLabel = PawnSkillLabel(skill);
        Rect labelRect = new Rect(barRect.x + labelLeft, textRectY, Mathf.Max(1f, barRect.width - labelLeft - inset), textRectHeight);
        DrawBarTextMasked(labelRect, fill, skillLabel, TextAnchor.MiddleLeft, size, inset,
            forceWhite: whiteFont, maskLeftInset: labelLeft);

        // 图标置于技能名后：按当前字号实测名称宽度，紧贴名称右侧绘制激情图标；
        // 剩余宽度放不下图标（会压到右侧等级）时跳过。
        if (passionIconAfterName && passionIcon != null)
        {
            float nameWidth;
            using (new FontSizeScope(size))
            {
                nameWidth = Text.CalcSize(skillLabel).x;
            }
            float iconSize = Mathf.Max(1f, Mathf.Min(barRect.height, LineHeightForSize(size)));
            float iconX = barRect.x + labelLeft + nameWidth + 1f;
            if (iconX + iconSize <= barRect.x + barRect.width - inset)
            {
                float iconY = barRect.y + (barRect.height - iconSize) * 0.5f;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(iconX, iconY, iconSize, iconSize), passionIcon);
            }
        }

        // 悬浮提示：悬停条区时显示原版角色面板的技能提示（原版方法，开关控制）。
        if (pawnSkillTooltip)
        {
            DrawSkillTooltip(barRect, skill);
        }
    }

    /// <summary>技能等级文本：开启 pawnSkillShowProgressText 时以固定两位小数显示等级与当前等级内的升级进度
    ///（如 5 级 + 55% 升级进度 = 5.55、5 级 + 5% = 5.05；整级 / 满级同样带小数 = 5.00 / 20.00）；关闭则显示整数等级。</summary>
    private static string SkillLevelText(SkillRecord skill)
    {
        if (!pawnSkillShowProgressText)
        {
            return skill.Level.ToStringCached();
        }
        return (skill.Level + skill.XpProgressPercent).ToString("0.00");
    }

    // 原版角色面板技能提示的内容方法（SkillUI 私有静态方法，反射获取；目标缺失时安全降级为无提示）。
    private static readonly MethodInfo? SkillDescriptionMethod =
        AccessTools.Method(typeof(SkillUI), "GetSkillDescription", new[] { typeof(SkillRecord) });

    /// <summary>技能条悬浮提示：与原版 SkillUI.DrawSkill 相同的提示实现——内容取原版私有 GetSkillDescription
    ///（技能说明、当前等级、升级进度、学习速度等），提示标识码与原版一致（def 派生），同一技能的提示稳定不闪烁。</summary>
    private static void DrawSkillTooltip(Rect rect, SkillRecord skill)
    {
        if (SkillDescriptionMethod == null || !Mouse.IsOver(rect))
        {
            return;
        }
        if (SkillDescriptionMethod.Invoke(null, new object[] { skill }) is string text && !string.IsNullOrEmpty(text))
        {
            TooltipHandler.TipRegion(rect, new TipSignal(text, skill.def.GetHashCode() * 397945));
        }
    }

    /// <summary>技能条标签（本地化）：中文为技能名称原文，英文为 3-4 字母缩写；未配置翻译时回退到 Def 的 skillLabel。
    /// 文本样式在名称后附加激情后缀 + / ++（图标样式返回纯名称，激情由图标表示）。</summary>
    private static string PawnSkillLabel(SkillRecord skill)
    {
        string key = "BetterInspectPane.Skill_" + skill.def.defName;
        if (key.TryTranslate(out TaggedString translated))
        {
            return translated.RawText + PassionTextSuffix(skill);
        }
        string label = skill.def.skillLabel;
        return string.IsNullOrEmpty(label) ? skill.def.LabelCap : label;
    }

    /// <summary>文本样式的激情后缀：优先取 Vanilla Skills Expanded 的 PassionDef.Indicator（含其额外激情种类），
/// 未装 VSE 时回退原版 轻微 = "+"、重度 = "++"；无激情 = 空，图标样式一律为空字符串。
/// VSE 的 Indicator 自带前导空格（如 " ★★★"），此处去除以与回退的 + / ++ 风格一致。</summary>
    private static string PassionTextSuffix(SkillRecord skill)
    {
        if (pawnSkillPassionStyle != SkillPassionStyle.Text || skill.passion == Passion.None)
        {
            return string.Empty;
        }
        if (VSEPassionReflection.TryGetIndicator(skill.passion, out string? indicator))
        {
            return indicator!.Trim();
        }
        switch (skill.passion)
        {
            case Passion.Minor: return "+";
            case Passion.Major: return "++";
            default: return string.Empty;
        }
    }

    /// <summary>激情图标（图标样式）：优先取 Vanilla Skills Expanded 的 PassionDef.Icon（含其额外激情种类），
    /// 未装 VSE 时回退原版静态图标字段（SkillUI.PassionMinorIcon / PassionMajorIcon）；
    /// 无激情或技能被完全禁用（与原版一致，禁用时不绘制图标）时返回 null。</summary>
    private static Texture2D? PassionIconFor(SkillRecord skill)
    {
        if (skill.TotallyDisabled || skill.passion == Passion.None)
        {
            return null;
        }
        if (VSEPassionReflection.TryGetIcon(skill.passion, out Texture2D? vseIcon))
        {
            return vseIcon;
        }
        switch (skill.passion)
        {
            case Passion.Minor: return SkillUI.PassionMinorIcon;
            case Passion.Major: return SkillUI.PassionMajorIcon;
            default: return null;
        }
    }

    /// <summary>
    /// 自爆阈值标记：在健康条上标出引信启动的血量位置。
    /// 只要存在有效阈值即始终绘制（满血时也在条上标出危险分界），
    /// 仅在阈值百分比越出条范围时跳过。
    /// </summary>
    private static void DrawExplosiveMarker(Rect barRect, Thing thing)
    {
        if (!enableExplosiveThreshold)
        {
            return;
        }

        int explosiveThreshold = GetExplosiveThreshold();
        if (explosiveThreshold <= 0)
        {
            return;
        }

        float thresholdPercent = (float)explosiveThreshold / thing.MaxHitPoints;
        if (thresholdPercent <= 0f || thresholdPercent >= 1f)
        {
            return;
        }

        float markerX = barRect.x + barRect.width * thresholdPercent;
        float markerW = thinExplosiveMarker ? 1f : 2f;
        Widgets.DrawBoxSolid(new Rect(markerX, barRect.y, markerW, barRect.height), explosiveColor);
    }

    /// <summary>
    /// 在条底部绘制一条黑色 1px 宽、自条底部向上延伸的竖直阈值线（fraction 为条内横向位置比例 0..1，越出条范围时跳过）。
    /// 高度取阈值标记高度设置 thresholdMarkerHeight（默认 2px），并不超过条高。
    /// 在 DrawBarRow 中先于条内文本绘制，故不覆盖文本。
    /// </summary>
    private static void DrawThresholdMarker(Rect barRect, float fraction)
    {
        if (fraction <= 0f || fraction >= 1f)
        {
            return;
        }
        float x = barRect.x + barRect.width * fraction;
        float height = Mathf.Min(thresholdMarkerHeight, barRect.height);
        Widgets.DrawBoxSolid(new Rect(x, barRect.yMax - height, 1f, height), Color.black);
    }

    /// <summary>心情条阈值标记：轻度 / 中度 / 重度崩溃阈值（MentalBreaker，默认约 45% / 26% / 6%）。</summary>
    private static void DrawMoodThresholdMarkers(Rect barRect, Pawn pawn)
    {
        if (!enableThresholdMarkers)
        {
            return;
        }
        Verse.AI.MentalBreaker? breaker = pawn.mindState?.mentalBreaker;
        if (breaker == null)
        {
            return;
        }
        DrawThresholdMarker(barRect, breaker.BreakThresholdMinor);
        DrawThresholdMarker(barRect, breaker.BreakThresholdMajor);
        DrawThresholdMarker(barRect, breaker.BreakThresholdExtreme);
    }

    /// <summary>
    /// 血液条阈值标记：失血（BloodLoss）各可见阶段的起始严重度。血液条按 1 - 失血严重度显示，
    /// 故阶段边界在条上的位置取 1 - minSeverity（原版约 85% / 70% / 55% / 40%）。
    /// </summary>
    private static void DrawBleedThresholdMarkers(Rect barRect)
    {
        if (!enableThresholdMarkers)
        {
            return;
        }
        List<HediffStage>? stages = HediffDefOf.BloodLoss?.stages;
        if (stages == null)
        {
            return;
        }
        for (int i = 0; i < stages.Count; i++)
        {
            float minSeverity = stages[i].minSeverity;
            if (minSeverity > 0f && minSeverity < 1f)
            {
                DrawThresholdMarker(barRect, 1f - minSeverity);
            }
        }
    }

    /// <summary>疼痛条阈值标记：疼痛休克阈值（StatDefOf.PainShockThreshold，默认 75%）。
    /// 反转填充时条位取 1 - 阈值（疼痛越高条越空，休克分界随之翻转）。</summary>
    private static void DrawPainThresholdMarkers(Rect barRect, Pawn pawn, bool inverted)
    {
        if (!enableThresholdMarkers)
        {
            return;
        }
        float threshold = pawn.GetStatValue(StatDefOf.PainShockThreshold);
        DrawThresholdMarker(barRect, inverted ? 1f - threshold : threshold);
    }

    #endregion
}