using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimWorld.Planet;
using static ASQBetterInspectPane.MyModTemplateSettings;

namespace ASQBetterInspectPane;

/// <summary>
/// InspectPanePatch 的数据计算部分：进度条收集、数值文字、护盾、缓动、自爆阈值与统计缓存。
/// 绘制入口与条排布见 InspectPanePatch.cs（本类为 partial 声明）。
/// </summary>
public static partial class InspectPanePatch
{
    #region 缓存重建

    /// <summary>
    /// 判定帧缓存是否需要重算：对象未变且距上次重算不足间隔帧时返回 false。
    /// 各 RefreshXxxCache 的对象 + 帧数双重判定统一由此辅助完成；命中时才交由调用方重算并写回，
    /// 从而把「该不该重算」的决策集中到一处（首次发现新对象时必然重算）。
    /// intervalFrames 为两次重算之间的最小帧间隔，各调用方可按需指定（默认用全局 CacheRefreshIntervalFrames）。
    /// </summary>
    private static bool NeedsFrameRefresh<T>(ref T? owner, ref int refreshFrame, T newOwner, int intervalFrames = CacheRefreshIntervalFrames) where T : class
    {
        if (owner == newOwner && currentFrame - refreshFrame < intervalFrames)
        {
            return false;
        }
        owner = newOwner;
        refreshFrame = currentFrame;
        return true;
    }

    /// <summary>
    /// 重建区域统计数据缓存（种植/钓鱼）：渲染每一帧都会进行，而种植/钓鱼两条都涉及整片区域遍历
    /// （GetPlant / FishPopulationAt），因此按帧缓存，选中区域切换时立即重算。
    /// 直接遍历 zone.cells 字段（Cells 属性会洗牌列表，仅供区域着色使用，这里不需要）。
    /// 储存容量统计由 RefreshStorageCache 单独缓存（储存区与储存容器建筑共用）。
    /// </summary>
    /// <summary>种植类区域判定：原版 Zone_Growing 与任何实现 IPlantToGrowSettable 的区域
    /// （如 Vanilla Plants Expanded - More Plants 的 Zone_GrowingSandy / Zone_GrowingAquatic /
    /// Zone_GrowingFlowerGarden）都算种植区。</summary>
    private static bool IsGrowingZone(Zone zone) => zone is IPlantToGrowSettable;

    /// <summary>取种植区设定作物（须在 IsGrowingZone 为真时调用）。</summary>
    private static ThingDef GetZonePlantDef(Zone zone) => ((IPlantToGrowSettable)zone).GetPlantDefToGrow();

    private static void RefreshZoneCache(Zone zone)
    {
        // 种植/钓鱼区域遍历开销较高，拉长到每 60 帧重算一次（选中区域切换时仍立即重算）。
        if (!NeedsFrameRefresh(ref cachedZoneOwner, ref cachedZoneRefreshFrame, zone, 60))
        {
            return;
        }

        // 先清零，再按区域类型填充对应的一条（区域只属于一种类型，else-if 只会命中一支）。
        cachedGrowingPlanted = 0;
        cachedGrowingTotal = 0;
        cachedZoneGrowth = -1f;
        cachedZoneGrowthCount = 0;
        cachedFishingRemaining = 0;
        cachedFishingTarget = 0;

        Map? map = zone.Map;
        if (map == null)
        {
            return;
        }

        // 种植区：已种植格数 = 与种植区设定作物一致的植物数。
        // 总量分两种口径：
        //  - 普通作物：区域格数。
        //  - 无法间隔种植的作物（树，plant.blockAdjacentSow）：静态上限 = 贪心独立集估算
        //    （该区形状下最多能隔格种下的树数，稳定不随种植收缩）；但已种树量逼近上限
        //    （>90%）时改用动态口径（已种 + 当前可播种空格），避免游戏贪心播种达不到
        //    静态上限时条永远填不满。
        // 已种植条与总体生长条共用同一次遍历：前者统计格数，后者累加已种植作物的生长水平求均值。
        if (IsGrowingZone(zone))
        {
            if (enableGrowingBar || enableZoneGrowthBar)
            {
                bool growingBarOn = enableGrowingBar;
                bool zoneGrowthBarOn = enableZoneGrowthBar;
                ThingDef plantDef = GetZonePlantDef(zone);
                // GetPlantDefToGrow 可能返回 null（区域尚未设定作物，或部分模组区域实现如此）：
                // 无设定作物则无可统计的种植数据，缓存保持已清零状态即可。
                if (plantDef == null)
                {
                    return;
                }
                int planted = 0;
                float growthSum = 0f;
                int growthCount = 0;
                bool spacingRestricted = plantDef.plant.blockAdjacentSow;
                List<IntVec3> cells = zone.cells;
                int plantableEmpty = 0;   // 动态口径：空格中当前可播种格数（仅树区使用）。
                int staticCapacity = spacingRestricted ? 0 : cells.Count; // 静态上限。

                if (spacingRestricted && growingBarOn)
                {
                    // 树区且需绘制已种植条：静态上限 + 动态可种口径都需要。
                    // 第一遍：每格只取一次 GetPlant，缓存各格是否阻挡相邻播种（供动态口径复用），
                    // 并统计已种/生长。相比对每格的 8 邻再各取一次植物（原 AdjacentSowBlocker），
                    // 大区可减少约 8 倍 GetPlant 开销。
                    spacingBlockedCells.Clear();
                    sowBlockMap.Clear();
                    for (int i = 0; i < cells.Count; i++)
                    {
                        IntVec3 c = cells[i];
                        Plant plant = c.GetPlant(map);
                        sowBlockMap[c] = plant != null && (plant.def.plant.blockAdjacentSow || plant.sown);
                        if (plant != null && plant.def == plantDef)
                        {
                            planted++;
                            if (zoneGrowthBarOn)
                            {
                                growthSum += plant.Growth;
                                growthCount++;
                            }
                        }
                    }
                    // 第二遍：动态可种空格（区域内邻格查缓存，区域外邻格才取植物）与静态贪心上限。
                    for (int i = 0; i < cells.Count; i++)
                    {
                        IntVec3 c = cells[i];
                        Plant plant = c.GetPlant(map);
                        if ((plant == null || plant.def != plantDef) && !SowBlockedByNeighbor(c, map))
                        {
                            plantableEmpty++; // 空格且无相邻阻挡即可播种（同播种工判定口径）。
                        }
                        if (!spacingBlockedCells.Contains(c))
                        {
                            staticCapacity++; // 贪心独立集：未被虚拟封锁的格位计为一个可种位。
                            MarkSpacingBlocked(c);
                        }
                    }
                }
                else
                {
                    // 普通作物，或树区未启用已种植条（仅生长条）：只需统计已种/生长。
                    for (int i = 0; i < cells.Count; i++)
                    {
                        Plant plant = cells[i].GetPlant(map);
                        if (plant != null && plant.def == plantDef)
                        {
                            planted++;
                            if (zoneGrowthBarOn)
                            {
                                growthSum += plant.Growth;
                                growthCount++;
                            }
                        }
                    }
                }

                cachedGrowingPlanted = planted;
                // 树区：已种数量逼近静态上限（>90%），或区域已无位可种（当前可播种空格为 0 且已有已种）
                // 时切到动态口径，保证无论游戏播种顺序如何，种满时都能到 100%。
                cachedGrowingTotal = spacingRestricted
                        && (planted > staticCapacity * 0.9f || (planted > 0 && plantableEmpty == 0))
                    ? planted + plantableEmpty
                    : staticCapacity;
                cachedZoneGrowth = growthCount > 0 ? growthSum / growthCount : -1f;
                cachedZoneGrowthCount = growthCount;
            }
        }
        // 钓鱼区：水区剩余鱼数 = 该水域当前鱼类种群（按区域首格定位所属水体，只统计仍在水中
        // 未被钓走的鱼；而非 OwnedFishCount 那样遍历整张地图的鱼）。填充比例在 FishingSpec 中限制在 0..1。
        // fishing.targetPopulationPct 目标比例阈值
        else if (zone is Zone_Fishing fishing)
        {
            if (enableFishingBar)
            {
                cachedFishingRemaining = fishing.cells.Count > 0
                    ? Mathf.RoundToInt(map.waterBodyTracker.FishPopulationAt(fishing.cells[0]))
                    : 0;
                cachedFishingTarget = Mathf.FloorToInt(map.waterBodyTracker.MaxPopulationAt(fishing.cells[0]));
            }
        }
    }

    /// <summary>
    /// 树区静态上限（贪心独立集）的封锁辅助：把格位 c 的 8 邻写入 spacingBlockedCells，
    /// 使相邻格位不再重复计为一个虚拟可种位。仅作查询键使用，非本区域格位不会被查到，
    /// 因此无需边界/归属判定。
    /// </summary>
    private static void MarkSpacingBlocked(IntVec3 c)
    {
        for (int d = 0; d < 8; d++)
        {
            spacingBlockedCells.Add(c + GenAdj.AdjacentCells[d]);
        }
    }

