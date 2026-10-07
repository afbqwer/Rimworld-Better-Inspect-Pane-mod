# Better Inspect Pane

RimWorld 1.6 检查面板增强模组：自定义重绘检查面板，为建筑 / 区域 / 世界对象 / Pawn 绘制各类进度条（健康、护盾、蓄电、工作、生长等），并支持多选网格、面板尺寸调整与自定义字号。

## 目录结构

```
My Mod Template 2/
├── About/                    # 模组元数据（About.xml、预览图、创意工坊 ID）
├── Languages/                # 本地化（English / ChineseSimplified 的 Keyed 翻译）
├── 1.6/Assemblies/           # 编译输出（Better Inspect Pane.dll）
└── Source/                   # C# 源码（Better Inspect Pane.csproj）
```

## 源码结构（Source/）

命名空间 `ASQBetterInspectPane`，基于 Harmony 补丁实现。

### 入口

| 文件 | 说明 |
|---|---|
| `Mod.cs` | 模组入口：`MyModTemplate : Mod` 加载设置；`HarmonyPatching` 启动时 `PatchAll` |

### 检查面板重绘（partial class `InspectPanePatch`）

核心补丁接管 `InspectPaneFiller.DoPaneContentsFor`，按 `partial` 按职责拆分：

| 文件 | 说明 |
|---|---|
| `InspectPanePatch.cs` | 绘制入口与绘制管线：分隔线、Pawn 策略行、进度条排布（单列 / 多列网格）、技能区、右下角设置按钮 |
| `InspectPanePatch.Logic.cs` | 数据计算：各类条数据收集、组件 / 区域 / 储存 / 电网等按帧缓存、缓动、剩余时间估算（EMA 采样） |
| `InspectPanePatch.InspectString.cs` | 自定义 InspectString 绘制：文本 30 帧缓存、自适应面板高度、额外信息文字拼装 |
| `InspectPanePatch.MultiSelect.cs` | 多选网格：接管多选时面板正文，RTS 风格选择网格（Things / Zones / 世界对象三类场景） |

### 其它 Harmony 补丁

| 文件 | 补丁目标 | 说明 |
|---|---|---|
| `InspectPaneOnGUIPatch.cs` | `InspectPaneUtility.InspectPaneOnGUI` | 面板边距与标题字号可配置 |
| `PaneSizeForPatch.cs` | `InspectPaneUtility.PaneSizeFor` | 面板最小宽 / 高可调 |
| `PaneLayoutPatch.cs` | `PaneWidthFor` / `PaneTopY` 等 | 面板变大后让 gizmo 区与标签页让位 |
| `InspectTabMemoryPatch.cs` | `InspectPaneUtility.UpdateTabs` | 记忆各类目标最近打开的标签页（LRU） |
| `SelectorSelectionPatch.cs` | `Selector.SelectInternal` | 选择数量上限可调、改进 Shift 框选 |

### 设置

| 文件 | 说明 |
|---|---|
| `Settings.Data.cs` | `MyModTemplateSettings : ModSettings` 的数据部分：字段、默认值、存取 |
| `Settings.UI.cs` | 设置界面：标签页（通用 / 进度条 / 多选 / 颜色 / 效果）、像素字号作用域 `FontSizeScope` |

### 窗口（window/）

| 文件 | 说明 |
|---|---|
| `Dialog_MyModSettings.cs` | 游戏内独立设置窗口（面板右下角按钮打开） |
| `Dialog_ColorPicker.cs` | 颜色选择器对话框 |

### 兼容层（compatibility/）

全部通过反射读取其它模组 / 原版私有成员，不硬引用 DLL，目标缺失时安全降级：

| 文件 | 目标 |
|---|---|
| `CombatExtendedHeightReflection.cs` | Combat Extended 竖直碰撞高度 |
| `VEFProcessorReflection.cs` | Vanilla Expanded Framework 处理系统组件 |
| `PipeNetReflection.cs` | VFE PipeSystem 管道网络 |
| `GravshipHeatsinkReflection.cs` / `GravMaintenanceReflection.cs` | Vanilla Gravship Expanded 散热器 / 维护 |
| `FortifiedWorkTableReflection.cs` | Fortified 自主工作台 |
| `InfoCardPlusReflection.cs` | Info Card Plus 收藏 Stat 文字 |
| `WorldDominationReflection.cs` | World Domination 2 世界对象强度（强度条）与关系上限（关系阈值标记） |
| `BoundedRationalityReflection.cs` | Bounded Rationality 信息隐藏同步：健康/需求/基础/技能条、策略行、观察文字特性与 Pawn 额外信息按钮按其「信息是否已知」判定同步显隐 |
| `VSEPassionReflection.cs` | Vanilla Skills Expanded 额外激情种类：技能条激情图标 / 文本后缀改用其 PassionDef（支持冷漠 / 天赋 / 狂热等） |

## 构建

- 目标框架 `net48`，输出到 `../1.6/Assemblies`
- 依赖 NuGet 包：`Krafs.Rimworld.Ref`（游戏程序集引用）、`Lib.Harmony`（均 `ExcludeAssets=runtime`，不随模组打包）
