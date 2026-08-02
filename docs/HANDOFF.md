# Dreadnought Departure 交接文档

> 更新日期：2026-08-02
> 分支：`codex/ship-operations`
> 最后提交：`957fc5e docs: 交接文档同步本轮提交号`

## 1. 当前状态

- 编辑器到游玩场景的数据链路已打通：编辑器画布 JSON 被 `LevelDataManager` 加载，`UnitSpawner` 按 `ShipId` 生成舰船。
- 分支上已完成第一版“分阶段逐船操作”：每个玩家阶段按存活舰船顺序排队，自动选中队首并弹出底部操作菜单。
- 本轮又完成：底部弹性菜单替代轮盘、三个移动阶段各自推进时立即惯性移动（Tween）、相机滚轮缩放联动仰角、旧场景路径修复、炮击点击失效修复、新建画布地图朝向（E/W 与 N/S）。
- 本轮继续完成：敌方 AI 接入阶段管线、胜负判定与战斗结果面板、岛屿阻挡移动、炮击射界限制、移动碰撞占位、主炮有限弹药、敌方行动演出镜头、单格下一到达预览、规则数据卡与玩法文档、关卡初设数据链路、纸质速力表奇偶、延迟降速、堆叠上限 2、运行时舰船目录、3D 六角朝向修正、Label3D 状态化与左右信息面板、鱼雷阶段开关、待命指令预览。
- 本轮新增：编辑器新建画布阶段限时 / 鱼雷开关可视化、副炮与前后侧火力、雷达技能化。
- 以上内容已随本轮提交入库，下一手可以直接继续。

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
- `ShipComponent` 增加 NS 地图船模偏移：NS 模式下船模额外逆时针旋转 30°，避免船头指向顶点。

### 战斗信息展示

- `ShipComponent.UpdateUi` 改为仅输出异常状态词（小破/中破/大破/沉没/离场/转向），无损时不显示、转向只显示“转向”；新增 `TurnedThisPhase` / `IsOffMap` 运行时状态。
- 沉没时 `StatusText` 只返回“沉没”；船体不销毁，尸体留在原位持续显示沉没标签。
- `Label3D` / `TurnFlag` 已从 `Ships/BaseShip/ship_3d.tscn` 移除，新增 `BattleShipOverlayController` 在战斗场景统一创建并同步位置。
- 临时元素生命周期加固：TurnFlag 切换时只保留当前焦点，GridOverlay 显示前强制清空，Label3D 周期同步显隐。
- `AIController.TakeTurn` 改为异步逐船演出：每艘敌舰行动前发 `CameraTopDownRequested` 并等待约 0.45 秒。
- `BattleShipOverlayController` 将 Label3D / 旗标挂到船体子节点，随舰船 Tween 同步移动。
- 阶段标签改为进度显示：只显示“第 X 回合 · 当前阶段”，CP 移到左侧指挥面板。
- `BattleUI` 新增左、右信息面板：左侧显示指挥值/CP 与我方全部登场舰船，右侧显示敌方全部登场舰船；底部保留战斗日志。
- 左右舰船列表改为按钮：点击我方船可快捷选中，点击敌方船可快捷查看并复用炮击目标选择链路。
- 已记录后续折叠规则：大战场按单纵阵 / 舰级 / 激活参战折叠，当前先完整列表。

### 阶段限时

- `PhaseSecondsPerShip` / `PhaseExtraSeconds` 已进入关卡 JSON 数据模型，默认 `5,5,5,5,5,10,10,0` + 额外 5 秒。
- `GameplayDirector` 用 `_Process` 倒计时：全体指定后倒计时继续，归零或手动推进时提交 pending、未选择船默认待命并轮到敌方；敌方 AI 完成后自动推进阶段。
- `BattleUI` 阶段控制区新增 `ProgressBar` 与倒计时标签，`PhaseTimerUpdated` 信号驱动。
- 行动未推进前允许点击其他我方舰船重选（取消当前 pending 行动）。
- `PhaseActionMenu` 操作卡片改为两行文本（动作 + CP 消耗），已选动作上浮高亮，CP 不足或受损等不可执行动作灰化下沉。

