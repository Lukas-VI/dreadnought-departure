# Dreadnought Departure 交接文档

> 更新日期：2026-08-01
> 分支：`codex/ship-operations`
> 最后提交：`483443e docs: 添加 Dreadnought Departure 交接文档`

## 1. 当前状态

- 编辑器到游玩场景的数据链路已打通：编辑器画布 JSON 被 `LevelDataManager` 加载，`UnitSpawner` 按 `ShipId` 生成舰船。
- 分支上已完成第一版“分阶段逐船操作”：每个玩家阶段按存活舰船顺序排队，自动选中队首并弹出底部操作菜单。
- 本轮又完成：底部弹性菜单替代轮盘、三个移动阶段各自推进时立即惯性移动（Tween）、相机滚轮缩放联动仰角、旧场景路径修复、炮击点击失效修复、新建画布地图朝向（E/W 与 N/S）。
- 本轮继续完成：敌方 AI 接入阶段管线、胜负判定与战斗结果面板、岛屿阻挡移动、炮击射界限制、移动碰撞占位、主炮有限弹药、敌方行动演出镜头、航向锥形移动预览、规则数据卡与玩法文档、关卡初设数据链路、纸质速力表奇偶、延迟降速、堆叠上限 2、运行时舰船目录、3D 六角朝向修正、Label3D 状态化与左右信息面板。
- 以上内容大部分仍在工作区未提交（见第 4 节），下一手可以直接继续。

## 2. 本轮改动明细

### 文件结构路径修复

- `Scripts/UI/Menu/MapSelectMenuController.cs`：`EditorScenePath` 改为 `res://Scenes/UI/Editor/editor_scene.tscn`，`MainMenuScenePath` 改为 `res://Scenes/UI/Menu/MainMenu/main_menu.tscn`。
- `Scripts/Editor/EditorSceneController.cs`、`Scripts/Editor/EditorUIController.cs`：主菜单路径同步指向新目录。
- 用户已自行更新的 `project.godot`、`MainMenuController.cs`、`PauseMenuController.cs`、`battle_scene.tscn` 保持原样，未回退。

### 地图朝向（E/W 水平向 / N/S 竖直向）

- `BattleDataTypes.cs` 新增 `HexOrientation`；`Scripts/Data/HexMath.cs` 提供编辑器与 3D 共用的轴向坐标投影。
- `LevelDataManager` 的 v3 JSON 新增 `Orientation` 字段（`ew` / `ns`），旧地图缺省按 `ew` 解析；`NewMap` 支持传入朝向。
- 新建画布弹窗（地图选择菜单与编辑器内）可选“E/W 水平向（当前）”或“N/S 竖直向”。
- 编辑器地块改为程序化绘制：`MapCanvasController` 用 `_Draw` 画带边框六边形，填充色采样自 `mat_ocean.tres` / `mat_island.tres`，不再使用 tileset PNG。
- 新增 `Scripts/Data/HexMath.cs`：轴向坐标与编辑器局部坐标统一投影，EW/NS 两种朝向共用一套换算；`ShipNameOverlay` 改用 `HexMath.HexToLocal` 定位舰名。
- 新增 `Scenes/UI/Editor/editor_scene.tscn`：`MapCanvas` + `ShipNameOverlay` 直接挂在场景根节点，不再实例化 `MapEditor` / TileMapLayer。
- NS 地图进入战斗时，`MapGenerator` 会把 3D 六角瓦片绕 Y 旋转 30°，与编辑器朝向一致。
- 已移除旧 2D TileMap 提取兼容路径：删除 `map_editor.tscn` 与 `EditorInputDetector`，`battle_scene.tscn` 不再绑定 `MapEditorScene`，`LevelDataManager` 只从 JSON 加载地图。

### 底部弹性菜单（替代轮盘）

- 新增 `Scripts/UI/Gameplay/PhaseActionMenu.cs` + `PhaseActionMenu.cs.uid`。
- `Scenes/UI/Gameplay/BattleUI.tscn` 中把 `WheelMenu` 节点替换为 `ActionMenu`（底部居中）+ `CardRow`（HBoxContainer）。
- `BattleUIController.cs` 改为管理 `PhaseActionMenu`，收到 `_show_menu` 后按当前阶段弹出操作卡片，卡片带 Back 缓动动画。
- 各阶段允许操作：
  - 速度调整：加速、减速、左转、右转、待命。
  - 三段移动：左转、右转、待命（移动不再手动点选）。
  - 火炮：炮击、技能、待命。