    /// <summary>
    /// 树区动态口径的相邻阻挡判定（同 PlantUtility.AdjacentSowBlocker 的树目标分支）：
    /// 格位 c 的 8 邻中任一存在「阻挡相邻播种」的植物（plant.blockAdjacentSow 或已播种）即返回 true。
    /// 区域内的邻格查 sowBlockMap 缓存（构建时每格只取一次 GetPlant），区域外的邻格才直接取植物；
    /// 越界邻格忽略。仅树区（plant.blockAdjacentSow）调用。
    /// </summary>
    private static bool SowBlockedByNeighbor(IntVec3 c, Map map)
    {
        for (int d = 0; d < 8; d++)
        {
            IntVec3 n = c + GenAdj.AdjacentCells[d];
            if (sowBlockMap.TryGetValue(n, out bool blocks))
            {
                if (blocks)
                {
                    return true; // 区域内邻格：命中阻挡缓存。
                }
            }
            else if (n.InBounds(map))
            {
                Plant p = n.GetPlant(map);
                if (p != null && (p.def.plant.blockAdjacentSow || p.sown))
                {
                    return true; // 区域外邻格：直接取植物判定。
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 重建储存容量统计缓存（储存区与储存容器建筑共用）：渲染每一帧都会进行，
    /// 而 SpaceRemaining / HeldThingsCount 都会遍历所属格位，因此按帧缓存，每 30 帧重算一次；
    /// 选中对象切换时立即重算。
    /// 储存区：容量 = 区域格数，已用 = 总格数 - SpaceRemaining；
    /// 储存容器建筑（如物品架）：容量 = maxItemsInCell × 建筑占地格数，已用 = 已存放堆叠数；
    /// 书架（Building_Bookcase，非 Building_Storage）：容量 = MaximumBooks，已用 = HeldBooks.Count。
    /// </summary>
    private static void RefreshStorageCache(ISelectable sel)
    {
        if (!NeedsFrameRefresh(ref cachedStorageOwner, ref cachedStorageRefreshFrame, sel))
        {
            return;
        }

        cachedStorageUsed = 0;
        cachedStorageTotal = 0;

        if (!enableStorageBar)
        {
            return;
        }

        // 储存区：已用格数 = 总格数 - SpaceRemaining（SpaceRemaining 内部会遍历区域格数）。
        if (sel is Zone_Stockpile stockpile && stockpile.Map != null)
        {
            int total = stockpile.CellCount;
            cachedStorageUsed = Mathf.Max(0, total - stockpile.SpaceRemaining);
            cachedStorageTotal = total;
            return;
        }

        // 储存容器建筑（如物品架）：已用堆叠数 = slotGroup.HeldThingsCount（遍历建筑格位）。
        if (sel is Building_Storage storage && storage.Spawned)
        {
            int total = Mathf.Max(0, storage.def.building.maxItemsInCell * storage.def.Size.Area);
            cachedStorageUsed = Mathf.Min(total, storage.slotGroup.HeldThingsCount);
            cachedStorageTotal = total;
        }
        // 书架：书籍存放在内部容器（无 slotGroup），容量 = MaximumBooks，已用 = HeldBooks.Count。
        else if (sel is Building_Bookcase bookcase && bookcase.Spawned)
        {
            int total = Mathf.Max(0, bookcase.MaximumBooks);
            cachedStorageUsed = Mathf.Min(total, bookcase.HeldBooks.Count);
            cachedStorageTotal = total;
        }
    }

    /// <summary>
    /// 重建电网蓄电统计缓存（电力设备的电网蓄电条）：渲染每一帧都会进行，
    /// 而 CurrentStoredEnergy 与各电池容量总和都涉及整网遍历，因此按帧缓存，
    /// 每 30 帧重算一次；选中对象切换或所连电网变化时立即重算。
    /// 蓄电上限 = 全网电池容量之和（无电池的电网无蓄电量，条不显示）。
    /// </summary>
    private static void RefreshGridCache(PowerNet? net)
    {
        if (net == null)
        {
            cachedGridOwner = null;
            cachedGridStored = 0f;
            cachedGridMax = 0f;
            return;
        }
        if (!NeedsFrameRefresh(ref cachedGridOwner, ref cachedGridRefreshFrame, net))
        {
            return;
        }

        cachedGridStored = net.CurrentStoredEnergy();
        float max = 0f;
        List<CompPowerBattery> batteries = net.batteryComps;
        for (int i = 0; i < batteries.Count; i++)
        {
            max += batteries[i].Props.storedEnergyMax;
        }
        cachedGridMax = max;
    }

    /// <summary>切换选中单位时重建 Comp 缓存（GetComp 走类型匹配，每帧调用开销较高）。</summary>
    private static void RefreshCompCache(Thing thing, ThingWithComps? twc)
    {
        if (cachedCompOwner == thing)
        {
            return;
        }
        cachedCompOwner = thing;
        cachedInterceptor = twc?.GetComp<CompProjectileInterceptor>();
        cachedDestroyAfterDelay = twc?.GetComp<CompDestroyAfterDelay>();
        cachedRefuelable = twc?.GetComp<CompRefuelable>();
        cachedRottable = twc?.GetComp<CompRottable>();
        cachedExplosive = twc?.GetComp<CompExplosive>();
        cachedBattery = twc?.GetComp<CompPowerBattery>();
        cachedPower = twc?.GetComp<CompPower>();
        // 维护组件（Vanilla Gravship Expanded）走反射匹配，选中切换时一并缓存。
        cachedGravMaintainable = GravMaintenanceReflection.FindComp(twc);
        // VEF 处理系统组件（Vanilla Expanded Framework，反射匹配）走反射匹配，选中切换时一并缓存。
        cachedVefProcessor = VEFProcessorReflection.FindComp(twc);
        // VFE 管道网络组件（Vanilla Expanded Framework，PipeSystem.CompResource 及其派生）反射匹配，
        // 用于管道网络整网条与管道储存容器容量条，选中切换时一并缓存。
        cachedPipeResource = PipeNetReflection.FindComp(twc);
        // 扫描组件（CompScanner 基类匹配：CompDeepScanner 地质扫描仪 / CompLongRangeMineralScanner
        // 远距离矿物扫描仪，进度读取见 TryGetScannerProgress，两者共用 daysWorkingSinceLastFinding）。
        cachedScanner = twc?.GetComp<CompScanner>();
        cachedDeepDrill = twc?.GetComp<CompDeepDrill>();
        cachedBiosculpter = twc?.GetComp<CompBiosculpterPod>();
        cachedAtomizer = twc?.GetComp<CompAtomizer>();
        cachedExplosiveThreshold = -1;
        cachedTurretGun = thing as Building_TurretGun;
        // 充能条相关组件/建筑（飞船反应堆 / 机械孕育器 / 定时激活器 / 清污泵 / 排水泵 /
        // 逆重飞船引擎起飞冷却），选中切换时一并缓存。
        cachedHibernatable = twc?.GetComp<CompHibernatable>();
        cachedSpawnerPawn = twc?.GetComp<CompSpawnerPawn>();
        cachedCountdown = twc?.GetComp<CompSendSignalOnCountdown>();
        cachedPollutionPump = twc?.GetComp<CompPollutionPump>();
        cachedTerrainPumpDry = twc?.GetComp<CompTerrainPumpDry>();
        cachedGravEngine = thing as Building_GravEngine;
        // 驾驶控制台（Odyssey）：其充能条显示所连引擎的起飞冷却，选中切换时一并缓存。
        cachedPilotConsole = twc?.GetComp<CompPilotConsole>();
        // 散热器（Vanilla Gravship Expanded，反射匹配）：冷却条蓄热，选中切换时一并缓存。
        cachedHeatsink = GravshipHeatsinkReflection.FindComp(twc);
        // 动物产物组件（仅 Pawn 有）：产物/产毛/繁殖条数据，选中切换时一并缓存。
        cachedMilkable = twc?.GetComp<CompMilkable>();
        cachedShearable = twc?.GetComp<CompShearable>();
        cachedEggLayer = twc?.GetComp<CompEggLayer>();
        // 树精茧类建筑（CompDryadHolder 基类匹配，含模组派生）：daysToComplete 蜕变进度走生长条，
        // 选中切换时一并缓存。
        cachedDryadHolder = twc?.GetComp<CompDryadHolder>();
    }

    /// <summary>
    /// 切换选中 Pawn 时重建模组需求缓存（Dubs Bad Hygiene 等）：按需求 Def 的 defName 匹配
    /// （Bladder / Hygiene / DBHThirst），只要 Pawn 有对应需求就缓存供对应条读取。
    /// 不依赖具体模组/类型判断（DBH 可能有多个组件、难以按 mod 判断），需求列表查询成本极低。
    /// </summary>
    private static void RefreshPawnNeedCache(Pawn pawn)
    {
        if (cachedNeedOwner == pawn)
        {
            return;
        }
        cachedNeedOwner = pawn;
        cachedBladderNeed = null;
        cachedHygieneNeed = null;
        cachedThirstNeed = null;
        if (pawn.needs == null)
        {
            return;
        }
        List<Need> needs = pawn.needs.AllNeeds;
        for (int i = 0; i < needs.Count; i++)
        {
            Need need = needs[i];
            if (need.def == null)
            {
                continue;
            }
            switch (need.def.defName)
            {
                case "Bladder":
                    cachedBladderNeed = need;
                    break;
                case "Hygiene":
                    cachedHygieneNeed = need;
                    break;
                case "DBHThirst":
                    cachedThirstNeed = need;
                    break;
            }
        }
    }

    #endregion

    #region 进度条数据

    /// <summary>
    /// 按显示顺序依次解析可见条并收集：先由 order（顺序列表，下标即顺序）取各类型，
    /// 再交给 resolve 逐条解析（是否可见由各 Spec 自行判断），过滤空值后返回。
    /// 物品 / 世界地图对象 / 区域的三个 Collect 方法共用此循环，避免重复。
    /// </summary>
    private static List<BarRowInfo> CollectSpecs(List<BarType> order, Func<BarType, BarRowInfo?> resolve)
    {
        List<BarRowInfo> bars = new List<BarRowInfo>(Math.Max(1, order.Count));
        for (int i = 0; i < order.Count; i++)
        {
            BarRowInfo? spec = resolve(order[i]);
            if (spec.HasValue)
            {
                bars.Add(spec.Value);
            }
        }
        return bars;
    }

    /// <summary>
    /// 收集当前可见的进度条：按 mainBarOrder 依次取各物品条，统一更新健康/护盾两路缓动后返回。
    /// 条本身是否可见由各 Spec 自行判断。
    /// </summary>
    private static List<BarRowInfo> CollectBars(Thing thing)
    {
        float healthFill = thing.def.useHitPoints ? Mathf.Clamp01(thing.HitPoints / (float)thing.MaxHitPoints) : 0f;
        float shieldFill = enableShieldBar ? GetShieldFillPercent() : 0f;

        // 切换选中单位时重置缓动状态（需在解析 Spec 前完成，保证护盾条可见性判定的初始值为 0）。
        BeginEaseFrame(thing);

        // 收集所有可见条，随后统一对每条做缓动（条本身即为缓动值，见 EaseCollectedBars）。
        List<BarRowInfo> bars = CollectSpecs(mainBarOrder, type => ThingBarSpec(type, thing, healthFill, shieldFill));
        EaseCollectedBars(bars);
        return bars;
    }

    /// <summary>
    /// 构造一行进度条数据：统一填充 label / value / fill / height / fontScale / color / span / type 等公共字段。
    /// eased 初始等于 fill（直接贴到目标值），随后由 EaseCollectedBars 统一缓动更新。
    /// </summary>
    private static BarRowInfo BuildBar(string label, string value, float fill, Color color, float height,
        float fontScale, BarSpan span, BarType type, Action<Rect>? onBarDrawn = null, Need? tooltipNeed = null)
    {
        return new BarRowInfo
        {
            label = label,
            value = value,
            fill = fill,
            eased = fill,
            height = height,
            fontScale = fontScale,
            whiteFont = WhiteFontFor(type),
            color = color,
            span = span,
            type = type,
            onBarDrawn = onBarDrawn,
            tooltipNeed = tooltipNeed
        };
    }

    /// <summary>按条标识取物品/建筑面板对应条的数据；条不可用（未启用/对象不匹配）时返回 null。</summary>
    private static BarRowInfo? ThingBarSpec(BarType type, Thing thing, float healthFill, float shieldFill)
    {
        switch (type)
        {
            case BarType.Health: return HealthSpec(thing, healthFill);
            case BarType.Shield: return ShieldSpec(shieldFill);
            case BarType.Ammo: return AmmoSpec();
            case BarType.Freshness: return FreshnessSpec();
            case BarType.Work: return WorkSpec(thing);
            case BarType.Research: return ResearchSpec(thing);
            case BarType.Growth: return GrowthSpec(thing);
            case BarType.Storage: return StorageSpec(thing);
            case BarType.Battery: return BatterySpec();
            case BarType.PowerGrid: return PowerGridSpec();
            case BarType.Maintenance: return MaintenanceSpec(thing);
            case BarType.PipeNet: return PipeNetSpec(thing);
            case BarType.Cooldown: return CooldownSpec(thing);
            case BarType.Charge: return ChargeSpec(thing);
            default: return null;
        }
    }

    /// <summary>健康条数据（可选）：使用命中点的对象显示正常耐久条；不使用命中点（视为无限耐久）的对象
    /// 在对应设置开启时显示永远满格、数值为 ∞ 的耐久条，否则不显示。</summary>
    private static BarRowInfo? HealthSpec(Thing thing, float fillPct)
    {
        if (!thing.def.useHitPoints)
        {
            // 无限耐久条：对象没有可读的 HitPoints 数值，故条永远满格、数值固定显示 ∞，且无自爆阈值标记。
            return showInfiniteDurabilityBar
                ? BuildBar(
                    "BetterInspectPane.HealthLabel".Translate(),
                    "BetterInspectPane.InfiniteDurabilityValue".Translate(),
                    1f,
                    GetHealthColor(1f),
                    BarHeightFor(healthBarHeightOffset), healthFontScale,
                    healthBarSpan,
                    BarType.Health)
                : null;
        }

        return BuildBar(
            "BetterInspectPane.HealthLabel".Translate(),
            GetHealthValueText(thing, fillPct),
            fillPct,
            GetHealthColor(fillPct),
            BarHeightFor(healthBarHeightOffset), healthFontScale,
            healthBarSpan,
            BarType.Health,
            onBarDrawn: barRect => DrawExplosiveMarker(barRect, thing));
    }

    /// <summary>护盾条数据（可选）：布局与健康条一致（标签 / 数值 / 条），填充比例来自 CompProjectileInterceptor。</summary>
    private static BarRowInfo? ShieldSpec(float shieldFill)
    {
        if (!enableShieldBar || (autoHideShieldAtZero && shieldFill <= 0f && GetStoredEased(BarType.Shield) <= 0.002f))
        {
            return null;
        }

        return BuildBar(
            "BetterInspectPane.ShieldLabel".Translate(),
            GetShieldValueText(shieldFill),
            shieldFill,
            shieldColor,
            BarHeightFor(shieldBarHeightOffset), shieldFontScale,
            shieldBarSpan,
            BarType.Shield);
    }

    /// <summary>弹药条数据（可选）：有 CompRefuelable 的物体显示燃料/弹药余量，无缓动。</summary>
    private static BarRowInfo? AmmoSpec()
    {
        if (!enableAmmoBar)
        {
            return null;
        }

        CompRefuelable? refuelable = cachedRefuelable;
        if (refuelable == null)
        {
            return null;
        }

        float fuel = refuelable.Fuel;
        float capacity = refuelable.Props.fuelCapacity;
        float fill = capacity > 0f ? Mathf.Clamp01(fuel / capacity) : 0f;

        // 使用原版该物体的燃料名（如弹药/燃油），已本地化；为空/超长时退回默认“燃料”。
        string fuelLabel;
        fuelLabel = refuelable.Props.FuelGizmoLabel;
        if (fuelLabel.NullOrEmpty() || fuelLabel.Length > 4)
        {
            if (refuelable.parent.def.building?.turretGunDef != null)
            {
                fuelLabel = "BetterInspectPane.AmmoLabel".Translate();
            }
            else
            {
                fuelLabel = "BetterInspectPane.FuelLabel".Translate();
            }
        }

        return BuildBar(
            fuelLabel,
            GetAmmoValueText(fuel, capacity, fill),
            fill,
            ammoColor,
            BarHeightFor(ammoBarHeightOffset), ammoFontScale,
            ammoBarSpan,
            BarType.Ammo);
    }

    /// <summary>新鲜条数据（可选）：有 CompRottable 的物体显示新鲜度，无缓动。</summary>
    private static BarRowInfo? FreshnessSpec()
    {
        if (!enableFreshnessBar)
        {
            return null;
        }

        CompRottable? rottable = cachedRottable;
        if (rottable == null || rottable.PropsRot.TicksToRotStart <= 0)
        {
            return null;
        }

        // 新鲜度 = 1 - 腐烂进度（腐烂进度超过 1 时按 0 处理）。
        float fill = Mathf.Clamp01(1f - rottable.RotProgressPct);

        return BuildBar(
            "BetterInspectPane.FreshnessLabel".Translate(),
            GetFreshnessValueText(rottable, fill),
            fill,
            freshnessColor,
            BarHeightFor(freshnessBarHeightOffset), freshnessFontScale,
            freshnessBarSpan,
            BarType.Freshness);
    }

    /// <summary>
    /// 工作进度条数据（可选）：未完成物品（UnfinishedThing）的制作进度、建筑框架（Frame）的施工进度，
    /// 以及建筑类（基因装配器 / 发酵桶 / 扫描类建筑 / 深钻井 / 塑形舱 / 次核扫描仪 / VEF 处理系统工厂）的进行进度。
    /// 进度 = 1 - 剩余工作量 / 总工作量，白色、无缓动。
    /// </summary>
    private static BarRowInfo? WorkSpec(Thing thing)
    {
        if (!enableWorkBar)
        {
            return null;
        }

        // 未完成物品的制作或建筑框架的施工；未初始化/无法确定总工作量时跳过整行。
        if (!TryGetWorkProgress(thing, out float workLeft, out float totalWork, out bool buildingSource))
        {
            // 未在作业（工作已完成 / 空闲）：自动隐藏开启则隐藏；关闭且为本条工作来源时以满条显示。
            if (autoHideWorkAtHundred || !IsWorkSource(thing))
            {
                return null;
            }
            workLeft = 0f;
            totalWork = 100f;
            buildingSource = true;
        }

        float fill = Mathf.Clamp01(1f - workLeft / totalWork);

        // 记录本次进度采样（按游戏刻，供剩余时间估算使用）。
        AddProgressSample(ref workSample, thing, workLeft);

        return BuildBar(
            "BetterInspectPane.WorkLabel".Translate(),
            GetWorkValueText(workLeft, fill, buildingSource),
            fill,
            workColor,
            BarHeightFor(workBarHeightOffset), workFontScale,
            workBarSpan,
            BarType.Work);
    }

    /// <summary>
    /// 研究进度条数据（可选）：研究台显示当前研究项目的进度，深蓝色、无缓动。
    /// 进度 = 当前研究项目的 ProgressPercent（0..1）；无当前项目时不显示。
    /// 数值样式按最近两刻的进度采样估算剩余研究时间（同工作条）。
    /// </summary>
    private static BarRowInfo? ResearchSpec(Thing thing)
    {
        if (!enableResearchBar)
        {
            return null;
        }

        if (!(thing is Building_ResearchBench))
        {
            return null;
        }

        // 当前研究项目（全局，无项目时为 null；与原版信息文字一致）。
        ResearchProjectDef? project = Find.ResearchManager.GetProject();
        float fill;
        float max;
        float left;
        if (project == null)
        {
            // 无当前研究项目（研究已完成 / 未开始）：自动隐藏开启则隐藏；关闭则以满条显示。
            if (autoHideResearchAtHundred)
            {
                return null;
            }
            // 无项目：清空显示缓存，避免沿用上一项目的剩余时间估计。
            cachedResearchProject = null;
            researchSample.shownTicks = -1;
            researchSample.raiseSinceTick = 0;
            fill = 1f;
            max = 1f;
            left = 0f;
        }
        else
        {
            fill = project.ProgressPercent;
            max = project.Cost;
            left = Mathf.Max(0f, max - project.ProgressReal);

            // 研究项目变化：清空「只减不增」显示缓存，避免沿用上一项目的剩余时间下限。
            if (project != cachedResearchProject)
            {
                cachedResearchProject = project;
                researchSample.shownTicks = -1;
                researchSample.raiseSinceTick = 0;
            }

            // 记录本次进度采样（按游戏刻，供剩余时间估算使用）。
            // 研究进度是全局状态，不随选中物体切换清空采样；且每刻增量极小、以「Cost − ProgressReal」
            // 大数相减读出，用精度自适应窗口（累计到足够进度再测速）压制 float 分辨率噪声。
            AddProgressSample(ref researchSample, thing, left, resetOnOwnerChange: false,
                minDeltaFraction: ResearchMinDeltaFraction, maxWindowTicks: ResearchMaxWindowTicks,
                smoothTicks: ResearchSmoothTicks);
        }

        return BuildBar(
            "BetterInspectPane.ResearchLabel".Translate(),
            GetResearchValueText(left, fill, max),
            fill,
            researchColor,
            BarHeightFor(researchBarHeightOffset), researchFontScale,
            researchBarSpan,
            BarType.Research);
    }

    /// <summary>
    /// 生长进度条数据（可选）：植物显示当前生长进度（0..1），深绿色、无缓动。
    /// 树精茧类建筑（CompDryadHolder，daysToComplete 折算蜕变进度）也复用本条展示。
    /// </summary>
    private static BarRowInfo? GrowthSpec(Thing thing)
    {
        if (!enableGrowthBar)
        {
            return null;
        }

        // 树精茧类建筑：总时长 = 60000 × daysToComplete，按完成时刻 tickComplete 反推进度；
        // 数值样式显示精确剩余时间（口径同原版 InspectString 的 TimeLeft）。
        if (cachedDryadHolder != null && TryGetDryadCocoonGrowth(out float cocoonFill, out int cocoonTicksLeft))
        {
            return BuildBar(
                "BetterInspectPane.GrowthLabel".Translate(),
                GetCocoonGrowthValueText(cocoonTicksLeft, cocoonFill),
                cocoonFill,
                growthColor,
                BarHeightFor(growthBarHeightOffset), growthFontScale,
                growthBarSpan,
                BarType.Growth);
        }

        if (!(thing is Plant plant))
        {
            return null;
        }

        float fill = Mathf.Clamp01(plant.Growth);

        return BuildBar(
            "BetterInspectPane.GrowthLabel".Translate(),
            GetGrowthValueText(plant, fill),
            fill,
            growthColor,
            BarHeightFor(growthBarHeightOffset), growthFontScale,
            growthBarSpan,
            BarType.Growth);
    }

    /// <summary>
    /// 树精茧类建筑的蜕变进度：tickComplete（protected，反射读取）为完成时刻，
    /// TryAcceptPawn 时置为 当前刻 + 60000 × daysToComplete，进度 = 1 − 剩余 / 总时长。
    /// 总时长从 CompProperties_DryadCocoon.daysToComplete 读取（props 类型校验，兼容模组派生类）；
    /// 尚未收入树精（tickComplete 为 -1）或拿不到 daysToComplete 时不显示。
    /// </summary>
    private static bool TryGetDryadCocoonGrowth(out float fill, out int remainingTicks)
    {
        fill = 0f;
        remainingTicks = -1;
        if (cachedDryadHolder == null
            || DryadHolderTickCompleteField == null
            || DryadHolderTickCompleteField.GetValue(cachedDryadHolder) is not int tickComplete
            || tickComplete < 0
            || cachedDryadHolder.props is not CompProperties_DryadCocoon cocoonProps)
        {
            return false;
        }

        remainingTicks = Mathf.Max(0, tickComplete - Find.TickManager.TicksGame);
        int totalTicks = (int)(60000f * cocoonProps.daysToComplete);
        fill = totalTicks > 0 ? Mathf.Clamp01(1f - (float)remainingTicks / totalTicks) : 1f;
        return true;
    }

    /// <summary>
    /// 电池蓄电条数据（可选）：有 CompPowerBattery 的建筑（电池）显示自身蓄电量，
    /// 黄色、无缓动。
    /// </summary>
    private static BarRowInfo? BatterySpec()
    {
        if (!enableBatteryBar)
        {
            return null;
        }

        CompPowerBattery? battery = cachedBattery;
        if (battery == null || battery.Props.storedEnergyMax <= 0f)
        {
            return null;
        }

        float fill = Mathf.Clamp01(battery.StoredEnergy / battery.Props.storedEnergyMax);

        return BuildBar(
            "BetterInspectPane.BatteryLabel".Translate(),
            GetBatteryValueText(battery, fill),
            fill,
            batteryColor,
            BarHeightFor(batteryBarHeightOffset), batteryFontScale,
            batteryBarSpan,
            BarType.Battery);
    }

    /// <summary>
    /// 电网蓄电条数据（可选）：电力设备（用电/发电建筑，CompPowerTrader 及派生）显示
    /// 所连电网的蓄电量，黄色、无缓动。统计值来自按帧缓存的 RefreshGridCache。
    /// 电池自身有独立蓄电条，也可同时显示电网蓄电条（两条并排）；无电池的电网无蓄电量，条不显示。
    /// </summary>
    private static BarRowInfo? PowerGridSpec()
    {
        if (!enablePowerGridBar)
        {
            return null;
        }

        // 电池（CompPowerBattery）也属于 CompPower，同样显示所连电网的蓄电条。
        if (cachedPower == null)
        {
            return null;
        }
        if (cachedGridMax <= 0f)
        {
            return null;
        }

        float fill = Mathf.Clamp01(cachedGridStored / cachedGridMax);

        return BuildBar(
            "BetterInspectPane.PowerGridLabel".Translate(),
            GetPowerGridValueText(fill),
            fill,
            powerGridColor,
            BarHeightFor(powerGridBarHeightOffset), powerGridFontScale,
            powerGridBarSpan,
            BarType.PowerGrid);
    }

    /// <summary>
    /// 维护条数据（可选，需 Vanilla Gravship Expanded）：仅对「需维护」的建筑显示，
    /// 淡紫色、无缓动。填充 = 维护剩余量（0..1），反射读取 CompGravMaintainable。
    /// </summary>
    private static BarRowInfo? MaintenanceSpec(Thing thing)
    {
        if (!enableMaintenanceBar)
        {
            return null;
        }

        ThingComp? comp = cachedGravMaintainable;
        if (comp == null || !GravMaintenanceReflection.TryGetMaintenance(comp, out float maintenance, out bool maintenanceFalls))
        {
            return null;
        }
        if (!maintenanceFalls)
        {
            return null;
        }

        float fill = Mathf.Clamp01(maintenance);

        return BuildBar(
            "BetterInspectPane.MaintenanceLabel".Translate(),
            GetMaintenanceValueText(fill),
            fill,
            maintenanceColor,
            BarHeightFor(maintenanceBarHeightOffset), maintenanceFontScale,
            maintenanceBarSpan,
            BarType.Maintenance);
    }

    /// <summary>
    /// 管道网络条数据（可选，Vanilla Expanded Framework 的管道系统）：已连接管道网络的建筑
    /// 显示所在网络整网的内容物 / 总容量，无缓动。填充 = 网络已存内容物 / 网络总容量，
    /// 数据来自按选中对象缓存的 cachedPipeResource 反射读取。网络无容量的建筑不显示。
    /// 颜色：开启「使用资源颜色」且资源色有效时用资源色（透明度 70%），否则用配置色。
    /// </summary>
    private static BarRowInfo? PipeNetSpec(Thing thing)
    {
        if (!enablePipeNetBar)
        {
            return null;
        }

        ThingComp? comp = cachedPipeResource;
        if (comp == null || !PipeNetReflection.TryGetNetworkData(comp, out float stored, out float totalCapacity))
        {
            return null;
        }
        if (totalCapacity <= 0f)
        {
            return null;
        }

        float fill = Mathf.Clamp01(stored / totalCapacity);
        Color color = useResourceColor
            && PipeNetReflection.TryGetResourceColor(comp, out Color rc)
            && rc.a > 0f
            ? new Color(rc.r, rc.g, rc.b, 0.7f)
            : pipeNetColor;

        return BuildBar(
            "BetterInspectPane.PipeNetLabel".Translate(),
            GetPipeNetValueText(stored, totalCapacity, fill),
            fill,
            color,
            BarHeightFor(pipeNetBarHeightOffset), pipeNetFontScale,
            pipeNetBarSpan,
            BarType.PipeNet);
    }

    /// <summary>
    /// 冷却条数据（可选，炮塔 / 散热器）：
    /// 炮塔——开火冷却 ≥ 设置的最短显示时长（默认 3 秒）的炮塔（含迫击炮与普通炮塔）显示距下次可开火的冷却进度；
    /// 散热器（Vanilla Gravship Expanded 的 heatsink）——显示吸收逆重飞行热量的蓄热进度，
    /// 蓄热随飞行增加、停机后逐渐消散，数值口径与游戏检视字符串一致。
    /// 淡蓝色、无缓动。
    /// 炮塔填充默认 = 1 - 剩余冷却刻数 / 总冷却刻数（冷却进行中由空渐满），
    /// 散热器填充默认 = 1 - 蓄热 / 最大蓄热（由空渐满表示散热/冷却进度，蓄热清零时不显示）；
    /// 开启「反转填充」时均反转（由满渐空）。
    /// </summary>
    private static BarRowInfo? CooldownSpec(Thing thing)
    {
        if (!enableCooldownBar)
        {
            return null;
        }

        // 炮塔开火冷却：数据来自按选中对象缓存的 cachedTurretGun 反射读取 burstCooldownTicksLeft，
        // 就绪（剩余冷却刻数 ≤ 0）时自动隐藏开启则不显示整条，关闭则以满条显示。
        Building_TurretGun? turret = cachedTurretGun;
        if (turret != null && TurretCooldownTicksField != null)
        {
            int totalTicks = GetTurretTotalCooldownTicks(turret);
            // 总冷却低于设置的最短显示时长（默认 3 秒）的炮塔不显示，避免短冷却炮塔的条频繁闪动。
            if (totalTicks >= Mathf.RoundToInt(minCooldownSeconds * 60f))
            {
                int cooldownLeft = (int)TurretCooldownTicksField.GetValue(turret);
                if (cooldownLeft > 0 || !autoHideCooldownAtHundred)
                {
                    // 就绪/未冷却时：与游戏内进度条只在冷却期出现一致（自动隐藏开启时不显示）。
                    float clampedLeft = Math.Max(0, cooldownLeft);
                    float fill = Mathf.Clamp01(1f - clampedLeft / (float)totalTicks);
                    if (invertCooldownFill)
                    {
                        fill = 1f - fill; // 反转：冷却进行中由满渐空。
                    }
                    return BuildBar(
                        "BetterInspectPane.CooldownLabel".Translate(),
                        GetCooldownValueText(Math.Max(0, cooldownLeft), fill),
                        fill,
                        cooldownColor,
                        BarHeightFor(cooldownBarHeightOffset), cooldownFontScale,
                        cooldownBarSpan,
                        BarType.Cooldown);
                }
            }
        }

        // 散热器蓄热（Vanilla Gravship Expanded，heatsink）：填充默认 = 1 - 蓄热/最大蓄热，
        // 与炮塔默认口径一致（由空渐满表示冷却进度，蓄热越低越接近冷却完成）；
        // 蓄热清零（冷却完成）时自动隐藏开启则不显示，关闭则以满条显示
        // （与游戏内散热器发光只有储热时出现一致）。
        if (cachedHeatsink != null
            && GravshipHeatsinkReflection.TryGetHeat(cachedHeatsink, out float stored, out float max)
            && (stored > 0f || !autoHideCooldownAtHundred))
        {
            float heatFill = Mathf.Clamp01(1f - Math.Max(0f, stored) / max);
            if (invertCooldownFill)
            {
                heatFill = 1f - heatFill;
            }
            return BuildBar(
                "BetterInspectPane.CooldownLabel".Translate(),
                GetHeatsinkValueText(Math.Max(0f, stored), max, heatFill),
                heatFill,
                cooldownColor,
                BarHeightFor(cooldownBarHeightOffset), cooldownFontScale,
                cooldownBarSpan,
                BarType.Cooldown);
        }

        return null;
    }

    /// <summary>炮塔总冷却刻数（复刻 Building_TurretGun.BurstCooldownTime：turretBurstCooldownTime >= 0 用之，否则回退主 verb 默认冷却；1 秒 = 60 刻）。</summary>
    private static int GetTurretTotalCooldownTicks(Building_TurretGun turret)
    {
        float seconds = turret.def.building.turretBurstCooldownTime >= 0f
            ? turret.def.building.turretBurstCooldownTime
            : turret.AttackVerb.verbProps.defaultCooldownTime;
        return seconds > 0f ? Mathf.RoundToInt(seconds * 60f) : 0;
    }

    /// <summary>
    /// 充能条数据（可选）：飞船反应堆（CompHibernatable 启动过程）/ 机械孕育器（CompSpawnerPawn 下次孕育）/
    /// 定时激活器（CompSendSignalOnCountdown 倒计时）/ 清污泵（CompPollutionPump 下次抽污）/
    /// 排水泵（CompTerrainPumpDry 排干半径扩展）/ 逆重飞船引擎（Building_GravEngine 起飞冷却）/
    /// 驾驶控制台（显示所连引擎的起飞冷却）/ 护盾发生器（CompProjectileInterceptor 重启恢复阶段）的充能进度，
    /// 紫色、无缓动。填充 = 已充能比例（0..1），数值样式显示剩余充能时间。
    /// </summary>
    private static BarRowInfo? ChargeSpec(Thing thing)
    {
        if (!enableChargeBar)
        {
            return null;
        }

        if (!TryGetChargeProgress(thing, out float fill, out int remainingTicks))
        {
            // 未在充能（充能已完成 / 空闲）：自动隐藏开启则隐藏；关闭且为本条充能来源时以满条显示。
            if (autoHideChargeAtHundred || !IsChargeSource(thing))
            {
                return null;
            }
            fill = 1f;
            remainingTicks = 0;
        }

        return BuildBar(
            "BetterInspectPane.ChargeLabel".Translate(),
            GetChargeValueText(remainingTicks, fill),
            fill,
            chargeColor,
            BarHeightFor(chargeBarHeightOffset), chargeFontScale,
            chargeBarSpan,
            BarType.Charge);
    }

    /// <summary>
    /// 取充能进度：飞船反应堆 / 清污泵 / 排水泵 / 护盾发生器重启按精确总量（props 字段计算），
    /// 机械孕育器 / 定时激活器 / 逆重飞船引擎 / 驾驶控制台按观察到的最大剩余量估算总量。输出已充能比例与剩余刻数。
    /// 清污泵 / 排水泵仅在有电（CompPowerTrader.PowerOn）时视为充能中。
    /// </summary>
    private static bool TryGetChargeProgress(Thing thing, out float fill, out int remainingTicks)
    {
        fill = 0f;
        remainingTicks = 0;

        // 飞船反应堆：仅在启动过程（Starting）充能。
        if (cachedHibernatable != null && cachedHibernatable.parent == thing
            && cachedHibernatable.State == HibernatableStateDefOf.Starting
            && HibernatableEndStartupTickField != null)
        {
            float totalTicks = cachedHibernatable.Props.startupDays * 60000f;
            int remaining = (int)HibernatableEndStartupTickField.GetValue(cachedHibernatable) - Find.TickManager.TicksGame;
            if (totalTicks > 0f && remaining >= 0)
            {
                fill = Mathf.Clamp01(1f - remaining / totalTicks);
                remainingTicks = remaining;
                return true;
            }
            return false;
        }

        // 机械孕育器：距下次孕育的充能（需激活且已排定孕育时刻）。
        if (cachedSpawnerPawn != null && cachedSpawnerPawn.parent == thing
            && cachedSpawnerPawn.Active && cachedSpawnerPawn.nextPawnSpawnTick > 0)
        {
            int remaining = cachedSpawnerPawn.nextPawnSpawnTick - Find.TickManager.TicksGame;
            if (remaining > 0 && TrackChargeBase(thing, remaining, out float baseTicks) && baseTicks > 0f)
            {
                fill = Mathf.Clamp01(1f - remaining / baseTicks);
                remainingTicks = remaining;
                return true;
            }
            return false;
        }

        // 定时激活器：倒计时充能（距触发）。
        if (cachedCountdown != null && cachedCountdown.parent == thing && cachedCountdown.ticksLeft > 0)
        {
            int remaining = cachedCountdown.ticksLeft;
            if (TrackChargeBase(thing, remaining, out float baseTicks) && baseTicks > 0f)
            {
                fill = Mathf.Clamp01(1f - remaining / baseTicks);
                remainingTicks = remaining;
                return true;
            }
            return false;
        }

        // 清污泵：抽污循环充能（需有电）。
        if (cachedPollutionPump != null && cachedPollutionPump.parent == thing
            && PumpPowerOn() && PollutionPumpTicksUntilPumpField != null)
        {
            int intervalTicks = cachedPollutionPump.Props.intervalTicks;
            int remaining = (int)PollutionPumpTicksUntilPumpField.GetValue(cachedPollutionPump);
            if (intervalTicks > 0 && remaining > 0)
            {
                fill = Mathf.Clamp01(1f - remaining / (float)intervalTicks);
                remainingTicks = remaining;
                return true;
            }
            return false;
        }

        // 排水泵：排干半径扩展充能（需有电，且尚未扩展到满半径）。
        if (cachedTerrainPumpDry != null && cachedTerrainPumpDry.parent == thing
            && PumpPowerOn() && TerrainPumpProgressTicksField != null)
        {
            int daysToRadius = Mathf.RoundToInt(((CompProperties_TerrainPump)cachedTerrainPumpDry.props).daysToRadius);
            int progress = (int)TerrainPumpProgressTicksField.GetValue(cachedTerrainPumpDry);
            if (daysToRadius > 0)
            {
                int totalTicks = daysToRadius * 60000;
                if (progress > 0 && progress < totalTicks)
                {
                    fill = Mathf.Clamp01(progress / (float)totalTicks);
                    remainingTicks = totalTicks - progress;
                    return true;
                }
            }
            return false;
        }

        // 逆重飞船引擎：起飞后的冷却（Building_GravEngine.cooldownCompleteTick 为公开字段，
        // 冷却期剩余刻数 > 0；总量按观察到的最大剩余量估算——总冷却由启航质量查表
        // （GravshipUtility.LaunchCooldownFromQuality）得出，但 Vanilla Gravship Expanded
        // 的散热器可减免冷却，故与机械孕育器/定时激活器同样按观察最大值估算）。
        if (cachedGravEngine != null)
        {
            int remaining = cachedGravEngine.cooldownCompleteTick - Find.TickManager.TicksGame;
            if (remaining > 0 && TrackChargeBase(thing, remaining, out float baseTicks) && baseTicks > 0f)
            {
                fill = Mathf.Clamp01(1f - remaining / baseTicks);
                remainingTicks = remaining;
                return true;
            }
            return false;
        }

        // 驾驶控制台（Odyssey CompPilotConsole）：显示所连逆重飞船引擎的起飞冷却，口径与引擎充能条一致
        // （engine 为 CompGravshipFacility 的公开字段，链接到引擎时非空）。
        if (cachedPilotConsole != null && cachedPilotConsole.parent == thing)
        {
            Building_GravEngine? engine = cachedPilotConsole.engine;
            if (engine != null)
            {
                int remaining = engine.cooldownCompleteTick - Find.TickManager.TicksGame;
                if (remaining > 0 && TrackChargeBase(thing, remaining, out float baseTicks) && baseTicks > 0f)
                {
                    fill = Mathf.Clamp01(1f - remaining / baseTicks);
                    remainingTicks = remaining;
                    return true;
                }
            }
            return false;
        }

        // 护盾发生器「重启时间」（Odyssey 逆重飞船护盾发生器 CompGravshipShieldGenerator 等拦截器）：
        // 拦截器充能/恢复阶段（ChargingTicksLeft > 0，对应原版 gizmo 的 ShieldTimeToRecovery），
        // 填充 = 已恢复比例（0..1），数值显示剩余恢复时间。总时长 = Props.chargeDurationTicks。
        if (cachedInterceptor != null && cachedInterceptor.ChargingTicksLeft > 0)
        {
            int total = cachedInterceptor.Props.chargeDurationTicks;
            int remaining = cachedInterceptor.ChargingTicksLeft;
            if (total > 0)
            {
                fill = Mathf.Clamp01(1f - remaining / (float)total);
                remainingTicks = remaining;
                return true;
            }
            return false;
        }

        return false;
    }

    /// <summary>清污泵 / 排水泵是否通电工作（同 CompTerrainPump.Working 口径：有电力组件看 PowerOn，无则视为工作）。</summary>
    private static bool PumpPowerOn()
    {
        return cachedPower is CompPowerTrader trader ? trader.PowerOn : true;
    }

    /// <summary>
    /// 是否为充能条来源（飞船反应堆 / 机械孕育器 / 定时激活器 / 清污泵 / 排水泵 / 逆重飞船引擎 / 驾驶控制台）。
    /// 用于关闭 100% 自动隐藏时，对未在充能（已充能完成 / 空闲）的来源以满条显示。
    /// </summary>
    private static bool IsChargeSource(Thing thing)
    {
        return (cachedHibernatable != null && cachedHibernatable.parent == thing)
            || (cachedSpawnerPawn != null && cachedSpawnerPawn.parent == thing)
            || (cachedCountdown != null && cachedCountdown.parent == thing)
            || (cachedPollutionPump != null && cachedPollutionPump.parent == thing)
            || (cachedTerrainPumpDry != null && cachedTerrainPumpDry.parent == thing)
            || cachedGravEngine != null
            || (cachedPilotConsole != null && cachedPilotConsole.parent == thing && cachedPilotConsole.engine != null);
    }

    /// <summary>
    /// 充能总量基数（观察最大值）跟踪：切换选中物体时以当前剩余量为基数，
    /// 剩余量回升（进入新周期）时更新基数。输出当前基数。
    /// </summary>
    private static bool TrackChargeBase(Thing thing, float remaining, out float baseTicks)
    {
        if (cachedChargeOwner != thing)
        {
            cachedChargeOwner = thing;
            cachedChargeTotal = remaining;
        }
        else if (remaining > cachedChargeTotal)
        {
            cachedChargeTotal = remaining;
        }
        baseTicks = cachedChargeTotal;
        return baseTicks > 0f;
    }

    /// <summary>
    /// 世界地图对象的可见条：按 worldObjectBarOrder 依次取剩余时间 / 阵营关系条。
    /// 条本身是否可见由各 Spec 自行判断。
    /// </summary>
    private static List<BarRowInfo> CollectWorldObjectBars(WorldObject wo)
    {
        BeginEaseFrame(wo);
        List<BarRowInfo> bars = CollectSpecs(worldObjectBarOrder, type => WorldObjectBarSpec(type, wo));
        EaseCollectedBars(bars);
        return bars;
    }

    /// <summary>按条标识取世界地图对象面板对应条的数据；条不可用（未启用/对象不匹配）时返回 null。</summary>
    private static BarRowInfo? WorldObjectBarSpec(BarType type, WorldObject wo)
    {
        switch (type)
        {
            case BarType.TimeRemaining: return TimeRemainingSpec(wo);
            case BarType.FactionRelation: return FactionRelationSpec(wo);
            case BarType.Strength: return StrengthSpec(wo);
            default: return null;
        }
    }

    /// <summary>
    /// 剩余时间条数据（可选）：有 TimeoutComp 且计时中的世界地图对象显示剩余时间，无缓动。
    /// 填充 = 剩余时间 / 总时长（总时长 ≈ 超时结束时刻 - 创建时刻；
    /// TicksLeft = endTick - now，故总时长 = TicksLeft + (now - creationGameTicks)）。
    /// </summary>
    private static BarRowInfo? TimeRemainingSpec(WorldObject wo)
    {
        if (!enableTimeRemainingBar)
        {
            return null;
        }

        TimeoutComp? timeout = wo.GetComponent<TimeoutComp>();
        if (timeout == null || !timeout.Active)
        {
            return null;
        }
        // 剩余时间耗尽（≤ 0）：自动隐藏开启则隐藏；关闭则继续显示（按 0 填充）。
        if (autoHideTimeRemainingAtZero && timeout.TicksLeft <= 0)
        {
            return null;
        }

        // 剩余时间耗尽（≤ 0）且关闭自动隐藏时：按 0 显示（避免负值）。
        int ticksLeft = Math.Max(0, timeout.TicksLeft);
        long total = (long)ticksLeft + (Find.TickManager.TicksGame - wo.creationGameTicks);
        float fill = total > 0 ? Mathf.Clamp01(ticksLeft / (float)total) : 1f;

        return BuildBar(
            "BetterInspectPane.TimeRemainingLabel".Translate(),
            FormatValueText(timeRemainingValueStyle, ticksLeft.ToStringTicksToPeriod(), fill),
            fill,
            timeRemainingColor,
            BarHeightFor(timeRemainingBarHeightOffset), timeRemainingFontScale,
            timeRemainingBarSpan,
            BarType.TimeRemaining);
    }

    /// <summary>
    /// 阵营关系条数据（可选）：有阵营且非玩家阵营的世界地图对象显示与玩家阵营的关系，无缓动。
    /// 填充 = 关系绝对值 / 上限；上限取值见 showRelationThresholdMarker（默认原版盟友阈值 75）；
    /// 颜色用原版 ColoredText 的关系颜色：负（敌意）红、正（盟友）绿、零（中立）青。
    /// </summary>
    private static BarRowInfo? FactionRelationSpec(WorldObject wo)
    {
        if (!enableFactionRelationBar)
        {
            return null;
        }

        Faction? faction = wo.Faction;
        if (faction == null || faction == Faction.OfPlayer || !faction.HasGoodwill)
        {
            return null;
        }
        int naturalGoodwill = faction.NaturalGoodwill;
        int goodwill = faction.PlayerGoodwill;
        Color color = goodwill < 0
            ? ColoredText.FactionColor_Hostile
            : (goodwill > 0 ? ColoredText.FactionColor_Ally : ColoredText.FactionColor_Neutral);

        // 关系阈值标记：开启后条上限改用「关系上限」并在盟友阈值（75）位置画阈值标记——
        // World Domination 2 激活时上限取其设置中的关系上限（Transpiler 后的原版好感上限），否则用原版上限 100；
        // 关闭时维持原口径（上限 = 盟友阈值 75，无标记）。标记受效果页「阈值标记」总开关控制。
        float cap = 75f;
        Action<Rect>? onBarDrawn = null;
        if (showRelationThresholdMarker)
        {
            cap = WorldDominationReflection.TryGetGoodwillCap(out int wd2Cap) ? wd2Cap : 100f;
            if (enableThresholdMarkers && cap > 75f)
            {
                float markerFraction = 75f / cap;
                onBarDrawn = barRect => DrawThresholdMarker(barRect, markerFraction);
            }
        }

        return BuildBar(
            "BetterInspectPane.FactionRelationLabel".Translate(),
            FormatValueText(factionRelationValueStyle, goodwill.ToString(), Mathf.Clamp(goodwill / cap, -1f, 1f), true),
            Mathf.Clamp01(Mathf.Abs(goodwill) / cap),
            color,
            BarHeightFor(factionRelationBarHeightOffset), factionRelationFontScale,
            factionRelationBarSpan,
            BarType.FactionRelation,
            onBarDrawn);
    }

    /// <summary>
    /// 强度条数据（可选，World Domination 2 反射读取），两类来源：
    /// 1) 带强度组件（CompViralSpread）的世界地图对象（据点 / 前哨站等）：总强度（进攻 + 防御）相对上限；
    /// 2) 移动商队（WorldObject_Traveler 及其任务子类）：当前强度相对出发时强度；
    /// 口径均与该模组检查文字一致；无有效基数（玩家殖民地、基数 ≤ 0 等）时自动隐藏。
    /// </summary>
    private static BarRowInfo? StrengthSpec(WorldObject wo)
    {
        if (!enableStrengthBar)
        {
            return null;
        }
        if (WorldDominationReflection.FindComp(wo) is { } comp
            && WorldDominationReflection.TryGetStrength(comp, out float current, out float max))
        {
            return BuildStrengthBar(current, max);
        }
        // 移动商队（World Domination 2）：强度 / 出发时强度。
        if (WorldDominationReflection.TryGetTravelerStrength(wo, out float tCurrent, out float tMax))
        {
            return BuildStrengthBar(tCurrent, tMax);
        }
        return null;
    }

    /// <summary>按当前强度与上限构造强度条：填充 = 当前 / 上限（已由来源保证上限 > 0）。</summary>
    private static BarRowInfo BuildStrengthBar(float current, float max)
    {
        float fill = Mathf.Clamp01(current / max);

        return BuildBar(
            "BetterInspectPane.StrengthLabel".Translate(),
            FormatValueText(strengthValueStyle, $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}", fill),
            fill,
            strengthColor,
            BarHeightFor(strengthBarHeightOffset), strengthFontScale,
            strengthBarSpan,
            BarType.Strength);
    }

    /// <summary>
    /// 区域的可见条：按 zoneBarOrder 依次取已种植 / 剩余容量 / 剩余鱼条。
    /// 条本身是否可见由各 Spec 按区域类型自行判断；统计值来自按刻缓存的 RefreshZoneCache。
    /// </summary>
    private static List<BarRowInfo> CollectZoneBars(Zone zone)
    {
        BeginEaseFrame(zone);
        List<BarRowInfo> bars = CollectSpecs(zoneBarOrder, type => ZoneBarSpec(type, zone));
        EaseCollectedBars(bars);
        return bars;
    }

    /// <summary>按条标识取区域面板对应条的数据；条不可用（未启用/区域类型不匹配）时返回 null。</summary>
    private static BarRowInfo? ZoneBarSpec(BarType type, Zone zone)
    {
        switch (type)
        {
            case BarType.Growing: return GrowingSpec(zone);
            case BarType.ZoneGrowth: return ZoneGrowthSpec(zone);
            case BarType.Storage: return StorageSpec(zone);
            case BarType.Fishing: return FishingSpec(zone);
            default: return null;
        }
    }

    /// <summary>
    /// 已种植条数据（种植区，可选）：已种植格数 / 区域总格数，无缓动。
    /// 统计值来自按刻缓存的 RefreshZoneCache。
    /// </summary>
    private static BarRowInfo? GrowingSpec(Zone zone)
    {
        if (!enableGrowingBar || !IsGrowingZone(zone) || cachedGrowingTotal <= 0)
        {
            return null;
        }

        float fill = Mathf.Clamp01(cachedGrowingPlanted / (float)cachedGrowingTotal);

        return BuildBar(
            "BetterInspectPane.GrowingLabel".Translate(),
            GetGrowingValueText(fill),
            fill,
            growingColor,
            BarHeightFor(growingBarHeightOffset), growingFontScale,
            growingBarSpan,
            BarType.Growing);
    }

    /// <summary>
    /// 总体生长条数据（种植区，可选）：已种植作物的平均生长水平（0..1），无缓动。
    /// 统计值来自按刻缓存的 RefreshZoneCache；无作物时不显示（平均无意义）。
    /// </summary>
    private static BarRowInfo? ZoneGrowthSpec(Zone zone)
    {
        if (!enableZoneGrowthBar || !IsGrowingZone(zone) || cachedZoneGrowthCount <= 0)
        {
            return null;
        }

        float fill = Mathf.Clamp01(cachedZoneGrowth);

        return BuildBar(
            "BetterInspectPane.ZoneGrowthLabel".Translate(),
            GetZoneGrowthValueText(GetZonePlantDef(zone), fill),
            fill,
            zoneGrowthColor,
            BarHeightFor(zoneGrowthBarHeightOffset), zoneGrowthFontScale,
            zoneGrowthBarSpan,
            BarType.ZoneGrowth);
    }

    /// <summary>
    /// 剩余容量条数据（储存区 / 储存容器建筑 / 书架 / 管道网络储存容器，可选）：已用堆叠数 / 总容量，无缓动。
    /// 统计值来自按 (选中对象, 游戏刻) 缓存的 RefreshStorageCache；
    /// 管道网络储存容器（VFE 框架 CompResourceStorage）走反射读取自身内容物 / 容量（浮点），
    /// 颜色在开启「使用资源颜色」时用资源色（原样），否则用配置色。
    /// </summary>
    private static BarRowInfo? StorageSpec(ISelectable sel)
    {
        if (!enableStorageBar)
        {
            return null;
        }

        // 管道网络储存容器：显示该容器自身内容物 / 容量（浮点口径，不并入整数堆叠缓存）。
        ThingComp? pipeComp = cachedPipeResource;
        if (sel is Thing pipeThing && pipeComp != null && pipeComp.parent == pipeThing
            && PipeNetReflection.TryGetContainerData(pipeComp, out float pipeStored, out float pipeCapacity)
            && pipeCapacity > 0f)
        {
            float pipeFill = Mathf.Clamp01(pipeStored / pipeCapacity);
            Color pipeColor = useResourceColor
                && PipeNetReflection.TryGetResourceColor(pipeComp, out Color rc)
                && rc.a > 0f
                ? rc
                : storageColor;
            return BuildBar(
                "BetterInspectPane.StorageLabel".Translate(),
                GetPipeContainerValueText(pipeStored, pipeCapacity, pipeFill),
                pipeFill,
                pipeColor,
                BarHeightFor(storageBarHeightOffset), storageFontScale,
                storageBarSpan,
                BarType.Storage);
        }

        if (!(sel is Zone_Stockpile || sel is Building_Storage || sel is Building_Bookcase) || cachedStorageTotal <= 0)
        {
            return null;
        }

        float fill = Mathf.Clamp01(cachedStorageUsed / (float)cachedStorageTotal);

        return BuildBar(
            "BetterInspectPane.StorageLabel".Translate(),
            GetStorageValueText(fill),
            fill,
            storageColor,
            BarHeightFor(storageBarHeightOffset), storageFontScale,
            storageBarSpan,
            BarType.Storage);
    }

    /// <summary>
    /// 剩余鱼条数据（钓鱼区，可选）：水区剩余鱼数 / 目标数量，无缓动。
    /// 水区剩余鱼数 = 该水域当前鱼类种群（FishPopulationAt），统计值来自按刻缓存的 RefreshZoneCache。
    /// 开启标记时在条上按钓鱼区 targetPopulationPct（保留目标比例阈值）位置画阈值标记。
    /// </summary>
    private static BarRowInfo? FishingSpec(Zone zone)
    {
        if (!enableFishingBar || !(zone is Zone_Fishing fishing) || cachedFishingTarget <= 0)
        {
            return null;
        }

        float fill = Mathf.Clamp01(cachedFishingRemaining / (float)cachedFishingTarget);

        // 保留目标比例阈值标记：条填充 = 鱼群比例（种群 / 上限），与 targetPopulationPct 同一口径，
        // 标记处即原版 OverTargetPopulation 的判定分界（比例低于该值时钓鱼区停止捕鱼）。
        Action<Rect>? onBarDrawn = showFishingTargetPctMarker
            ? barRect => DrawThresholdMarker(barRect, fishing.targetPopulationPct)
            : null;

        return BuildBar(
            "BetterInspectPane.FishingLabel".Translate(),
            GetFishingValueText(fill),
            fill,
            fishingColor,
            BarHeightFor(fishingBarHeightOffset), fishingFontScale,
            fishingBarSpan,
            BarType.Fishing,
            onBarDrawn);
    }

    /// <summary>
    /// 尝试取物体的工作进度数据：制作（UnfinishedThing）与施工（Frame）为真实工作量；
    /// 建筑类（基因装配器 / 发酵桶 / 扫描类建筑 / 深钻井 / 塑形舱 / 次核扫描仪 /
    /// 机械培育器 / 自主工作台 / VEF 处理系统）均以 0..1 进度换算为剩余/总工作量。
    /// 输出 buildingSource：命中建筑来源时为 true，供数值文字在无采样进度时按百分比显示。
    /// </summary>
    private static bool TryGetWorkProgress(Thing thing, out float workLeft, out float totalWork, out bool buildingSource)
    {
        // 制作 / 施工为真实工作量，非建筑类。
        if (TryGetCraftingWorkProgress(thing, out workLeft, out totalWork))
        {
            buildingSource = false;
            return true;
        }
        // 命中任一建筑来源即为建筑类工作源。
        buildingSource = true;
        return TryGetBuildingWorkProgress(thing, out workLeft, out totalWork);
    }

    /// <summary>
    /// 是否为工作条来源（建筑类工作源：基因装配器 / 发酵桶 / 扫描类建筑 / 深钻井 / 塑形舱 /
    /// 机械培育器 / 次核扫描仪 / 培育舱 / 垃圾分解器 / 基因提取器 / VEF 处理系统 / 自主工作台）。
    /// 用于关闭 100% 自动隐藏时，对未在作业（工作已完成 / 空闲）的来源以满条显示。
    /// 制作（UnfinishedThing）/ 施工（Frame）完成即转化为成品/建筑，无「空闲」状态，故不列入。
    /// </summary>
    private static bool IsWorkSource(Thing thing)
    {
        return thing is Building_GeneAssembler
            || thing is Building_FermentingBarrel
            || thing is Building_MechGestator
            || thing is Building_SubcoreScanner
            || thing is Building_GrowthVat
            || thing is Building_GeneExtractor
            || cachedScanner != null
            || cachedVefProcessor != null
            || cachedDeepDrill != null
            || cachedBiosculpter != null
            || cachedAtomizer != null
            || FortifiedWorkTableReflection.IsAutonomousWorkTable(thing);
    }

    /// <summary>制作（UnfinishedThing）与施工（Frame）：分别给出真实剩余/总工作量（未初始化/无法确定时跳过）。</summary>
    private static bool TryGetCraftingWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (thing is UnfinishedThing uft && uft.Initialized && uft.Recipe != null)
        {
            totalWork = uft.Recipe.WorkAmountTotal(uft);
            if (totalWork > 0f)
            {
                workLeft = uft.workLeft;
                return true;
            }
        }
        if (thing is Frame frame)
        {
            totalWork = frame.WorkToBuild;
            if (totalWork > 0f)
            {
                workLeft = frame.WorkLeft;
                return true;
            }
        }
        return false;
    }

    /// <summary>建筑类工作源分派表：按 else-if 顺序依次尝试，短路即等效原链式回退。</summary>
    private static bool TryGetBuildingWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        return TryGetGeneAssemblerWorkProgress(thing, out workLeft, out totalWork)
            || TryGetFermentingWorkProgress(thing, out workLeft, out totalWork)
            || TryGetScannerWorkProgress(thing, out workLeft, out totalWork)
            || TryGetVefWorkProgress(thing, out workLeft, out totalWork)
            || TryGetDeepDrillWorkProgress(thing, out workLeft, out totalWork)
            || TryGetBiosculpterWorkProgress(thing, out workLeft, out totalWork)
            || TryGetGestatorWorkProgress(thing, out workLeft, out totalWork)
            || TryGetAutonomousWorkProgress(thing, out workLeft, out totalWork)
            || TryGetSubcoreWorkProgress(thing, out workLeft, out totalWork)
            || TryGetGrowthVatWorkProgress(thing, out workLeft, out totalWork)
            || TryGetAtomizerWorkProgress(thing, out workLeft, out totalWork)
            || TryGetGeneExtractorWorkProgress(thing, out workLeft, out totalWork);
    }

    /// <summary>把建筑类工作源的 0..1 进度换算为剩余/总工作量（统一口径：总量 100、剩余 = (1-进度)×100）。</summary>
    private static void EmitBuildingProgress(float fill, out float workLeft, out float totalWork)
    {
        totalWork = 100f;
        workLeft = Mathf.Max(0f, (1f - fill) * 100f);
    }

    /// <summary>基因装配器（Biotech）：异种胚芽组装进度（ProgressPercent 0..1），仅组装进行中显示。</summary>
    private static bool TryGetGeneAssemblerWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (thing is Building_GeneAssembler geneAssembler && geneAssembler.Working)
        {
            EmitBuildingProgress(geneAssembler.ProgressPercent, out workLeft, out totalWork);
            return true;
        }
        return false;
    }

    /// <summary>发酵桶：麦芽汁发酵进度（Progress 0..1）。有麦芽汁且未发酵完成时显示；温度损坏后自动不显示。</summary>
    private static bool TryGetFermentingWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (thing is Building_FermentingBarrel barrel
            && !barrel.Fermented
            && barrel.SpaceLeftForWort < Building_FermentingBarrel.MaxCapacity)
        {
            EmitBuildingProgress(barrel.Progress, out workLeft, out totalWork);
            return true;
        }
        return false;
    }

