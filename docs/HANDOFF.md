# Dreadnought Departure 交接文档

> 更新日期：2026-08-02
> 分支：`codex/ship-operations`
> 最后提交：`c5a22fa fix: 堆叠偏移改为当前航向局部侧向轴`

## 1. 当前状态

- 编辑器到游玩场景的数据链路已打通：编辑器画布 JSON 被 `LevelDataManager` 加载，`UnitSpawner` 按 `ShipId` 生成舰船。
- 分支上已完成第一版“分阶段逐船操作”：每个玩家阶段按存活舰船顺序排队，自动选中队首并弹出底部操作菜单。
- 本轮又完成：底部弹性菜单替代轮盘、三个移动阶段各自推进时立即惯性移动（Tween）、相机滚轮缩放联动仰角、旧场景路径修复、炮击点击失效修复、新建画布地图朝向（E/W 与 N/S）。
- 本轮继续完成：敌方 AI 接入阶段管线、胜负判定与战斗结果面板、岛屿阻挡移动、炮击射界限制、移动碰撞占位、主炮有限弹药、敌方行动演出镜头、单格下一到达预览、规则数据卡与玩法文档、关卡初设数据链路、纸质速力表奇偶、延迟降速、堆叠上限 2、运行时舰船目录、3D 六角朝向修正、Label3D 状态化与左右信息面板、鱼雷阶段开关、待命指令预览。
- 本轮新增：单纵阵贪吃蛇转向与列表标记、编辑器关卡设置编辑、舰船数据改为标量字段防资源重写丢失。
- 本轮修复：跟随舰到达首船转向格的瞬间立即转向，而不是多走一格后再转，避免单纵阵在转向后断链；后续舰仍按贪吃蛇依次延迟转向。
- 转向动画：玩家提交转向、敌方 AI 转向与编队跟随转向均改为 Tween 补间旋转，逻辑航向即时更新，模型平滑转到位。
- 本轮新增：指挥值随舰船损伤减少、双方 CP 独立补充与上限收缩、敌方 AI 消耗敌方 CP、PV 评分与回合上限判负。
- 本轮新增：操作卡片对变速/转向预测“组成/切断”单纵阵并高亮，左右舰船列表按单纵阵分组显示“阵1/阵2/阵3……”。
- 本轮修复：单纵阵改为追踪首舰完整轨迹，支持“转向-待命-转向”形成的多个转向点与 S/N 形路线；无移动阶段连续转向不再解散编队。
- 本轮新增：炮击范围高亮区分射界，前/后射界使用 `mat_attack_front.tres`，侧射使用 `mat_attack.tres`。
- 本轮修正：射界角度改为正前/正后各 60°、左右侧射各 120°，`Firepower.ForArc` 与 overlay 材质映射同步更新。
- 本轮重构：新增 `FiringArcEvaluator`，按尖顶六角轴向坐标计算真实角度划分 Front/Rear/Port/Starboard，战斗判定与 overlay 共用同一套几何射界。
- 本轮修正：射界整体顺时针偏转 60° 与六角格对齐；侧射材质改为 `mat_attack_side.tres`，`mat_attack.tres` 移除。
- 本轮 UI：指挥值 / CP / PV 状态栏移到顶部居中，左右敌我舰船列表改为可滚动弹性面板；P0 状态更新为“核心闭环完成”，冲撞以简易占位，鱼雷实体玩法后推。
- 本轮调整：左右敌我列表保持当前锚点，容器类型改为 `MarginContainer`，并保留内部 `ScrollContainer` 弹性滚动。
- 本轮新增：同阵营单格堆叠（上限 2 艘），移动可进入友军占位格；移动结算与沉没结算后按堆叠序号沿当前航向的局部侧向轴左右并排，Y 保持统一高度。
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

### 指挥值 / CP / PV 评分