- 旧的 `WheelMenu.cs` 不再被场景引用，暂保留未删除。

### 玩家操作（PlayerController）

- 去掉手动移动指令；机动改为各移动阶段推进时按航向/速度自动推算。
- 保留逐船队列：`BeginPhaseAction` 建队，`SelectNextShip` 自动轮到下一艘，点击非队首船会提示按顺序操作。
- 新增 `skip`（待命）指令，避免某船无可操作时卡住。
- 炮击：菜单点“炮击”后进入待命，点击敌舰/敌格确认目标；命中进入 `PendingDamage`，结算阶段落实。
- 相机反馈：变速以“船-推算到达格”中点聚焦，转向以船体俯视，炮击先拉高看射程、确认目标后以“船-敌舰”中点聚焦。

### 敌方 AI 与胜负判定

- `PlayerController` 玩家队列清空时发出 `PlayerSideFinished`；`GameplayDirector` 在同一阶段内接续 `AIController`：速度阶段变速、移动阶段转向、炮击阶段对射程内敌舰开火。位移仍由阶段结束时的统一移动结算执行，不会让 AI 额外移动。
- 结算阶段落实 `PendingDamage` 后检查任一方全灭，`BattleEnded` 信号驱动 `BattleUI` 新增的结果面板（重试 / 返回主菜单）。
- 阶段移动改为逐格推进：`MoveRulesEvaluator.AdvanceSteps` 沿航向检查地形，遇到岛屿格停在上一格并输出阻挡日志，不会整段穿岛。
- 移动结算先收集全部存活舰船占位，逐船推进时禁止进入其他舰船占位格，并预留目标格，避免多船同回合挤入同一格。
- 敌方行动开始时，`GameplayDirector` 计算存活敌舰中心并通过 `CameraFocusRequested` 平滑切镜，复用现有运镜系统。

### 炮击射界限制

- `CombatRulesEvaluator.CanFireInArc` 用 `Firepower.ForArc` 按攻击者航向与目标方向校验射界；未配置 `ShipData` 时默认全域可射。
- 命中结算把前/侧/后火力值作为伤害系数（`基础伤害 × arcPower / 6`）；默认 `BackwardFire = 0`，船尾目标会被玩家与 AI 的炮击流程拒绝。
- `MoveRulesEvaluator.DirectionTo` 统一提供六格方向换算，AI 转向与射界判定共用同一套方向逻辑。

### 规则数据卡与玩法文档

- `ShipData` 新增 `PV`、`ShipClass`、`HullThresholds`、`MaxSpeedByState`、副炮火力、鱼雷管、备用鱼雷、雷达等字段；`DamageThresholds` 的旧“HP 百分比”语义已移除，改为累计损伤点。
- `ShipComponent` 新增 `DamageState` / `DamageTaken` / `MaxSpeedForCurrentState`，损伤落实后自动切换状态；最大速度限制在下一回合速度调整阶段强制生效。小破/中破/大破分别影响火力系数与开火资格。
- 四艘原型舰数据与预制体已补齐：南达科他、白露(b)、火奴鲁鲁级轻巡、尼古拉斯级驱逐，对应 `Ships/Dreadnought`、`Ships/Frigate`、`Ships/Cruiser`、`Ships/Destroyer`。
- 新增 `docs/GAMEPLAY_RULES.md`：整理当前简化玩法、舰船数据卡、已实现/未实现清单，以及与纸质规则书的冲突点。

### 关卡初设与规则拍板

- `LevelDataManager` 的 v3 JSON 新增关卡初设：双方指挥值、双方初设 CP、主动权值、基本视野、鱼雷命中模式、最大回合数；编辑器与主菜单的新建画布弹窗均提供占位输入。
- `GameplayDirector` 从关卡初设读取玩家指挥值与初设 CP：`MaxCP = 指挥值 × 2`，每回合补充 `指挥值` 点。
- 速力表恢复纸质 A2 表（速度 0-8）；速度 1 的第一移动阶段为 `0+`，仅奇数回合移动 1 格，奇偶回合判定重新启用。
- 损伤导致的降速不立即生效，统一在下一回合速度调整阶段强制压速。
- 堆叠上限从 3 改为 2；模型视觉偏移、移动后堆叠与冲撞检定仍留待后续。

### 舰船目录与 3D 朝向