### 待命指令与鱼雷开关

- 速度、转向、炮击改为 pending：`ShipComponent` 保存 `PendingSpeed` / `PendingDirection` / `PendingAttackTarget`，`GameplayDirector.AdvancePhase` 手动推进时统一提交。
- pending 期间显示预测到达格与相机预览；全部舰船下达指令后不再自动选中第一艘、菜单收起、相机拉高等待点击推进。
- 关卡 JSON 新增 `TorpedoPhaseEnabled`，默认关闭；未启用时 `Gunfire → EndTurn` 跳过鱼雷阶段。

### 单纵阵

- `MoveRulesEvaluator.DetectLineAhead` 完善为同速同向、首尾相邻的完整编队链检测。
- 头舰变速/转向时自动给全部编队船写入 pending，并只收 1 CP；`SelectNextShip` 跳过已有待命指令的船，玩家仍可手动点击覆盖。

### 地形与冲撞

- 岛屿格不可进入，移动结算中撞岛船直接进入沉没状态并保留尸体。
- 移动受阻时先判断阻挡来源：舰船占位触发 `1D10≤2` 冲撞检定，命中后按简化 A3 表（按船体值之和分段）对双方造成损伤；完整 A3 表与超堆叠检定留待后续。

### 视野与雷达

- 新增 `VisionRulesEvaluator`：基本视野 + 岛屿视线遮挡；`RadarRulesEvaluator` 按雷达型号提供距离与命中修正。
- 玩家与 AI 炮击目标均需通过 `CanEngage`；雷达激活时可越过基本视野交战，并在命中判定中应用型号修正。

### 编辑器新建画布

- 主菜单与编辑器内新建画布弹窗均可逐阶段配置 `PhaseSecondsPerShip`（速度/三移动/视野/炮击/鱼雷/结算每船秒数）、`PhaseExtraSeconds` 与 `TorpedoPhaseEnabled`，写入 v3 JSON。
- `LevelDataManager.NewMap` 已接收并保存这些参数；旧地图缺省值不变。

### 副炮与前后侧火力

- `ShipData` 新增 `SecondaryGunCaliber` / `SecondaryAttackPower`；南达科他副炮 12cm（6-10-6）已配置。
- `CombatRulesEvaluator` 主炮/副炮分别按前/侧/后射界独立命中检定与伤害结算；大型舰一次射击同时结算两门炮，中小型舰只结算主炮。
- 未配置副炮火力时不影响原射击；副炮基础火力按口径比例折算或读取配表值。

### 雷达技能

- 雷达从自动生效改为炮击阶段技能：`ShipComponent.PendingRadarActive` 标记显式激活，中破及以上按 D1 禁用。
- 玩家点“雷达”按钮开关不消耗行动；AI 在炮击阶段自动激活。
- `VisionRulesEvaluator.CanEngage` / `IsRadarOnly` 接收激活标记，雷达射击继续应用 `RadarRulesEvaluator` 的型号命中修正。

### 性能优化

- 3D 地形瓦片统一关闭阴影，200+ 瓦片场景显著减少渲染开销。
- `PhaseTimerUpdated` 改为 0.05 秒节流发送，避免逐帧驱动进度条刷新。
- 主菜单 → 画布选择 → 战斗场景的完整链路已在 headless 下验证可加载；若实际设备仍有卡顿，下一步做地形 MultiMesh 合批。

### 弹药系统

- `ShipData.MainAmmo` / `ShipComponent.MainAmmo`：每艘船拥有主炮弹药量，玩家与 AI 每次开火消耗 1；弹药耗尽后操作菜单不再显示炮击按钮，AI 也不会再开火。
- 舰船头顶 Label 与 HUD 选中信息均显示剩余弹药；鱼雷弹药与鱼雷阶段仍未实现。

### 分阶段惯性移动