- 新增 `CommandRulesEvaluator`：按规则书 4.3 计算指挥值减少（大型舰小破 -1、中破/大破/沉没 -2；中型舰中破/大破/沉没 -1；小型舰满 3 艘 -1；最低 1），并统一表 A4 射击 CP 花费（大型 2、中小型 1）。
- `GameplayDirector` 新增双方运行时指挥值、敌方 CP 与 PV 分数；`MaxCP = 当前指挥值 × 2`，指挥值降低时同步收缩 CP 上限，回合补充按当前指挥值。
- 变速费用修正为表 A4 的“每次 1 CP”（不再按速度差），编队/单纵阵变速仍为 1 CP。
- 敌方 AI 的变速、转向、炮击现在会消耗敌方 CP；CP 不足时跳过对应行动。
- 新增 `VictoryRulesEvaluator`：中破 `PV/4`、大破 `PV/2`、沉没全额 PV；回合达到 `MaxTurns` 的结算后按 PV 分数判胜，全灭仍立即结束。
- HUD 左侧同时显示双方指挥值、CP 与 PV 分数。

### 单纵阵组成/切断提示与列表分组

- `MoveRulesEvaluator.DetectLineAhead` 增加可选航向/速度覆盖参数，可预测操作后的编队状态而不改变船体运行时属性。
- `PhaseActionMenu` 对变速/转向按钮预测组成/切断：可组成用绿色高亮并显示“组成”，可切断用橙色高亮并显示“切断”；首舰变速（同步全队）与首舰转向（贪吃蛇）不视为切断。
- `BattleUIController` 按当前几何关系把单纵阵舰船分成组：组标题“单纵阵 1/2/3……”，组内成员前缀“阵1 / 阵2 / 阵3……”，非编队舰船仍逐行显示。

### 单纵阵多转向点轨迹

- `GameplayDirector` 新增 `FormationTrail`：记录首舰经过的格子与每格到达后的航向，后续舰按自身在轨迹中的滞后位置逐格消费。
- 首舰原地转向时也会更新当前格航向，即使本阶段不移动也不丢失转向点。
- `SyncFormationGroups` 先按旧整组保留贪吃蛇跟随中的编队，再对剩余舰船做几何检测，避免几何检测把跟随中的尾部拆成独立船。
- 玩家控制器改用运行时编队标记识别首舰，首舰在无移动阶段连续转向不会被误当成独航舰而清除标记。

### 射界高亮材质

- `OverlayDrawRequested` 信号增加航向与可用射界掩码；`GridOverlayController` 按目标方向把前/后射界格设为 `AttackFrontMaterial`、侧射格设为 `AttackMaterial`。
- `battle_scene.tscn` 已绑定 `mat_attack_front.tres`；主/副炮火力均为 0 的射界不参与高亮。

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
- 新增 `SyncFormationGroups`：把 `FormationLead` / `FormationIndex` 写入运行时编队标记，供列表与移动结算使用。
- 头舰变速时自动给全部编队船写入 pending，并只收 1 CP；头舰转向只写自身待命，移动阶段后船逐格进入前船让出的位置，到达转向格时才转向（贪吃蛇）。
- `ShipComponent.AnimateMovePath` 逐格动画并按位移更新航向；`SelectNextShip` 跳过编队后续舰，玩家仍可手动点击覆盖或离队。
- 左右舰船列表对单纵阵船只显示“单纵阵”标记。

### 舰船数据防重写

- `ShipData` 的船体阈值与状态速度从 `int[]` 改为标量字段（`HullLight/HullModerate/HullHeavy/HullSunk`、`SpeedIntact/SpeedLight/SpeedModerate/SpeedHeavy`），Godot 重存资源时不再丢失数组字段。
- 四艘原型舰资源均显式写入对应数值，白露的 1/2/3/4 船体阈值不再回退到默认值。

### 地形与冲撞

- 岛屿格不可进入，移动结算中撞岛船直接进入沉没状态并保留尸体。
- 移动受阻时先判断阻挡来源：舰船占位触发 `1D10≤2` 冲撞检定，命中后按简化 A3 表（按船体值之和分段）对双方造成损伤；完整 A3 表与超堆叠检定留待后续。