- `ShipList.tres` / `ShipList.cs` 已移除：`UnitSpawner` 不再按旧 TileId 查表，`ShipCatalog` 运行时扫描 `res://Data/Ships` 与 `res://Ships`，目录名即 ShipId。
- `HexMath` / `MapGenerator` / `UnitSpawner` / `BattleInputDetector` 改为朝向相关投影：EW 用平边投影，NS 用尖角投影，NS 地块各自旋转 30°；撤销了此前盲加的全局 -60° 基准旋转。

### 战斗信息展示

- `ShipComponent.UpdateUi` 改为仅输出状态词（小破/中破/大破/沉没/离场/转向），无损时隐藏；新增 `TurnedThisPhase` / `IsOffMap` 运行时状态。
- 阶段标签改为进度显示：只显示“第 X 回合 · 当前阶段”，CP 移到左侧指挥面板。
- `BattleUI` 新增左、右信息面板：左侧显示指挥值/CP 与我方全部登场舰船，右侧显示敌方全部登场舰船；底部保留战斗日志。
- 已记录后续折叠规则：大战场按单纵阵 / 舰级 / 激活参战折叠，当前先完整列表。

### 弹药系统

- `ShipData.MainAmmo` / `ShipComponent.MainAmmo`：每艘船拥有主炮弹药量，玩家与 AI 每次开火消耗 1；弹药耗尽后操作菜单不再显示炮击按钮，AI 也不会再开火。
- 舰船头顶 Label 与 HUD 选中信息均显示剩余弹药；鱼雷弹药与鱼雷阶段仍未实现。

### 分阶段惯性移动

- `GameplayDirector.AdvancePhase` 在离开第一/二/三移动阶段时，按 `SpeedTable.MoveForPhase(speed, phase)` 立即执行该阶段位移，动画结束后再切换阶段。
- 选中船时按当前航向与总移动力高亮前方 120° 锥形可到达格（`OverlayArcDrawRequested`），供玩家预判机动方向。
- 回合不再按奇偶区分，`SpeedTable` 对 `"+"` 档位每阶段统一 +1 格。
- 结算阶段 `DoEndTurnSettlement` 只落实 `PendingDamage`，不再执行移动。
- `ShipComponent` 新增 `public virtual Tween AnimateMoveTo(MapGenerator map, Vector2I target, float duration)`，子类可覆写播放模型专属动画。

### 相机缩放联动

- `GameplayCameraController`：
  - 滚轮缩放时按距离插值 `_targetPitch`：放大趋近水平正视（`HorizontalViewPitch`），缩小趋近俯视（`TopDownPitch`）。
  - `PitchLimit` 下限降到 5°，便于放大到接近水平视角。
  - 焦点不手动平移时仍围绕原焦点，鼠标地面锚点逻辑保留。

### 炮击点击失效修复（根因）

- 现象：点击敌船无法选中/炮击无反应。
- 根因一：`Ships/BaseShip/ship_3d.tscn` 的 `Area3D` 默认在碰撞层 1，而 `BattleInputDetector` 用 mask 2 射线。
- 根因二：舰船碰撞体是 `Area3D`，Godot 射线默认不碰撞 Area（`CollideWithAreas` 默认 false）。
- 修复：
  - `ship_3d.tscn`：`Area3D.collision_layer = 2`。
  - `BattleInputDetector.cs`：查询设 `CollideWithAreas = true; CollideWithBodies = false;`。
- 验证：headless 冒烟对 6 艘船逐个从上方打射线，全部命中 `Area3D`。

## 3. 验证记录

- `dotnet build 'Dreadnought Departure.csproj'`：0 警告，0 错误。
- Headless 冒烟（Godot 4.7 mono）：
  - 战斗场景实例化成功，`ActionMenu` 开局可见。