- `GameplayDirector.AdvancePhase` 在离开第一/二/三移动阶段时，按 `SpeedTable.MoveForPhase(speed, phase)` 立即执行该阶段位移，动画结束后再切换阶段。
- 选中船时只高亮当前指令后下一移动阶段将到达的单个格子（`MoveTargetHighlighted`），转向/加减速后按 pending 方向与速度刷新。
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
- 单纵阵 headless 冒烟：两船同速同向纵队，头舰加速后两船 `PendingSpeed=4`，CP 由 8→7。
- 撞岛 headless 冒烟：在船正前方设置岛屿并推进移动阶段后 `DamageState=Sunk`，船体保留原位。
- AI 相机/视野 headless 冒烟：触发 `PlayerSideFinished` 后敌舰逐艘俯视运镜，阶段推进无报错。
- 规则 headless 冒烟：编辑器 `NewMap` 可写入关卡初设；四艘舰船资源/场景可加载；南达科他受 21 点损伤后连推七个阶段到下一回合速度调整阶段，延迟降速流程无报错。
- 目录冒烟：移除 `ShipList` 后，战斗场景仍能通过 `ShipCatalog` 从运行时目录生成舰船，且 3D 地图生成无报错。
- NS 投影冒烟：空 NS 地图放入 (0,0)/(1,0)/(0,1)/(-1,1) 后，3D 瓦片中心呈 0°/60°/120° 尖角六边形排列，无错位留空。
- 限时冒烟：阶段倒计时标签随时间递减，进度条可见且数值同步更新。
- pending 冒烟：速度指令写入 pending 后 `CurrentSpeed` 不变；手动推进后提交速度并自动接敌方、推进到下一阶段。
- 编辑器新建画布 headless 冒烟：`LevelDataManager` 写入/读回 `PhaseSecondsPerShip`、`PhaseExtraSeconds`、`TorpedoPhaseEnabled`；编辑器 `editor_ui.tscn` 与主菜单 `map_select_menu.tscn` 实例化无报错。
- 副炮 headless 冒烟：南达科他在炮击阶段对距离 3 敌舰开火，主炮与副炮各产生独立命中检定记录。
- 雷达技能 headless 冒烟：基本视野 1、敌舰距离 6，轻巡先开雷达再炮击可选中目标，无“目标不在视野”拒绝日志。
- 尚未做真人编辑器内点击验证。

## 4. Git 状态与提交建议

- 已提交：`f46f9ef`（单纵阵、冲撞检定与视野雷达规则）、`957fc5e`（交接文档同步）。
- 本轮待提交：编辑器新建画布限时/鱼雷开关、副炮与前后侧火力、雷达技能及配套文档。
- 涉及文件：`Scenes/UI/Editor/editor_ui.tscn`、`Scripts/Editor/EditorUIController.cs`、`Scripts/UI/Menu/MapSelectMenuController.cs`、`Scripts/Battle/{AIController,CombatRulesEvaluator,PlayerController,VisionRulesEvaluator}.cs`、`Scripts/Data/Ship/ShipData.cs`、`Scripts/UI/Gameplay/PhaseActionMenu.cs`、`Scripts/Unit/ShipComponent.cs`、三份舰船 `.tres` 与三份文档。

## 5. 已知问题与下一步

- 真人编辑器内炮击、分阶段移动动画、滚轮缩放需要实际点击验证。
- 冲撞目前是简化 A3 表，完整表 A3 与移动后超堆叠检定尚未精确实现。
- 夜战照明阶段（`ReconLighting`）只有逻辑占位，尚无探照灯/照明弹玩法。
- 烟幕、照明弹、鱼雷仍是后续实体玩法；雷达技能已接入，探照灯未做。
- 后续可做：主动权规则、完整 A3 冲撞表与超堆叠检定、鱼雷阶段、烟幕/照明弹技能、舰船专属移动动画与炮口火光/水花特效。

## 6. 测试纪律

- 运行 Godot headless 测试后，不得按进程名批量 `Stop-Process`，否则可能误杀用户打开的带窗口 Godot 编辑器。
- 只清理本次测试自己启动的精确 PID：启动时记录 PID，结束后仅对该 PID 做收尾。
- 若进程已自行退出，不要执行任何 Godot 进程清理。