### 视野与雷达

- 新增 `VisionRulesEvaluator`：基本视野 + 岛屿视线遮挡；`RadarRulesEvaluator` 按雷达型号提供距离与命中修正。
- 玩家与 AI 炮击目标均需通过 `CanEngage`；雷达激活时可越过基本视野交战，并在命中判定中应用型号修正。

### 编辑器新建画布与关卡设置

- 主菜单与编辑器内新建画布弹窗均可逐阶段配置 `PhaseSecondsPerShip`（速度/三移动/视野/炮击/鱼雷/结算每船秒数）、`PhaseExtraSeconds` 与 `TorpedoPhaseEnabled`，写入 v3 JSON。
- `LevelDataManager.NewMap` 已接收并保存这些参数；旧地图缺省值不变。
- 编辑器新增“关卡设置”按钮：打开已有画布后可修改地图昼夜、主动权归属、双方 CP/指挥、基本视野、回合数、阶段限时、鱼雷模式与开关，不改变名称、朝向与地形/船表。
- `LevelDataManager` 新增 `ApplyScenarioSettings` / `SetMapType` / `SetTorpedoModes` 接口，保存时随 v3 JSON 落盘。

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
- 单纵阵贪吃蛇 headless 冒烟：三船同向纵队，头舰左转后第一移动阶段第一跟随舰到达转向格即转向，第二跟随舰第二移动阶段到达转向格时转向，左右列表含“单纵阵”标记。
- 舰船数据 headless 冒烟：`ShipData` 标量船体阈值/状态速度加载正确，Godot 重存资源后数值保留。
- 撞岛 headless 冒烟：在船正前方设置岛屿并推进移动阶段后 `DamageState=Sunk`，船体保留原位。
- AI 相机/视野 headless 冒烟：触发 `PlayerSideFinished` 后敌舰逐艘俯视运镜，阶段推进无报错。
- 规则 headless 冒烟：编辑器 `NewMap` 可写入关卡初设；四艘舰船资源/场景可加载；南达科他受 21 点损伤后连推七个阶段到下一回合速度调整阶段，延迟降速流程无报错。
- 目录冒烟：移除 `ShipList` 后，战斗场景仍能通过 `ShipCatalog` 从运行时目录生成舰船，且 3D 地图生成无报错。
- NS 投影冒烟：空 NS 地图放入 (0,0)/(1,0)/(0,1)/(-1,1) 后，3D 瓦片中心呈 0°/60°/120° 尖角六边形排列，无错位留空。
- 限时冒烟：阶段倒计时标签随时间递减，进度条可见且数值同步更新。
- pending 冒烟：速度指令写入 pending 后 `CurrentSpeed` 不变；手动推进后提交速度并自动接敌方、推进到下一阶段。
- 指挥值/PV 冒烟：玩家大破、敌方中破后指挥值分别减为 3/3，CP 上限收缩为 6/6，PV 分数 4/21。
- 单纵阵提示冒烟：独航舰减速按钮显示“组成”，首舰转向不显示“切断”，手动选中跟随舰后转向按钮显示“切断”；左右列表出现“单纵阵 1”与“阵1/阵2”分组。
- 多转向点冒烟：三船速度 5 执行“第一移动左转、第二移动待命、第三移动再左转”，下一回合尾舰追到第二个转向点后三船同向同排；无移动阶段连续两次转向后编队保留并正常追位。
- 射界材质冒烟：对朝北舰船发出射程 2 的全射界高亮，正前/正后格为纯红 `mat_attack_front`，其余四向侧射格为浅红 `mat_attack`，六向材质全部符合 60°/120° 定义。
- 几何射界冒烟：按 `FiringArcEvaluator` 顺时针 60° 对齐后的角度规则验证六邻格分类，`mat_attack_front`/`mat_attack_side` 选色与 60°/120° 定义一致。
- BattleUI 冒烟：`battle_smoke` 在指挥状态栏移至 `TopCenterPanel`、左右列表移入 `ScrollContainer` 后仍能正常完成阶段推进与炮击流程。
- MarginContainer 冒烟：左右列表改为 `MarginContainer` 布局后 `battle_smoke` 仍能正常加载、推进阶段并完成炮击。
- 堆叠冒烟：友军舰船在移动阶段进入同格后 `HexCoords` 相同，Y 保持 0.3，水平距离自动错开 0.5；`formation_smoke` / `battle_smoke` 均通过。
- 编辑器新建画布 headless 冒烟：`LevelDataManager` 写入/读回 `PhaseSecondsPerShip`、`PhaseExtraSeconds`、`TorpedoPhaseEnabled`；编辑器 `editor_ui.tscn` 与主菜单 `map_select_menu.tscn` 实例化无报错。
- 编辑器关卡设置 headless 冒烟：`ApplyScenarioSettings` 写入/读回夜战、主动权归属与鱼雷模式；编辑器“关卡设置”弹窗隐藏名称/朝向字段并正常打开。
- 副炮 headless 冒烟：南达科他在炮击阶段对距离 3 敌舰开火，主炮与副炮各产生独立命中检定记录。
- 雷达技能 headless 冒烟：基本视野 1、敌舰距离 6，轻巡先开雷达再炮击可选中目标，无“目标不在视野”拒绝日志。
- 尚未做真人编辑器内点击验证。