    /// <summary>扫描类建筑（地质 / 远距离矿物扫描仪）：扫描进度（保证发现资源的进度，0..1）。</summary>
    private static bool TryGetScannerWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (cachedScanner != null && TryGetScannerProgress(cachedScanner, out float scannerFill))
        {
            EmitBuildingProgress(scannerFill, out workLeft, out totalWork);
            return true;
        }
        return false;
    }

    /// <summary>VEF 处理系统（Vanilla Expanded Framework，反射读取）：工厂等「配方」建筑的生产进度（0..1）。</summary>
    private static bool TryGetVefWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (cachedVefProcessor != null && VEFProcessorReflection.TryGetProgress(cachedVefProcessor, out float processorFill))
        {
            EmitBuildingProgress(processorFill, out workLeft, out totalWork);
            return true;
        }
        return false;
    }

    /// <summary>深钻井：挖掘进度（距下一次产出矿石的进度，0..1）。仅地下有可采资源时显示。</summary>
    private static bool TryGetDeepDrillWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (cachedDeepDrill != null && cachedDeepDrill.ValuableResourcesPresent())
        {
            EmitBuildingProgress(Mathf.Clamp01(cachedDeepDrill.ProgressToNextPortionPercent), out workLeft, out totalWork);
            return true;
        }
        return false;
    }

    /// <summary>塑形舱：当前周期进度（占用中，0..1）。总时长 = durationDays × 60000，进度 = 1 - 剩余刻数 / 总时长。</summary>
    private static bool TryGetBiosculpterWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (cachedBiosculpter != null
            && cachedBiosculpter.State == BiosculpterPodState.Occupied
            && cachedBiosculpter.CurrentCycle != null
            && BiosculpterTicksRemainingField != null)
        {
            float totalTicks = cachedBiosculpter.CurrentCycle.Props.durationDays * 60000f;
            if (BiosculpterTicksRemainingField.GetValue(cachedBiosculpter) is float ticksRemaining && totalTicks > 0f)
            {
                EmitBuildingProgress(Mathf.Clamp01(1f - ticksRemaining / totalTicks), out workLeft, out totalWork);
                return true;
            }
        }
        return false;
    }

    /// <summary>机械培育器（Biotech）：机械胚体培育的整体进度，跨培育周期连续（详见分派注释）。</summary>
    private static bool TryGetGestatorWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (thing is Building_MechGestator gestator
            && gestator.ActiveMechBill is Bill_Mech mechBill
            && mechBill.State is FormingState.Forming or FormingState.Preparing
            && mechBill.recipe != null)
        {
            // 每个配方分多个培育周期：进度 = (已完成周期数 + 当前周期进度) / 总周期数，
            // 当前周期进度 = 1 - 剩余刻数 / 单周期刻数（formingTicks 为按参考速度计的剩余刻数）。
            // 周期完成时已完成周期数 +1、当前周期从满归零，两者相抵使总体进度连续单调；
            // 首次集料的 Gathering 与成品完工的 Formed 态不显示。
            float cycleTicks = mechBill.recipe.formingTicks;
            if (mechBill.recipe.gestationCycles > 0 && cycleTicks > 0f)
            {
                float currentCycleFill = Mathf.Clamp01(1f - mechBill.formingTicks / cycleTicks);
                EmitBuildingProgress(
                    Mathf.Clamp01((mechBill.GestationCyclesCompleted + currentCycleFill) / mechBill.recipe.gestationCycles),
                    out workLeft, out totalWork);
                return true;
            }
        }
        return false;
    }

    /// <summary>自主工作台（Fortified 框架，如 Machine Printer）：按配方生产，剩余工作量单调递减直到完成。</summary>
    private static bool TryGetAutonomousWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (!FortifiedWorkTableReflection.IsAutonomousWorkTable(thing)
            || !FortifiedWorkTableReflection.TryGetWorkRemaining(thing, out float autoRemaining))
        {
            return false;
        }
        // 该建筑不暴露 0..1 进度，工作条以「首次观察到的剩余量」为总量基数计算填充；
        // 新配方开始时剩余量回升，以观察到的最大剩余量作为新的基数（取最大值，随配方推进单调）。
        // 仅在剩余量 > 0（正在制造当前阶段）时显示，空闲期不显示整条。
        if (cachedAutoWorkOwner != thing)
        {
            cachedAutoWorkOwner = thing;
            cachedAutoWorkTotal = autoRemaining;
        }
        else if (autoRemaining > cachedAutoWorkTotal)
        {
            cachedAutoWorkTotal = autoRemaining; // 进入新配方：剩余量回升，更新总量基数。
        }
        if (cachedAutoWorkTotal > 0f && autoRemaining > 0f)
        {
            EmitBuildingProgress(Mathf.Clamp01(1f - autoRemaining / cachedAutoWorkTotal), out workLeft, out totalWork);
            return true;
        }
        return false;
    }

    /// <summary>次核扫描仪（差分 / 裂解共用此类）：制造次核进度（占用中，0..1）。</summary>
    private static bool TryGetSubcoreWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (thing is Building_SubcoreScanner subcoreScanner
            && subcoreScanner.State == SubcoreScannerState.Occupied
            && SubcoreScannerFabricationTicksField != null
            && subcoreScanner.def.building.subcoreScannerTicks > 0)
        {
            float totalTicks = subcoreScanner.def.building.subcoreScannerTicks;
            float subcoreFill = SubcoreScannerFabricationTicksField.GetValue(subcoreScanner) is int ticksLeft
                ? Mathf.Clamp01(1f - ticksLeft / totalTicks)
                : 0f;
            if (subcoreFill > 0f)
            {
                EmitBuildingProgress(subcoreFill, out workLeft, out totalWork);
                return true;
            }
        }
        return false;
    }

    /// <summary>培育舱（Biotech）：工作模式进度。孵胚胎用 EmbryoGestationPct；抚养儿童用年龄/18 近似，两种均在 Working 时显示。</summary>
    private static bool TryGetGrowthVatWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (thing is Building_GrowthVat vat && vat.Working)
        {
            if (vat.selectedEmbryo != null)
            {
                EmitBuildingProgress(vat.EmbryoGestationPct, out workLeft, out totalWork);
                return true;
            }
            if (vat.SelectedPawn is Pawn child)
            {
                EmitBuildingProgress(Mathf.Clamp01(child.ageTracker.AgeBiologicalYearsFloat / 18f),
                    out workLeft, out totalWork);
                return true;
            }
        }
        return false;
    }

    /// <summary>垃圾分解器（Biotech）：整批分解剩余进度（= 1 - 剩余刻数 / 总刻数）。有内容时显示。</summary>
    private static bool TryGetAtomizerWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (cachedAtomizer != null && !cachedAtomizer.Empty)
        {
            int total = cachedAtomizer.TotalStackCount * cachedAtomizer.TicksPerAtomize;
            if (total > 0)
            {
                EmitBuildingProgress(Mathf.Clamp01(1f - (float)cachedAtomizer.TicksLeftUntilAllAtomized / total),
                    out workLeft, out totalWork);
                return true;
            }
        }
        return false;
    }

    /// <summary>基因提取器（Biotech）：基因提取进度（1 - ticksRemaining/30000，原版进度条口径）。工作状态时显示。</summary>
    private static bool TryGetGeneExtractorWorkProgress(Thing thing, out float workLeft, out float totalWork)
    {
        workLeft = 0f;
        totalWork = 0f;
        if (thing is Building_GeneExtractor extractor
            && extractor.Working
            && GeneExtractorTicksRemainingField != null
            && GeneExtractorTicksRemainingField.GetValue(extractor) is int ticksRemaining
            && ticksRemaining >= 0)
        {
            EmitBuildingProgress(Mathf.Clamp01(1f - ticksRemaining / 30000f), out workLeft, out totalWork);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 扫描类建筑扫描进度（0..1）：距上次发现以来累计扫描天数 / 保证发现天数。
    /// CompScanner.daysWorkingSinceLastFinding 为 protected 字段，走反射读取
    /// （字段缺失或保证发现天数无效时安全返回 false）。
    /// </summary>
    private static bool TryGetScannerProgress(CompScanner scanner, out float fill)
    {
        fill = 0f;
        if (ScannerDaysWorkedField == null)
        {
            return false;
        }
        float guaranteedDays = scanner.Props.scanFindGuaranteedDays;
        if (guaranteedDays <= 0f)
        {
            return false;
        }
        fill = Mathf.Clamp01((float)ScannerDaysWorkedField.GetValue(scanner) / guaranteedDays);
        return true;
    }

    // 剩余时间估算的速度平滑参数（游戏刻）：
    // RateSmoothWindowTicks 为指数滑动平均（EMA）的有效时间常数，越长越稳但反应越慢；
    // 同时作为停顿判定「相对间隔」的下限（见 AddProgressSample：本次间隔 > max(该值, 上次间隔×3)
    // 即视为停顿），避免把很小的批量间隔误判为停顿，并兼容优化 mod 拉长 TickInterval。
    private const int RateSmoothWindowTicks = 30;

    // 显示值允许「回升」的确认刻数下限：真实变慢（换更慢的工人、受伤等）会让估计值持续高于当前显示值，
    // 需持续达到确认窗口才确认并一次性回升，以排除速率噪声造成的瞬态回升。实际窗口取
    // max(该值, 本条 EMA 时间常数 smoothTicks)：既保证 ≥3×基准常数，又不短于本条平滑时长
    // （否则会在平滑过渡途中就确认，分成多步跳变）。工作条 = 90，研究条 = 300。
    private const int RemainingRaiseConfirmTicks = RateSmoothWindowTicks * 3;

    // 研究条测速窗口的相对进度阈值：窗口累计进度达到「窗口起点剩余量 × 该比例」才测速，
    // 使测量的相对误差（≈ float 在该量级的分辨率 / 累计进度）≲ 0.5%。研究每刻只加约 0.008 点，
    // 而剩余量 = Cost − ProgressReal 是两个大数相减，float 分辨率占比很高，必须靠该阈值把窗口
    // 拉长到足够进度；工作条增量大，不需要（传 0 即每段测速）。
    private const float ResearchMinDeltaFraction = 2.4e-5f;
    // 研究条测速窗口的最长刻数上限：速率极低时避免窗口无限拉长导致反应过慢（精度略降）。
    private const int ResearchMaxWindowTicks = 300;
    // 研究条速率的 EMA 时间常数（刻）：研究测量间隔可达约 30~300 刻，若沿用工作条的 30 刻，
    // alpha 会被钳到 1（每次测量直接替换 avgRate、无跨窗口平均），慢速研究下测量噪声便直接透出成抖动。
    // 取远大于测量间隔的值才能真正平滑；代价是反映速率变化约需该刻数。
    private const int ResearchSmoothTicks = 300;

    /// <summary>
    /// 记录当前剩余量采样（按游戏刻）：只在观察到「进展」（剩余量下降）时测量每刻进度
    /// （窗口剩余量下降量 / 窗口刻数），并对其做 EMA（时间常数约 smoothTicks 刻）作为
    /// 速度估计，供剩余时间估算使用；同一刻多次调用只刷新当前剩余量。
    /// 之所以跨整个窗口测速，是因为 RimWorld 用 TickInterval(delta) 把工作量按 delta 批量施加
    /// （每 UpdateRateTicks 刻才推进一次，该间隔随镜头缩放为 1..15，优化 mod 还可能更大）：
    /// 逐刻点采样会把批量尖峰误当成常速而高估约 delta 倍、剩余时间偏短，且游戏速度越快尖峰越被
    /// 摊平、反而越准，出现「速度越快显示时间越长」的怪象。跨窗口测速后批量间隔完整计入分母。
    /// 停顿用「相对间隔」判定而非固定刻阈值（兼容任意 TickInterval）：含停顿的窗口会稀释速率，
    /// 直接丢弃；minDeltaFraction &gt; 0 时窗口还要累计到「进度 ≥ 起点剩余量 × 该比例」才测速
    /// （研究条专用），maxWindowTicks &gt; 0 为窗口刻数上限。
    /// resetOnOwnerChange 为 true 时切换选中物体即清空采样（工作条）；为 false 时不随选中切换清空（研究条）。
    /// </summary>
    private static void AddProgressSample(ref ProgressSample s, Thing thing, float left,
        bool resetOnOwnerChange = true, float minDeltaFraction = 0f, int maxWindowTicks = 0,
        int smoothTicks = RateSmoothWindowTicks)
    {
        int ticks = Find.TickManager.TicksGame;
        // 回升确认窗口：至少 3×基准时间常数，且不小于本条 EMA 时间常数（研究条平滑更久，
        // 若仍用 90 刻会在平滑过渡的中途就确认回升，导致分成多步跳变而非一次干净跳变）。
        s.raiseConfirmTicks = Mathf.Max(RemainingRaiseConfirmTicks, smoothTicks);
        if (resetOnOwnerChange && s.owner != thing)
        {
            s.owner = thing;
            s.lastTick = ticks;
            s.lastLeft = left;
            s.segTick = ticks;
            s.segLeft = left;
            s.progTick = ticks;
            s.lastGap = 0;
            s.segValid = false;   // 新物体的首段仅作基准，不参与测速。
            s.activeTicks = 0;
            s.shownTicks = -1;    // 清空显示缓存。
            s.raiseSinceTick = 0;
            s.avgRate = 0f;
            return;
        }

        if (s.lastTick == ticks)
        {
            // 同一刻（重绘 / 多事件）：仅刷新当前剩余量。
            s.lastLeft = left;
            return;
        }
        s.lastTick = ticks;
        s.lastLeft = left;

        // 完成 / 空闲（剩余量归零）或剩余量回升（进入新周期 / 重置）：重置测速窗口与显示缓存，
        // 不做测速（避免完成瞬间的骤降、或空闲时回填的 0 污染平均速率）。
        if (left <= 0f || left > s.segLeft)
        {
            s.segTick = ticks;
            s.segLeft = left;
            s.progTick = ticks;
            s.segValid = false;
            s.shownTicks = -1;
            s.raiseSinceTick = 0;
            return;
        }

        // 本刻未取得进展（批量 TickInterval 的间隔刻，或工人暂停 / 离去）：保持窗口起点不变，
        // 让下一次进展跨过整段间隔来测速。是否停顿改由进展时的「相对间隔」判定（见下），
        // 不再用固定刻阈值，从而兼容优化 mod 造成的任意 TickInterval 间隔。
        if (left >= s.segLeft)
        {
            return;
        }

        // 取得进展。
        int gap = ticks - s.progTick;
        s.progTick = ticks;
        // 停顿判定（无固定阈值）：本次间隔远大于上次间隔，多半中间有停顿；含停顿的窗口会把
        // 停顿时长算进分母、稀释速率，故丢弃并重置窗口（本进展点作新窗口起点）。
        // 注意不更新 lastGap：让它始终代表「正常间隔」，否则停顿时长会被当成正常间隔，
        // 使后续的停顿判定与「暂停即回退显示」判定失准。
        if (s.lastGap > 0 && gap > Mathf.Max(RateSmoothWindowTicks, s.lastGap * 3))
        {
            s.segTick = ticks;
            s.segLeft = left;
            s.segValid = true;
            return;
        }
        s.lastGap = gap;

        if (!s.segValid)
        {
            // 窗口刚重置：本点仅作窗口起点，不测速。
            s.segTick = ticks;
            s.segLeft = left;
            s.segValid = true;
            return;
        }

        int span = ticks - s.segTick;
        float delta = s.segLeft - left;
        // 精度自适应：累计进度达到起点剩余量的 minDeltaFraction（或窗口到达上限）才测速；
        // minDeltaFraction ≤ 0 表示每段即测（工作条）。
        bool enough = minDeltaFraction <= 0f
            || delta >= s.segLeft * minDeltaFraction
            || (maxWindowTicks > 0 && span >= maxWindowTicks);
        if (!enough)
        {
            return; // 继续累计，不推进窗口起点。
        }

        if (span > 0)
        {
            float instRate = delta / span; // 每刻进度
            // 按刻加权 EMA：时间常数约 smoothTicks 刻，预热期权重更大（首次即取当前值）。
            // 注意 alpha = span / min(累计刻数, smoothTicks)：若 smoothTicks ≤ span（测量间隔），
            // alpha 会被钳到 1、每次测量都直接替换 avgRate，等于没有跨窗口平均。研究条测量间隔较长
            // （精度自适应窗口约 30~300 刻），故必须传入远大于该间隔的 smoothTicks 才能真正平滑。
            s.activeTicks = Mathf.Min(s.activeTicks + span, smoothTicks);
            float alpha = Mathf.Clamp01((float)span / Mathf.Max(1, s.activeTicks));
            s.avgRate = s.avgRate + (instRate - s.avgRate) * alpha;
        }
        s.segTick = ticks;
        s.segLeft = left;
    }

    /// <summary>
    /// 依据滑动平均后的剩余量下降速度估算完成剩余量所需的游戏刻数：剩余时间 = 剩余量 / 每刻完成量。
    /// 剩余量为 0（已完成）或尚无有效速率估计（avgRate ≤ 0）时返回 -1，调用方退回显示剩余量。
    /// 不做「过期」判定：停顿期间保持上一次速率继续给出估计，由 UpdateShownRemaining 以只减不增的
    /// 缓存冻结显示，既避免在「时间」与「剩余量」格式间来回闪烁，也兼容优化 mod 拉长 TickInterval。
    /// </summary>
    private static int EstimateRemainingTicks(ref ProgressSample s, float left)
    {
        // left <= 0 时回退：完成瞬间剩余量骤降到 0，速率估计已无意义。
        if (left <= 0f || s.avgRate <= 0f)
        {
            return -1;
        }
        float remainingTicks = left / s.avgRate;
        if (remainingTicks < 0f || remainingTicks > int.MaxValue || float.IsNaN(remainingTicks))
        {
            return -1;
        }
        // left 与 avgRate 均 > 0，商必为正，无需再钳下限。
        return Mathf.RoundToInt(remainingTicks);
    }

    /// <summary>
    /// 把本次估算并入显示缓存，返回应显示的剩余刻数（-1 表示尚无可用估计）。
    /// 真实剩余时间随进度单调不增，故默认「只减不增」：估计值回升多半是速率噪声，直接抑制。
    /// 但为反映真实变慢（换更慢的工人等），当估计值持续高于当前显示值达本条确认窗口（raiseConfirmTicks，
    /// = max(RemainingRaiseConfirmTicks, smoothTicks)）时，才确认变慢并一次性回升到新估计；此后再次偏高
    /// 需重新累计。确认停顿（相对间隔判定）时返回 -1，退回显示剩余工作量 / 百分比；raw 无效
    /// （速率被稀释到溢出等）且未停顿时保留上一次显示值，避免因瞬时无效而在两种格式间闪烁。
    /// </summary>
    private static int UpdateShownRemaining(ref ProgressSample s, int raw)
    {
        int ticks = Find.TickManager.TicksGame;
        // 确认停顿（用相对间隔判定，兼容任意 TickInterval / 优化 mod）：距上次进展超过
        // max(基准常数, 上次正常间隔×3) 即认为暂停，此时退回显示剩余工作量 / 百分比，
        // 不再显示冻结的旧时间估计。这里只清「变慢」计数、不动 shownTicks，以便恢复后单调衔接。
        if (ticks - s.progTick > Mathf.Max(RateSmoothWindowTicks, s.lastGap * 3))
        {
            s.raiseSinceTick = 0;
            return -1;
        }
        if (raw < 0)
        {
            return s.shownTicks;
        }
        // 首次估计，或估计值不高于显示值（正常推进 / 变快）：直接采用并结束回升计数。
        if (s.shownTicks < 0 || raw <= s.shownTicks)
        {
            s.shownTicks = raw;
            s.raiseSinceTick = 0;
            return s.shownTicks;
        }
        // 估计值高于显示值：疑似变慢，需持续偏高达到滞回窗口才确认。
        if (s.raiseSinceTick == 0)
        {
            s.raiseSinceTick = ticks;
        }
        else if (ticks - s.raiseSinceTick >= Mathf.Max(1, s.raiseConfirmTicks))
        {
            s.shownTicks = raw;
            s.raiseSinceTick = 0;
        }
        return s.shownTicks;
    }

    #endregion

    #region Pawn 条

    /// <summary>
    /// Pawn 的可见条：按 pawnBarOrder 依次取健康（总体健康）/ 血液 / 心情 / 食物 / 休息 / 娱乐 / 机械能量条，
    /// 统一更新缓动后返回。条本身是否可见由各 Spec 自行判断
    /// （无对应数据时自动隐藏，如动物无心情/休息/娱乐、机械体无食物但有能量）。
    /// </summary>
    private static List<BarRowInfo> CollectPawnBars(Pawn pawn)
    {
        BeginEaseFrame(pawn);
        List<BarRowInfo> bars = CollectSpecs(pawnBarOrder, type => PawnBarSpec(type, pawn));
        EaseCollectedBars(bars);
        return bars;
    }

    /// <summary>按条标识取 Pawn 面板对应条的数据；条不可用（未启用/数据缺失）时返回 null。</summary>
    private static BarRowInfo? PawnBarSpec(BarType type, Pawn pawn)
    {
        // Bounded Rationality 同步：信息未知时按 BR 对原版面板各区块的隐藏口径隐藏对应条
        //（健康/血液/疼痛 → Health，心情/食物/休息/娱乐/机械能量/模组需求 → Needs，
        // 产物/产毛/繁殖 → Basic；见 BoundedRationalityReflection）。
        if (!BoundedRationalityReflection.IsPawnBarKnown(type, pawn))
        {
            return null;
        }
        switch (type)
        {
            case BarType.Health: return PawnHealthSpec(pawn);
            case BarType.Bleed: return BloodSpec(pawn);
            case BarType.Mood: return MoodSpec(pawn);
            case BarType.Food: return FoodSpec(pawn);
            case BarType.Rest: return RestSpec(pawn);
            case BarType.Joy: return JoySpec(pawn);
            case BarType.MechEnergy: return MechEnergySpec(pawn);
            case BarType.Milk: return MilkSpec(pawn);
            case BarType.Wool: return WoolSpec(pawn);
            case BarType.Breeding: return BreedingSpec(pawn);
            case BarType.Pain: return PainSpec(pawn);
            case BarType.Bladder: return BladderSpec(pawn);
            case BarType.Hygiene: return HygieneSpec(pawn);
            case BarType.Thirst: return ThirstSpec(pawn);
            default: return null;
        }
    }

    /// <summary>
    /// Pawn 健康条数据：填充 = 总体健康（SummaryHealthPercent，与原版迷你健康条一致）。
    /// 数值样式显示总体状态标签（HealthUtility.GetGeneralConditionLabel 短版，同原版），
    /// 百分比样式显示百分比；高度/字号/占位/数值样式用 Pawn 独立的健康条设置（与物品/建筑健康条互相独立），
    /// 颜色取通用 full/mid/low 分级，白色字体可单独开关，无自爆阈值标记。
    /// </summary>
    private static BarRowInfo PawnHealthSpec(Pawn pawn)
    {
        float fill = Mathf.Clamp01(pawn.health.summaryHealth.SummaryHealthPercent);
        return BuildBar(
            "BetterInspectPane.PawnHealthLabel".Translate(),
            FormatValueText(pawnHealthValueStyle, FormatPercentText(fill, 0), fill),
            fill,
            GetHealthColor(fill),
            BarHeightFor(pawnHealthBarHeightOffset), pawnHealthFontScale,
            pawnHealthBarSpan,
            BarType.Health);
    }

    /// <summary>
    /// 血液条数据（可选）：当前血液量 = 1 - 失血严重度（HediffDefOf.BloodLoss 的 Severity），
    /// 与其它条一致显示当前水平（满血 = 满条，失血越多越低）。
    /// 满血（无失血 Hediff）时自动隐藏开启则隐藏整条，关闭则以满条显示。
    /// </summary>
    private static BarRowInfo? BloodSpec(Pawn pawn)
    {
        if (!enableBloodBar || !pawn.health.CanBleed)
        {
            return null;
        }
        Hediff? bloodLoss = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
        float fill = Mathf.Clamp01(1f - (bloodLoss?.Severity ?? 0f));
        if (fill >= 1f && autoHideBloodAtFull)
        {
            return null;
        }

        // 数值样式：正在失血（总出血速率有效）时显示失血致死所需时间（游戏时间格式），否则显示当前血液比例。
        string numericText = fill.ToStringPercent();
        if (bloodValueStyle == ValueDisplayStyle.Numeric)
        {
            int deathTicks = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);
            if (deathTicks != int.MaxValue)
            {
                numericText = deathTicks.ToStringTicksToPeriod();
            }
        }

        return BuildBar(
            "BetterInspectPane.BloodLabel".Translate(),
            FormatValueText(bloodValueStyle, numericText, fill),
            fill,
            bloodColor,
            BarHeightFor(bloodBarHeightOffset), bloodFontScale,
            bloodBarSpan,
            BarType.Bleed,
            barRect => DrawBleedThresholdMarkers(barRect));
    }

    /// <summary>
    /// Pawn 需求条（心情 / 食物 / 休息 / 娱乐 / 机械能量共用）：
    /// 无对应需求（动物无心情/休息/娱乐、机械体无食物但有能量等）时隐藏；
    /// 填充 = 需求当前比例（CurLevelPercentage），数值样式显示百分比。
    /// tooltipNeed 记录对应需求，悬停条区时显示原版需求提示（统一悬浮提示开关控制）。
    /// </summary>
    private static BarRowInfo? PawnNeedSpec(bool barEnabled, string labelKey, Need? need,
        float heightOffset, float fontScale, Color color, BarSpan span, ValueDisplayStyle style, BarType type,
        Action<Rect>? onBarDrawn = null)
    {
        if (!barEnabled || need == null)
        {
            return null;
        }

        float fill = Mathf.Clamp01(need.CurLevelPercentage);

        return BuildBar(
            labelKey.Translate(),
            FormatValueText(style, fill.ToStringPercent(), fill),
            fill,
            color,
            BarHeightFor(heightOffset), fontScale,
            span,
            type,
            onBarDrawn,
            tooltipNeed: need);
    }

    private static BarRowInfo? MoodSpec(Pawn pawn)
    {
        Need? mood = pawn.needs?.mood;
        return PawnNeedSpec(enableMoodBar, "BetterInspectPane.MoodLabel".Translate(),
            mood, moodBarHeightOffset, moodFontScale, moodColor, moodBarSpan, moodValueStyle, BarType.Mood,
            mood != null ? barRect => DrawMoodThresholdMarkers(barRect, pawn) : null);
    }

    private static BarRowInfo? FoodSpec(Pawn pawn) => PawnNeedSpec(enableFoodBar, "BetterInspectPane.FoodLabel",
        pawn.needs?.food, foodBarHeightOffset, foodFontScale, foodColor, foodBarSpan, foodValueStyle, BarType.Food);

    private static BarRowInfo? RestSpec(Pawn pawn) => PawnNeedSpec(enableRestBar, "BetterInspectPane.RestLabel",
        pawn.needs?.rest, restBarHeightOffset, restFontScale, restColor, restBarSpan, restValueStyle, BarType.Rest);

    private static BarRowInfo? JoySpec(Pawn pawn) => PawnNeedSpec(enableJoyBar, "BetterInspectPane.JoyLabel",
        pawn.needs?.joy, joyBarHeightOffset, joyFontScale, joyColor, joyBarSpan, joyValueStyle, BarType.Joy);

    private static BarRowInfo? MechEnergySpec(Pawn pawn) => PawnNeedSpec(enableMechEnergyBar, "BetterInspectPane.MechEnergyLabel",
        pawn.needs?.energy, mechEnergyBarHeightOffset, mechEnergyFontScale, mechEnergyColor, mechEnergyBarSpan, mechEnergyValueStyle, BarType.MechEnergy);

    /// <summary>
    /// 动物产物条数据（可选）：可挤奶动物（有 CompMilkable）的奶水充盈进度，无缓动。
    /// 填充 = Fullness（0..1，1 = 可以挤奶）；刚挤过奶（Fullness 为 0，或组件未激活不增长）时不显示。
    /// 数值样式显示距奶水填满的剩余时间（按组分间隔与生长速度精确计算，同 CompTick 增量口径）。
    /// </summary>
    private static BarRowInfo? MilkSpec(Pawn pawn)
    {
        if (!enableMilkBar || cachedMilkable == null || cachedMilkable.parent != pawn)
        {
            return null;
        }
        float fill = Mathf.Clamp01(cachedMilkable.Fullness);
        if (fill <= 0f)
        {
            return null;
        }
        int remainingTicks = TryGetMilkRemainingTicks(pawn, cachedMilkable, out int milkTicks) ? milkTicks : -1;

        return BuildBar(
            "BetterInspectPane.MilkLabel".Translate(),
            GetMilkValueText(fill, remainingTicks),
            fill,
            milkColor,
            BarHeightFor(milkBarHeightOffset), milkFontScale,
            milkBarSpan,
            BarType.Milk);
    }

    /// <summary>
    /// 动物产毛条数据（可选）：可剪毛动物（有 CompShearable）的羊毛生长进度，无缓动。
    /// 填充 = Fullness（0..1，1 = 可以剪毛）；刚剪过毛（Fullness 为 0，或组件未激活不增长）时不显示。
    /// 数值样式显示距羊毛长满的剩余时间（按组分间隔与生长速度精确计算，同 CompTick 增量口径）。
    /// </summary>
    private static BarRowInfo? WoolSpec(Pawn pawn)
    {
        if (!enableWoolBar || cachedShearable == null || cachedShearable.parent != pawn)
        {
            return null;
        }
        float fill = Mathf.Clamp01(cachedShearable.Fullness);
        if (fill <= 0f)
        {
            return null;
        }
        int remainingTicks = TryGetWoolRemainingTicks(pawn, cachedShearable, out int woolTicks) ? woolTicks : -1;

        return BuildBar(
            "BetterInspectPane.WoolLabel".Translate(),
            GetWoolValueText(fill, remainingTicks),
            fill,
            woolColor,
            BarHeightFor(woolBarHeightOffset), woolFontScale,
            woolBarSpan,
            BarType.Wool);
    }

    /// <summary>
    /// 动物繁殖条数据（可选）：下蛋动物（CompEggLayer）显示产蛋进度，胎生动物显示怀孕（妊娠）进度，
    /// 两者只取其一（下蛋动物没有怀孕 hediff）。无缓动。
    /// 产蛋进度 = CompEggLayer.eggProgress（private 字段反射读取，与游戏内 EggProgress 口径一致）；
    /// 妊娠进度 = Hediff_Pregnant.GestationProgress（0..1，接近 1 临产）。
    /// 进度刚起步（0）时不显示；数值样式显示距产蛋/分娩的剩余时间（按周期天数与生长速度精确计算）。
    /// </summary>
    private static BarRowInfo? BreedingSpec(Pawn pawn)
    {
        if (!enableBreedingBar)
        {
            return null;
        }

        float fill = 0f;
        int remainingTicks = -1; // -1 = 无法估计剩余时间（退回显示百分比）。
        if (cachedEggLayer != null && cachedEggLayer.parent == pawn)
        {
            // 下蛋动物：读取产蛋进度（eggProgress 只在组件激活时增长，未激活恒为 0）。
            if (EggLayerProgressField != null
                && EggLayerProgressField.GetValue(cachedEggLayer) is float eggProgress)
            {
                fill = Mathf.Clamp01(eggProgress);
                if (TryGetEggRemainingTicks(eggProgress, out int eggTicks))
                {
                    remainingTicks = eggTicks;
                }
            }
        }
        else
        {
            // 胎生动物（含人类）：怀孕（HediffDefOf.Pregnant）的妊娠进度。
            Hediff? pregnant = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant);
            if (pregnant is Hediff_Pregnant hp)
            {
                fill = Mathf.Clamp01(hp.GestationProgress);
                if (TryGetPregnancyRemainingTicks(pawn, hp, out int pregTicks))
                {
                    remainingTicks = pregTicks;
                }
            }
        }
        if (fill <= 0f)
        {
            return null;
        }

        return BuildBar(
            "BetterInspectPane.BreedingLabel".Translate(),
            GetBreedingValueText(fill, remainingTicks),
            fill,
            breedingColor,
            BarHeightFor(breedingBarHeightOffset), breedingFontScale,
            breedingBarSpan,
            BarType.Breeding);
    }

    /// <summary>
    /// Pawn 疼痛条数据（可选）：填充 = 疼痛比例（hediffSet.PainTotal，可超过 1 表示剧痛，条按 0..1 截断）。
    /// 无疼痛（0%）时自动隐藏开启则隐藏，关闭则以 0 填充显示；数值样式按实际疼痛比例显示。
    /// 开启「反转疼痛条」时填充 = 1 - 疼痛比例（由满渐空，数值仍按实际疼痛比例显示）。
    /// </summary>
    private static BarRowInfo? PainSpec(Pawn pawn)
    {
        if (!enablePainBar || !pawn.RaceProps.IsFlesh)
        {
            return null;
        }

        float pain = Mathf.Max(0f, pawn.health.hediffSet.PainTotal);
        if (pain <= 0f && autoHidePainAtZero)
        {
            return null;
        }
        float fill = Mathf.Clamp01(pain);
        if (invertPainFill)
        {
            fill = 1f - fill;
        }

        return BuildBar(
            (invertPainFill ? "BetterInspectPane.PainToleranceLabel" : "BetterInspectPane.PainLabel").Translate(),
            FormatValueText(painValueStyle, Mathf.RoundToInt(pain * 100f) + "%", fill),
            fill,
            painColor,
            BarHeightFor(painBarHeightOffset), painFontScale,
            painBarSpan,
            BarType.Pain,
            barRect => DrawPainThresholdMarkers(barRect, pawn, invertPainFill));
    }

    /// <summary>
    /// Pawn 模组需求条（膀胱 / 卫生 / 口渴共用）：只有 Pawn 有对应需求（按 defName 匹配，
    /// 如 Dubs Bad Hygiene 的 Bladder / Hygiene / DBHThirst）才显示，不依赖具体模组判断。
    /// 填充 = 需求当前比例（CurLevelPercentage）。
    /// invert 为 true（膀胱/口渴的「反转需求比例」选项）时填充 = 1 - 当前比例（如 80%→20%）。
    /// tooltipNeed 记录对应需求，悬停条区时显示原版需求提示（统一悬浮提示开关控制）。
    /// </summary>
    private static BarRowInfo? DbhNeedSpec(bool barEnabled, string labelKey, Need? need, bool invert,
        float heightOffset, float fontScale, Color color, BarSpan span, ValueDisplayStyle style, BarType type)
    {
        if (!barEnabled || need == null)
        {
            return null;
        }

        float fill = Mathf.Clamp01(need.CurLevelPercentage);
        if (invert)
        {
            fill = 1f - fill;
        }

        return BuildBar(
            labelKey.Translate(),
            FormatValueText(style, fill.ToStringPercent(), fill),
            fill,
            color,
            BarHeightFor(heightOffset), fontScale,
            span,
            type,
            tooltipNeed: need);
    }

    /// <summary>膀胱条（Dubs Bad Hygiene 的 Bladder 需求等）：默认显示当前水平，可反转需求比例。</summary>
    private static BarRowInfo? BladderSpec(Pawn pawn) => DbhNeedSpec(enableBladderBar, "BetterInspectPane.BladderLabel",
        cachedBladderNeed, invertBladderFill, bladderBarHeightOffset, bladderFontScale, bladderColor,
        bladderBarSpan, bladderValueStyle, BarType.Bladder);

    /// <summary>卫生条（Dubs Bad Hygiene 的 Hygiene 需求等）：显示当前干净程度（无反转选项）。</summary>
    private static BarRowInfo? HygieneSpec(Pawn pawn) => DbhNeedSpec(enableHygieneBar, "BetterInspectPane.HygieneLabel",
        cachedHygieneNeed, invert: false, hygieneBarHeightOffset, hygieneFontScale, hygieneColor,
        hygieneBarSpan, hygieneValueStyle, BarType.Hygiene);

    /// <summary>口渴条（Dubs Bad Hygiene 的 DBHThirst 需求等）：默认显示当前水平，可反转需求比例。</summary>
    private static BarRowInfo? ThirstSpec(Pawn pawn) => DbhNeedSpec(enableThirstBar, "BetterInspectPane.ThirstLabel",
        cachedThirstNeed, invertThirstFill, thirstBarHeightOffset, thirstFontScale, thirstColor,
        thirstBarSpan, thirstValueStyle, BarType.Thirst);

    #endregion

    #region 数值文字

    /// <summary>
    /// 按数值显示样式统一格式化进度条数值文字：
    /// Numeric 显示传入的具体数值文字，Percent / PercentOneDecimal / PercentTwoDecimals 显示百分比，None 不显示。
    /// </summary>
    private static string FormatValueText(ValueDisplayStyle style, string numericText, float fill, bool negative = false)
    {
        switch (style)
        {
            case ValueDisplayStyle.Numeric:
                return numericText;
            case ValueDisplayStyle.Percent:
                return FormatPercentText(fill, 0, negative);
            case ValueDisplayStyle.PercentOneDecimal:
                return FormatPercentText(fill, 1, negative);
            case ValueDisplayStyle.PercentTwoDecimals:
                return FormatPercentText(fill, 2, negative);
            default:
                return "";
        }
    }

    /// <summary>按样式格式化百分比文字：百分比（整数）、一位小数或两位小数。decimals 为小位数（0 表示整数）。</summary>
    private static string FormatPercentText(float fill, int decimals, bool negative = false)
    {
        float pct = negative ? Mathf.Clamp(fill, -1f, 1f) * 100f : Mathf.Clamp01(fill) * 100f;
        return decimals > 0 ? pct.ToString("F" + decimals) + "%" : Mathf.RoundToInt(pct) + "%";
    }

    /// <summary>健康条数值文字（按设置样式）；上限大于 9999 时只显示当前值。</summary>
    private static string GetHealthValueText(Thing thing, float fillPct)
    {
        string numericText = thing.MaxHitPoints > 9999
            ? $"{thing.HitPoints}"
            : $"{thing.HitPoints} / {thing.MaxHitPoints}";
        return FormatValueText(healthValueStyle, numericText, fillPct);
    }

    /// <summary>弹药条数值文字（按设置样式）；上限大于 9999 时只显示当前余量。</summary>
    private static string GetAmmoValueText(float fuel, float capacity, float fill)
    {
        string numericText = capacity > 9999f
            ? $"{fuel.ToStringDecimalIfSmall()}"
            : $"{fuel.ToStringDecimalIfSmall()} / {capacity.ToStringDecimalIfSmall()}";
        return FormatValueText(ammoValueStyle, numericText, fill);
    }

    /// <summary>新鲜条数值文字（按设置样式）。</summary>
    private static string GetFreshnessValueText(CompRottable rottable, float fill)
    {
        // 数值样式显示即将腐坏的时间（游戏时间，使用游戏自带的时间格式化）；
        // 冻结不腐坏时沿用原版「当前冻结」提示，避免显示 20 年之类的误导性数值。
        float rotRate = GenTemperature.RotRateAtTemperature(Mathf.RoundToInt(rottable.parent.AmbientTemperature));
        string numericText = rotRate < 0.001f
            ? "CurrentlyFrozen".Translate()
            : rottable.TicksUntilRotAtCurrentTemp.ToStringTicksToPeriod();
        return FormatValueText(freshnessValueStyle, numericText, fill);
    }

    /// <summary>
    /// 读取酿酒桶的理论剩余刻数（原版 Building_FermentingBarrel.EstimatedTicksLeft，
    /// 基于当前进度与温度速度计算，private 属性走反射读取）。
    /// 酿酒桶的进度仅在 tickRare（每约 250 刻）推进一次，相邻两刻采样无法测得真实速度，
    /// 因此不回退到采样估算。属性未解析到位或读取失败时安全返回 false。
    /// </summary>
    private static bool TryGetFermentingTicksLeft(Thing? thing, out int ticksLeft)
    {
        ticksLeft = 0;
        if (FermentingBarrelEstimatedTicksProp == null || thing is not Building_FermentingBarrel barrel)
        {
            return false;
        }
        return FermentingBarrelEstimatedTicksProp.GetValue(barrel) is int i
            && (ticksLeft = i) >= 0; // EstimatedTicksLeft 本身非负，此处仅防御性校验。
    }

    /// <summary>
    /// 机械培育器理论剩余刻数：总剩余 = 当前周期剩余刻数 + 剩余周期数 × 单周期形成刻数，
    /// 再除以 WorkSpeedMultiplier 换算为真实游戏刻（formingTicks 每刻按该倍率递减，已含速度）。
    /// 直接精确给出，避免帧采样在周期边界/暂停平台期间的失真（外推减半）与跳变（退回 %）。
    /// </summary>
    private static bool TryGetGestatorRemainingTicks(Thing? thing, out int ticks)
    {
        ticks = 0;
        if (thing is Building_MechGestator gestator
            && gestator.ActiveMechBill is Bill_Mech bill
            && bill.State is FormingState.Forming or FormingState.Preparing
            && bill.recipe != null)
        {
            int totalCycles = bill.recipe.gestationCycles;
            float cycleTicks = bill.recipe.formingTicks;
            float speedMul = bill.WorkSpeedMultiplier;
            if (totalCycles > 0 && cycleTicks > 0f && speedMul > 0f)
            {
                float remaining = (bill.formingTicks
                    + Mathf.Max(0, totalCycles - bill.GestationCyclesCompleted - 1) * cycleTicks) / speedMul;
                ticks = Mathf.Max(0, Mathf.RoundToInt(remaining));
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 培育舱理论剩余刻数：仅胚胎模式（孵化时长固定，startTick - TicksGame）可精确求得；
    /// 抚养儿童模式无理论剩余刻数，不在此提供（退回帧采样估算）。
    /// </summary>
    private static bool TryGetGrowthVatRemainingTicks(Thing? thing, out int ticks)
    {
        ticks = 0;
        if (thing is Building_GrowthVat vat && vat.Working && vat.selectedEmbryo != null && vat.EmbryoGestationTicksRemaining >= 0)
        {
            ticks = vat.EmbryoGestationTicksRemaining;
            return true;
        }
        return false;
    }

    /// <summary>垃圾分解器理论剩余刻数：整批分解剩余刻数（TotalStackCount×TicksPerAtomize - ticksAtomized，与检查面板 FinishesIn 一致）。</summary>
    private static bool TryGetAtomizerRemainingTicks(Thing? thing, out int ticks)
    {
        ticks = 0;
        if (cachedAtomizer is CompAtomizer a && cachedAtomizer.parent == thing && !a.Empty)
        {
            ticks = Mathf.Max(0, a.TicksLeftUntilAllAtomized);
            return true;
        }
        return false;
    }

    /// <summary>基因提取器理论剩余刻数：当前提取剩余刻数（ticksRemaining，private 反射读取）。</summary>
    private static bool TryGetGeneExtractorRemainingTicks(Thing? thing, out int ticks)
    {
        ticks = 0;
        if (thing is Building_GeneExtractor ex && ex.Working
            && GeneExtractorTicksRemainingField != null
            && GeneExtractorTicksRemainingField.GetValue(ex) is int v && v >= 0)
        {
            ticks = v;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 工作进度条数值文字（按设置样式），workLeft 为剩余工作量。
    /// 数值样式下：若最近两刻检测到工作量在增加（有工人在作业），则显示按该速度估算的
    /// 剩余制作/施工时间；否则仍显示剩余工作量（与原版 "Work left" 信息一致）。
    /// 建筑类工作条（基因装配器 / 发酵桶 / 扫描类建筑）无采样进度时退回显示已完成百分比，
    /// 与原版检查面板（"Progress: X%" 等）口径一致，避免把剩余量误读为进度。
    /// 酿酒桶、VEF 处理系统工厂、自主工作台（Fortified）为脉冲式推进（tickRare 每 250 刻才推进一次），
    /// 采样估算不稳定，直接按其理论剩余刻数显示剩余时间。
    /// 其余样式由 FormatValueText 统一处理（这里传入的数值文字会被忽略）。
    /// </summary>
    private static string GetWorkValueText(float workLeft, float fill, bool buildingSource)
    {
        // VEF 处理系统（工厂等配方建筑）：脉冲式推进，跳过采样估算，
        // 直接使用流程的理论剩余刻数（Process.TickLeft，游戏刻）作为剩余时间。
        if (cachedVefProcessor != null && IsVefProcessorSource(workSample.owner)
            && VEFProcessorReflection.TryGetRemainingTicks(cachedVefProcessor, out int processorTicksLeft))
        {
            return FormatValueText(workValueStyle,
                processorTicksLeft.ToStringTicksToPeriod(), fill);
        }

        // 自主工作台（Fortified，如 Machine Printer）：每 tickRare（每约 250 刻）才推进一次，
        // 采样估算不稳定，直接按其剩余工作量换算的理论剩余刻数显示剩余时间。
        if (workSample.owner is Thing autoThing
            && FortifiedWorkTableReflection.IsAutonomousWorkTable(autoThing)
            && FortifiedWorkTableReflection.TryGetRemainingTicks(autoThing, out int autoTicksLeft))
        {
            return FormatValueText(workValueStyle,
                autoTicksLeft.ToStringTicksToPeriod(), fill);
        }

        // 酿酒桶：进度仅在 tickRare（每 250 刻）推进一次，相邻两刻采样无法测得真实速度，
        // 因此直接使用原版按当前进度与温度速度计算的 EstimatedTicksLeft（与检查面板口径一致）。
        if (TryGetFermentingTicksLeft(workSample.owner, out int barrelTicksLeft))
        {
            return FormatValueText(workValueStyle,
                barrelTicksLeft.ToStringTicksToPeriod(), fill);
        }

        // 机械培育器：总剩余刻数可精确求得（含剩余周期与速度倍率），
        // 帧采样在周期边界/暂停平台会失真减半或退回 %，故直接显示理论值。
        if (TryGetGestatorRemainingTicks(workSample.owner, out int gestatorTicksLeft))
        {
            return FormatValueText(workValueStyle,
                gestatorTicksLeft.ToStringTicksToPeriod(), fill);
        }

        // 培育舱（孵化胚胎）：总剩余刻数可精确求得（startTick - 当前刻），直接显示理论值。
        if (TryGetGrowthVatRemainingTicks(workSample.owner, out int growthVatTicksLeft))
        {
            return FormatValueText(workValueStyle,
                growthVatTicksLeft.ToStringTicksToPeriod(), fill);
        }

        // 垃圾分解器：整批分解剩余刻数可精确求得（TotalStackCount×TicksPerAtomize - ticksAtomized），直接显示理论值。
        if (TryGetAtomizerRemainingTicks(workSample.owner, out int atomizerTicksLeft))
        {
            return FormatValueText(workValueStyle,
                atomizerTicksLeft.ToStringTicksToPeriod(), fill);
        }

        // 基因提取器：剩余刻数可精确求得（ticksRemaining），直接显示理论值。
        if (TryGetGeneExtractorRemainingTicks(workSample.owner, out int geneExtractorTicksLeft))
        {
            return FormatValueText(workValueStyle,
                geneExtractorTicksLeft.ToStringTicksToPeriod(), fill);
        }

        int shownTicks = UpdateShownRemaining(ref workSample, EstimateRemainingTicks(ref workSample, workLeft));
        string numericText;
        if (shownTicks >= 0)
        {
            numericText = shownTicks.ToStringTicksToPeriod();
        }
        else if (buildingSource)
        {
            numericText = Mathf.CeilToInt(fill * 100f) + "%";
        }
        else
        {
            numericText = workLeft.ToStringWorkAmount();
        }
        return FormatValueText(workValueStyle, numericText, fill);
    }

    /// <summary>是否为 VEF 处理系统工厂建筑（带处理系统组件的建筑）。</summary>
    private static bool IsVefProcessorSource(Thing? thing)
        => cachedVefProcessor != null && cachedVefProcessor.parent == thing;

    /// <summary>
    /// 研究进度条数值文字（按设置样式），left 为剩余研究点数。
    /// 数值样式下：已有有效速率估计（研究期间测得）时显示按该速率估算的剩余研究时间（游戏时间格式，
    /// 只减不增）；否则退回显示已完成研究点数（max - left，从 0 到上限），上限大于 9999 时只显示已完成点数。
    /// 其余样式由 FormatValueText 统一处理。
    /// </summary>
    private static string GetResearchValueText(float left, float fill, float max)
    {
        int shownTicks = UpdateShownRemaining(ref researchSample, EstimateRemainingTicks(ref researchSample, left));
        // 已完成点数（从 0 到上限）：max - left = ProgressReal。
        float done = max - left;
        string numericText = shownTicks >= 0
            ? shownTicks.ToStringTicksToPeriod()
            : max > 9999f
                ? done.ToStringDecimalIfSmall()
                : $"{done.ToStringDecimalIfSmall()} / {max.ToString("F0")}";
        return FormatValueText(researchValueStyle, numericText, fill);
    }

    /// <summary>
    /// 生长条数值文字（按设置样式）：
    /// 数值样式显示剩余生长时间（按当前生长进度与总生长天数估算，游戏时间格式），
    /// 百分比样式显示当前生长百分比；无样式不显示数值。
    /// </summary>
    private static string GetGrowthValueText(Plant plant, float fill)
    {
        // 完全生长后，数值样式显示「成熟」。
        if (fill >= 1f)
        {
            return "BetterInspectPane.Mature".Translate();
        }
        // 数值样式显示剩余生长时间（按当前生长进度与总生长天数估算，游戏时间格式）。
        int remainingTicks = Mathf.RoundToInt((1f - fill) * plant.def.plant.growDays * 60000f);
        return FormatValueText(growthValueStyle, remainingTicks.ToStringTicksToPeriod(), fill);
    }

    /// <summary>树精茧生长条数值文字（按设置样式）：数值样式显示完成剩余时间（精确值，口径同原版 TimeLeft）。</summary>
    private static string GetCocoonGrowthValueText(int remainingTicks, float fill)
    {
        // 已到完成时刻（存活至销毁前的最后一帧）：与植物口径一致按「成熟」处理。
        if (remainingTicks <= 0)
        {
            return "BetterInspectPane.Mature".Translate();
        }
        return FormatValueText(growthValueStyle, remainingTicks.ToStringTicksToPeriod(), fill);
    }

    /// <summary>已种植条数值文字（按设置样式），数值样式显示「已种植 / 总格数」；总格数大于 9999 时只显示已种植。</summary>
    private static string GetGrowingValueText(float fill)
    {
        string numericText = cachedGrowingTotal > 9999
            ? $"{cachedGrowingPlanted}"
            : $"{cachedGrowingPlanted} / {cachedGrowingTotal}";
        return FormatValueText(growingValueStyle, numericText, fill);
    }

    /// <summary>
    /// 总体生长条数值文字（按设置样式）：
    /// 数值样式按种植区设定作物（PlantDefToGrow）的总生长天数与当前平均生长水平估算剩余生长时间
    /// （游戏时间格式，口径与逐株生长条 GetGrowthValueText 一致），
    /// 百分比样式显示平均生长百分比；无样式不显示数值。
    /// </summary>
    private static string GetZoneGrowthValueText(ThingDef plantDef, float fill)
    {
        // 完全成熟：平均生长为 1 意味着所有已种植作物均已成熟，显示「成熟」（与逐株生长条口径一致）。
        if (fill >= 1f)
        {
            return "BetterInspectPane.Mature".Translate();
        }
        // 数值样式显示剩余生长时间（按种植区设定作物与当前平均生长水平估算，游戏时间格式）。
        int remainingTicks = Mathf.RoundToInt((1f - fill) * plantDef.plant.growDays * 60000f);
        return FormatValueText(zoneGrowthValueStyle, remainingTicks.ToStringTicksToPeriod(), fill);
    }

    /// <summary>剩余容量条数值文字（按设置样式），数值样式显示「已用 / 总容量」；总容量大于 9999 时只显示已用。</summary>
    private static string GetStorageValueText(float fill)
    {
        string numericText = cachedStorageTotal > 9999
            ? $"{cachedStorageUsed}"
            : $"{cachedStorageUsed} / {cachedStorageTotal}";
        return FormatValueText(storageValueStyle, numericText, fill);
    }

    /// <summary>管道网络容器容量条数值文字（按设置样式），浮点「已存 / 容量」；容量大于 9999 时只显示已存。</summary>
    private static string GetPipeContainerValueText(float stored, float capacity, float fill)
    {
        string numericText = capacity > 9999f
            ? $"{stored.ToString("F0")}"
            : $"{stored.ToString("F0")} / {capacity.ToString("F0")}";
        return FormatValueText(storageValueStyle, numericText, fill);
    }

    /// <summary>管道网络条数值文字（按设置样式），浮点「已存 / 总容量」；总容量大于 9999 时只显示已存。</summary>
    private static string GetPipeNetValueText(float stored, float capacity, float fill)
    {
        string numericText = capacity > 9999f
            ? $"{stored.ToString("F0")}"
            : $"{stored.ToString("F0")} / {capacity.ToString("F0")}";
        return FormatValueText(pipeNetValueStyle, numericText, fill);
    }

    /// <summary>
    /// 冷却条数值文字（按设置样式）：数值样式显示剩余冷却时间（游戏时间格式），
    /// 反映下次可开火前的等待；其余样式由 FormatValueText 统一处理。
    /// </summary>
    private static string GetCooldownValueText(int cooldownLeft, float fill)
    {
        return FormatValueText(cooldownValueStyle,
            cooldownLeft.ToStringTicksToPeriod(), fill);
    }

    /// <summary>散热器蓄热条数值文字（按设置样式）：数值样式显示「蓄热 / 最大蓄热」，口径同游戏检视字符串。</summary>
    private static string GetHeatsinkValueText(float stored, float max, float fill)
    {
        string numericText = max > 999f
            ? stored.ToString("F1")
            : $"{stored.ToString("F1")} / {max.ToString("F1")}";
        return FormatValueText(cooldownValueStyle, numericText, fill);
    }

    /// <summary>
    /// 充能条数值文字（按设置样式）：数值样式显示理论剩余充能时间（游戏时间格式）。
    /// 各组件均有精确可得的游戏态剩余刻数（见「采样 vs 理论」评估），不走采样估算。
    /// </summary>
    private static string GetChargeValueText(int remainingTicks, float fill)
    {
        return FormatValueText(chargeValueStyle,
            remainingTicks.ToStringTicksToPeriod(), fill);
    }

    /// <summary>电池蓄电条数值文字（按设置样式），数值样式显示「当前 / 最大」；上限大于 9999 时只显示当前值。</summary>
    private static string GetBatteryValueText(CompPowerBattery battery, float fill)
    {
        string numericText = battery.Props.storedEnergyMax > 9999f
            ? $"{battery.StoredEnergy.ToString("F0")}"
            : $"{battery.StoredEnergy.ToString("F0")} / {battery.Props.storedEnergyMax.ToString("F0")}";
        return FormatValueText(batteryValueStyle, numericText, fill);
    }

    /// <summary>电网蓄电条数值文字（按设置样式），数值样式显示「当前 / 最大 Wd」；上限大于 9999 时只显示当前值。</summary>
    private static string GetPowerGridValueText(float fill)
    {
        string numericText = cachedGridMax > 9999f
            ? $"{cachedGridStored.ToString("F0")}"
            : $"{cachedGridStored.ToString("F0")} / {cachedGridMax.ToString("F0")}";
        return FormatValueText(powerGridValueStyle, numericText, fill);
    }

    /// <summary>维护条数值文字（按设置样式）；数值样式显示维护百分比。</summary>
    private static string GetMaintenanceValueText(float fill)
    {
        return FormatValueText(maintenanceValueStyle,
            GenText.ToStringPercent(fill, "F2"), fill);
    }

    /// <summary>剩余鱼条数值文字（按设置样式），数值样式显示「水区剩余 / 目标」；目标大于 9999 时只显示剩余。</summary>
    private static string GetFishingValueText(float fill)
    {
        string numericText = cachedFishingTarget > 9999
            ? $"{cachedFishingRemaining}"
            : $"{cachedFishingRemaining} / {cachedFishingTarget}";
        return FormatValueText(fishingValueStyle, numericText, fill);
    }

    /// <summary>
    /// 产物条数值文字（按设置样式）：数值样式显示距奶水填满的剩余时间（游戏时间格式，
    /// 已满或速度无效时退回百分比）；其余样式由 FormatValueText 统一处理。
    /// </summary>
    private static string GetMilkValueText(float fill, int remainingTicks)
    {
        string numericText = fill.ToStringPercent();
        if (remainingTicks > 0)
        {
            numericText = remainingTicks.ToStringTicksToPeriod();
        }
        return FormatValueText(milkValueStyle, numericText, fill);
    }

    /// <summary>
    /// 产毛条数值文字（按设置样式）：数值样式显示距羊毛长满的剩余时间（游戏时间格式，
    /// 已满或速度无效时退回百分比）；其余样式由 FormatValueText 统一处理。
    /// </summary>
    private static string GetWoolValueText(float fill, int remainingTicks)
    {
        string numericText = fill.ToStringPercent();
        if (remainingTicks > 0)
        {
            numericText = remainingTicks.ToStringTicksToPeriod();
        }
        return FormatValueText(woolValueStyle, numericText, fill);
    }

    /// <summary>
    /// 繁殖条数值文字（按设置样式）：数值样式显示距产蛋/分娩的剩余时间（游戏时间格式，
    /// 已满或无法估计时退回百分比）；其余样式由 FormatValueText 统一处理。
    /// </summary>
    private static string GetBreedingValueText(float fill, int remainingTicks)
    {
        string numericText = fill.ToStringPercent();
        if (remainingTicks > 0)
        {
            numericText = remainingTicks.ToStringTicksToPeriod();
        }
        return FormatValueText(breedingValueStyle, numericText, fill);
    }

    /// <summary>产物条（挤奶）填满所需剩余刻数：按挤奶周期天数与生长速度精确计算（口径同 CompMilkable 基类 CompTick 增量）。</summary>
    private static bool TryGetMilkRemainingTicks(Pawn pawn, CompMilkable comp, out int ticks)
        => TryGetBodyResourceRemainingTicks(pawn, comp.Props.milkIntervalDays, comp.Fullness, out ticks);

    /// <summary>产毛条（剪毛）长满所需剩余刻数：按剪毛周期天数与生长速度精确计算（口径同 CompShearable 基类 CompTick 增量）。</summary>
    private static bool TryGetWoolRemainingTicks(Pawn pawn, CompShearable comp, out int ticks)
        => TryGetBodyResourceRemainingTicks(pawn, comp.Props.shearIntervalDays, comp.Fullness, out ticks);

    /// <summary>
    /// 可采集体资源（奶/毛）填满所需剩余刻数：剩余比例 × 单周期刻数 ÷ 生长速度。
    /// 生长速度 = PawnUtility.BodyResourceGrowthSpeed（即食物状态倍率，饥饿时该速度为 0，
    /// 资源不再增长），速度为 0 或已满时返回 false（调用方退回百分比显示）。
    /// </summary>
    private static bool TryGetBodyResourceRemainingTicks(Pawn pawn, float intervalDays, float fullness, out int ticks)
    {
        ticks = 0;
        if (intervalDays <= 0f || fullness >= 1f)
        {
            return false;
        }
        float periodTicks = intervalDays * 60000f;
        float growthSpeed = PawnUtility.BodyResourceGrowthSpeed(pawn);
        if (growthSpeed <= 0f)
        {
            return false;
        }
        ticks = Mathf.RoundToInt((1f - fullness) * periodTicks / growthSpeed);
        return true;
    }

    /// <summary>
    /// 产蛋剩余刻数：剩余进度 × 单周期刻数 ÷ 生长速度（口径同 CompEggLayer.CompTick 增量）。
    /// 未受精封顶的蛋（eggProgressUnfertilizedMax &lt; 1 且进度已到封顶）进度停止不再推进，
    /// 无法估计剩余时间，返回 false。
    /// </summary>
    private static bool TryGetEggRemainingTicks(float eggProgress, out int ticks)
    {
        ticks = 0;
        CompEggLayer? egg = cachedEggLayer;
        if (egg == null || eggProgress >= 1f)
        {
            return false;
        }
        if (egg.Props.eggProgressUnfertilizedMax < 1f && eggProgress >= egg.Props.eggProgressUnfertilizedMax)
        {
            return false; // 未受精封顶：产蛋进度停止，无剩余时间可估计。
        }
        Pawn? pawn = egg.parent as Pawn;
        return pawn != null && TryGetBodyResourceRemainingTicks(pawn, egg.Props.eggLayIntervalDays, eggProgress, out ticks);
    }

    /// <summary>
    /// 怀孕（妊娠）剩余刻数：剩余进度 × 妊娠周期刻数 ÷ 生长速度（口径同 Hediff_Pregnant.TickInterval 增量）。
    /// 已足月或生长速度为 0 时返回 false（调用方退回百分比显示）。
    /// </summary>
    private static bool TryGetPregnancyRemainingTicks(Pawn pawn, Hediff_Pregnant pregnant, out int ticks)
    {
        ticks = 0;
        if (pawn.RaceProps.gestationPeriodDays <= 0f || pregnant.GestationProgress >= 1f)
        {
            return false;
        }
        float growthSpeed = PawnUtility.BodyResourceGrowthSpeed(pawn);
        if (growthSpeed <= 0f)
        {
            return false;
        }
        ticks = Mathf.RoundToInt((1f - pregnant.GestationProgress) * pawn.RaceProps.gestationPeriodDays * 60000f / growthSpeed);
        return true;
    }

    #endregion

    #region 护盾数据

    /// <summary>
    /// 读取建筑物的护盾填充比例。
    /// 支持带 HP 上限的拦截器，以及按时间激活/充能的拦截器。
    /// </summary>
    private static float GetShieldFillPercent()
    {
        CompProjectileInterceptor? interceptor = cachedInterceptor;
        if (interceptor == null || !interceptor.Active)
        {
            return 0f;
        }

        int maxHP = interceptor.HitPointsMax;
        if (maxHP <= 0)
        {
            return GetInterceptorTimeFraction(interceptor);
        }

        int curHP = interceptor.currentHitPoints;
        if (curHP < 0)
        {
            curHP = maxHP;
        }
        return Mathf.Clamp01((float)curHP / maxHP);
    }

    /// <summary>
    /// 无 HP 上限的拦截器：按激活/充能剩余时间换算填充比例。
    /// </summary>
    private static float GetInterceptorTimeFraction(CompProjectileInterceptor interceptor)
    {
        var props = interceptor.Props;
        int ticksGame = Find.TickManager.TicksGame;

        if (props.activated)
        {
            int activatedTick = (int)ActivatedTickField.GetValue(interceptor);
            int remaining = activatedTick + props.activeDuration - ticksGame;
            if (remaining <= 0 || props.activeDuration <= 0)
            {
                return 0f;
            }
            return Mathf.Clamp01((float)remaining / props.activeDuration);
        }

        if (props.chargeIntervalTicks > 0)
        {
            int remaining = interceptor.ChargeCycleStartTick - ticksGame;
            int activeDuration = props.chargeIntervalTicks - props.chargeDurationTicks;
            if (remaining <= 0 || activeDuration <= 0)
            {
                return 0f;
            }
            return Mathf.Clamp01((float)remaining / activeDuration);
        }

        // 被动常开拦截器 + 自毁计时（如护盾投射仪）：以自毁进度显示护盾条（剩余寿命比例，从满逐步耗尽到烧毁）。
        if (TryGetShieldSelfDestruct(out int burnoutRemaining, out int burnoutTotal))
        {
            return Mathf.Clamp01((float)burnoutRemaining / burnoutTotal);
        }

        // 被动常开拦截器且无 HP 限制 → 永久满额，显示护盾条无意义。
        if (interceptor.HitPointsMax <= 0)
        {
            return 0f;
        }

        return 1f;
    }

    /// <summary>
    /// 被动常开拦截器（无 HP、无充能）且带自毁计时（CompDestroyAfterDelay，如护盾投射仪）时的自毁信息。
    /// 存在有效自毁计时时返回 true；remainingTicks 为距自毁的剩余刻数，totalTicks 为总自毁时长。
    /// </summary>
    private static bool TryGetShieldSelfDestruct(out int remainingTicks, out int totalTicks)
    {
        remainingTicks = 0;
        totalTicks = 0;
        CompDestroyAfterDelay? destroyDelay = cachedDestroyAfterDelay;
        if (destroyDelay == null || destroyDelay.Props.delayTicks <= 0)
        {
            return false;
        }
        int ticksLeft = destroyDelay.TicksLeft;
        if (ticksLeft <= 0)
        {
            return false;
        }
        totalTicks = destroyDelay.Props.delayTicks;
        remainingTicks = ticksLeft;
        return true;
    }

    /// <summary>
    /// 护盾条数值文字（按设置样式）：
    /// 数值样式下，HP 型拦截器显示「当前HP / 最大HP」，时间制充能拦截器显示剩余充能时间（游戏时间格式），
    /// 被动常开且带自毁计时的拦截器显示距自毁的剩余时间，
    /// 其余情况显示填充百分比；百分比样式总是显示百分比；无样式不显示数值。
    /// </summary>
    private static string GetShieldValueText(float shieldFill)
    {
        // 数值样式下优先「当前HP / 最大HP」，其次剩余充能时间，最后退回填充百分比。
        string? hpText = GetShieldHitPointsText();
        string numericText;
        if (hpText != null)
        {
            numericText = hpText;
        }
        else
        {
            int remaining = GetShieldChargeRemainingTicks();
            if (remaining < 0 && TryGetShieldSelfDestruct(out int burnoutTicks, out _))
            {
                // 被动常开拦截器且带自毁计时：数值显示距自毁的剩余时间。
                remaining = burnoutTicks;
            }
            numericText = remaining >= 0 ? remaining.ToStringTicksToPeriod() : FormatPercentText(shieldFill, 0);
        }
        return FormatValueText(shieldValueStyle, numericText, shieldFill);
    }

    /// <summary>
    /// HP 型护盾的「当前HP / 最大HP」文字（最大 HP 大于 9999 时只显示当前 HP）；
    /// 非 HP 型（时间制充能）拦截器返回 null。
    /// </summary>
    private static string? GetShieldHitPointsText()
    {
        CompProjectileInterceptor? interceptor = cachedInterceptor;
        if (interceptor == null || !interceptor.Active)
        {
            return null;
        }

        int maxHP = interceptor.HitPointsMax;
        if (maxHP <= 0)
        {
            return null;
        }

        int curHP = interceptor.currentHitPoints;
        if (curHP < 0)
        {
            curHP = maxHP;
        }
        return maxHP > 9999 ? $"{curHP}" : $"{curHP} / {maxHP}";
    }

    /// <summary>
    /// 时间制充能拦截器（无 HP 上限、按激活/充能时间工作的拦截器）的剩余充能刻数。
    /// 非时间制充能或未在充能时返回 -1（此时按填充百分比显示）。
    /// </summary>
    private static int GetShieldChargeRemainingTicks()
    {
        CompProjectileInterceptor? interceptor = cachedInterceptor;
        if (interceptor == null || !interceptor.Active || interceptor.HitPointsMax > 0)
        {
            return -1;
        }

        var props = interceptor.Props;
        int ticksGame = Find.TickManager.TicksGame;

        if (props.activated)
        {
            if (props.activeDuration <= 0)
            {
                return -1;
            }
            int activatedTick = (int)ActivatedTickField.GetValue(interceptor);
            return Mathf.Max(0, activatedTick + props.activeDuration - ticksGame);
        }

        if (props.chargeIntervalTicks > 0)
        {
            int remaining = interceptor.ChargeCycleStartTick - ticksGame;
            return remaining > 0 ? remaining : -1;
        }

        // 被动常开拦截器无充能过程。
        return -1;
    }

    #endregion

    #region 缓动

    /// <summary>
    /// 缓动状态缓存：检查面板同一时间只显示一个单位，按条标识独立跟踪。
    /// 槽位数取 BarType 枚举成员数（枚举尾部追加新条时自动扩容），按 BarType 直接索引，省去字典哈希；
    /// 布尔数组标记该槽是否已有状态。切换选中单位时整体清零（直接贴到目标值，避免跨单位视觉跳跃）。
    /// </summary>
    private static readonly EaseState[] barEaseStates = new EaseState[Enum.GetValues(typeof(BarType)).Length];
    private static readonly bool[] barEaseStateValid = new bool[Enum.GetValues(typeof(BarType)).Length];
    private static object? lastEasedOwner;

    /// <summary>
    /// 缓动帧开始：切换选中单位时清空全部缓动状态（直接贴到目标值，避免跨单位视觉跳跃）。
    /// 需在解析 Spec 前调用，保证护盾条可见性判定（读取现状态）在切换后取到初值 0。
    /// </summary>
    private static void BeginEaseFrame(object owner)
    {
        if (lastEasedOwner != owner)
        {
            lastEasedOwner = owner;
            Array.Clear(barEaseStates, 0, barEaseStates.Length);
            Array.Clear(barEaseStateValid, 0, barEaseStateValid.Length);
        }
    }

    /// <summary>
    /// 对已收集的整组条统一做缓动（作用于所有条）：条本身的绘制值 eased 朝目标 fill 过渡。
    /// 启用缓动时按条标识跟踪各自的 EaseState 逐步过渡；禁用缓动或条首次出现时直接贴到目标。
    /// 条宽即 eased，缓动收敛后等于 fill，不再额外绘制差量段。
    /// </summary>
    private static void EaseCollectedBars(List<BarRowInfo> bars)
    {
        bool enable = enableEaseEffect;
        for (int i = 0; i < bars.Count; i++)
        {
            BarRowInfo bar = bars[i];
            BarType type = bar.type;
            int slot = (int)type;

            if (!barEaseStateValid[slot])
            {
                // 条首次出现：直接贴到目标，不从头动画。
                barEaseStateValid[slot] = true;
                barEaseStates[slot].Set(bar.fill);
                bar.eased = bar.fill;
                bars[i] = bar;
                continue;
            }

            EaseState state = barEaseStates[slot];
            // 冷却、工作条禁用缓动，其余条按全局设置。
            if (!enable || type is BarType.Cooldown or BarType.Work)
            {
                state.Set(bar.fill);
            }
            else
            {
                state.DoEase(bar.fill);
            }
            bar.eased = state.easedValue;
            barEaseStates[slot] = state;
            bars[i] = bar;
        }
    }

    /// <summary>读取某条当前缓动状态的绘制值（尚无状态时视为 0）。</summary>
    private static float GetStoredEased(BarType type)
        => barEaseStateValid[(int)type] ? barEaseStates[(int)type].easedValue : 0f;

    /// <summary>
    /// 单路缓动状态：记录当前值、起点、目标与已用时间。
    /// 目标变化时从当前缓动值过渡到新目标；足够接近时直接贴附，避免小幅波动反复重启缓动。
    /// </summary>
    private struct EaseState
    {
        public float easedValue;
        public float startValue;
        public float currentTarget;
        public float elapsedTime;

        /// <summary>直接贴到目标值（禁用缓动或切换选中单位时）。</summary>
        public void Set(float target)
        {
            easedValue = target;
            startValue = target;
            currentTarget = target;
            elapsedTime = 0f;
        }

        /// <summary>朝目标值过渡一步（Linear/EaseIn/EaseOut，按速度确定时长）。</summary>
        public void DoEase(float target)
        {
            target = Mathf.Clamp01(target);

            // 目标变化：从当前缓动值开始过渡到新目标。
            float delta = currentTarget - target;
            if (delta > 1e-6f || delta < -1e-6f)
            {
                // 已足够接近新目标（<0.2%）则直接贴附，避免小幅波动反复重启缓动。
                float valDelta = easedValue - target;
                if (valDelta > -0.002f && valDelta < 0.002f)
                {
                    Set(target);
                    return;
                }

                startValue = easedValue;
                currentTarget = target;
                elapsedTime = 0f;
            }

            float totalDelta = currentTarget - startValue;
            if (totalDelta > -1e-6f && totalDelta < 1e-6f)
            {
                easedValue = currentTarget;
                return;
            }

            elapsedTime += Time.deltaTime;
            float duration = Mathf.Abs(totalDelta) / easeSpeed;
            if (elapsedTime >= duration)
            {
                easedValue = currentTarget;
                startValue = currentTarget;
                return;
            }

            float t = elapsedTime / duration;
            switch (easingStyle)
            {
                case EasingStyle.EaseIn:
                    t = t * t;
                    break;
                case EasingStyle.EaseOut:
                    t = 1f - (1f - t) * (1f - t);
                    break;
            }
            easedValue = startValue + (currentTarget - startValue) * t;
        }
    }

    #endregion

    #region 自爆阈值

    /// <summary>
    /// 获取自爆引信启动阈值（仅首次检查并缓存，切换单位时重置）。
    /// 返回该物体血量低于多少点时引信启动；无 CompExplosive 或无阈值时返回 0。
    /// </summary>
    private static int GetExplosiveThreshold()
    {
        if (cachedExplosiveThreshold >= 0)
        {
            return cachedExplosiveThreshold;
        }

        cachedExplosiveThreshold = 0;
        CompExplosive? explosive = cachedExplosive;
        if (explosive != null && explosive.Props is CompProperties_Explosive props
            && props.startWickHitPointsPercent > 0f)
        {
            cachedExplosiveThreshold = Mathf.RoundToInt(props.startWickHitPointsPercent * explosive.parent.MaxHitPoints);
        }
        return cachedExplosiveThreshold;
    }

    #endregion
}