- 画布菜单 `EditorScenePath` / `MainMenuScenePath` 均指向新文件结构。
- 所有船 `Area3D.collision_layer == 2`，开启 Area 碰撞后射线全部命中。
- 编辑器程序化画布 headless 冒烟：`editor_scene.tscn` 实例化成功；`1.json`（EW）与 `2.json`（NS）均能 `LoadMap`、`ApplyDataToLayers`、`queue_redraw` 且无报错，NS 空图朝向解析为 `NSVertical`。
- 战斗 headless 冒烟：实例化 `battle_scene.tscn` 后触发 `PlayerSideFinished`，敌方 AI 行动与 `BattleEnded` 结果面板流程无报错。
- 演出镜头 headless 冒烟：触发 `PlayerSideFinished` 后敌方行动与相机焦点信号无报错。
- 岛屿阻挡 headless 冒烟：在船头设置岛屿、把船速提到 3 后连续推进三个移动阶段，移动结算与阶段流转无报错。
- 射界 headless 冒烟：推进到炮击阶段后对船尾方向的敌舰发起炮击，射界拒绝路径与阶段流转无报错。
- 弹药 headless 冒烟：把全部舰船主炮弹药设为 0 后推进到炮击阶段，点击“炮击”走拒绝路径且流程无报错。
- 碰撞 headless 冒烟：把两艘船摆成同航向纵队并推进移动阶段，后船不会穿过前船占位格，阶段流转无报错。
- 航向预览 headless 冒烟：战斗开局自动选中首船时 `OverlayArcDrawRequested` 高亮路径无报错。
- 规则 headless 冒烟：编辑器 `NewMap` 可写入关卡初设；四艘舰船资源/场景可加载；南达科他受 21 点损伤后连推七个阶段到下一回合速度调整阶段，延迟降速流程无报错。
- 目录冒烟：移除 `ShipList` 后，战斗场景仍能通过 `ShipCatalog` 从运行时目录生成舰船，且 3D 地图生成无报错。
- NS 投影冒烟：空 NS 地图放入 (0,0)/(1,0)/(0,1)/(-1,1) 后，3D 瓦片中心呈 0°/60°/120° 尖角六边形排列，无错位留空。
- 尚未做真人编辑器内点击验证。

## 4. Git 状态与提交建议

- 已提交：`9d2de4c`（逐船队列、相机焦点接口、地图类型、白天地图跳照明）。
- 未提交的本轮功能文件（建议单独 commit）：
  - `Scripts/UI/Menu/MapSelectMenuController.cs`
  - `Scripts/Editor/EditorSceneController.cs`
  - `Scripts/Editor/EditorUIController.cs`
  - `Scripts/UI/Gameplay/PhaseActionMenu.cs` + `.uid`
  - `Scenes/UI/Gameplay/BattleUI.tscn`
  - `Scripts/UI/Gameplay/BattleUIController.cs`
  - `Scripts/Battle/PlayerController.cs`
  - `Scripts/Battle/GameplayDirector.cs`
  - `Scripts/Unit/ShipComponent.cs`
  - `Scripts/Camera/GameplayCameraController.cs`
  - `Ships/BaseShip/ship_3d.tscn`
  - `Scripts/Battle/BattleInputDetector.cs`
  - `Scripts/Battle/AIController.cs`
  - `Scripts/Battle/IUnitController.cs`
  - `Scripts/Battle/MoveRulesEvaluator.cs`
  - `Scripts/Battle/GridOverlayController.cs`
  - `Scripts/Battle/CombatRulesEvaluator.cs`
  - `Scripts/Data/Ship/ShipData.cs`
  - `Ships/Cruiser/cruiser_data.tres`、`Ships/Cruiser/cruiser.tscn`
  - `Ships/Destroyer/destroyer_data.tres`、`Ships/Destroyer/destroyer.tscn`
  - `docs/GAMEPLAY_RULES.md`
  - `Scripts/Data/EventBus.cs`
  - `Scripts/Map/LevelDataManager.cs`
  - `Scripts/UI/Gameplay/BattleUIController.cs`
  - `Scripts/Data/HexMath.cs` + `.uid`
  - `Scripts/Editor/MapCanvasController.cs`
  - `Scripts/Editor/ShipNameOverlay.cs`
  - `Scenes/UI/Editor/editor_scene.tscn` + 配套 UI 场景
  - 删除旧 `Scenes/UI/Editor/map_editor.tscn`、`Scripts/Map/EditorInputDetector.cs` + `.uid`
  - `export/maps/2.json`
  - 已删除程序化地块改造前生成的 `assets/texture/Tiles/*_ns.png`
- 工作区还有用户的文件结构重构未提交（场景移动/删除、`project.godot`、`export/maps/1.json` 等），提交时应与功能改动分开。

## 5. 已知问题与下一步

- 真人编辑器内炮击、分阶段移动动画、滚轮缩放需要实际点击验证。
- 敌方 AI 仍未接入阶段管线（`AIController` 仍是占位）。
- 分阶段移动未处理目标格占用/堆叠，后续需要碰撞或堆叠规则。
- 夜战照明阶段（`ReconLighting`）只有逻辑占位，尚无探照灯/照明弹玩法。
- 后续可做：舰船专属移动动画、炮口火光/水花特效、技能菜单卡片化、战役流程。