## 4. Git 状态与提交建议

- 已提交：`c5a22fa`（堆叠局部侧向轴）、`d1102a5`（堆叠 Z 轴并排）、`16259d1`（同阵营堆叠）、`7e3a7f3`（左右列表 MarginContainer）、`33773c6`（指挥/CP 顶部居中与列表弹性滚动）、`3d80edd`（射界 60° 对齐与侧射材质）、`a9c074f`（几何射界 60°/120°）、`0a73f3e`（射界 60°/120° 修正）、`57f8b5a`（前/侧射界材质）、`e432d8b`（炮击范围按射界区分前后/侧射材质）、`42b6ca9`（单纵阵多转向点轨迹与无移动阶段转向）、`fa45e6f`（单纵阵组成/切断提示与列表分组）、`dada6ec`（指挥值随损伤减少、双方 CP 与 PV 评分）、`fd57304`（单纵阵转向格立即转向与转向补间动画）、`bf754a2` / `a3d46ae`（贪吃蛇转向与文档同步）及更早的编辑器关卡设置系列。
- 本次提交包含堆叠局部侧向偏移源码与三份文档；工作区中 `BattleUI.tscn` / `BattleUIController.cs` 的 UI 微调、`Ships/*/*_data.tres`、`export/maps/1.json` 与未跟踪的 `export/maps/4.json` 是用户/编辑器产物，未纳入提交。

## 5. 已知问题与下一步

- 真人编辑器内炮击、分阶段移动动画、滚轮缩放需要实际点击验证。
- 单纵阵贪吃蛇移动已接入，规则书要求的解散判定、堆叠内编队等边界仍需逐步精确。
- 冲撞目前是简化 A3 表，完整表 A3 与移动后超堆叠检定尚未精确实现。
- 夜战照明阶段（`ReconLighting`）只有逻辑占位，尚无探照灯/照明弹玩法。
- 烟幕、照明弹、鱼雷仍是后续实体玩法；雷达技能已接入，探照灯未做。
- 后续可做：主动权规则、完整 A3 冲撞表与超堆叠检定、鱼雷阶段、烟幕/照明弹技能、舰船专属移动动画与炮口火光/水花特效。

## 6. 测试纪律

- 运行 Godot headless 测试后，不得按进程名批量 `Stop-Process`，否则可能误杀用户打开的带窗口 Godot 编辑器。
- 只清理本次测试自己启动的精确 PID：启动时记录 PID，结束后仅对该 PID 做收尾。
- 若进程已自行退出，不要执行任何 Godot 进程清理。
