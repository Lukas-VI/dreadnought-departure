# Dreadnought Departure 交接文档

> 更新日期：2026-07-31
> 分支：`codex/ship-operations`
> 最后提交：`9d2de4c feat: 分阶段逐船操作与相机运镜反馈`

## 1. 当前状态

- 编辑器到游玩场景的数据链路已打通：编辑器画布 JSON 被 `LevelDataManager` 加载，`UnitSpawner` 按 `ShipId` 生成舰船。
- 分支上已完成第一版“分阶段逐船操作”：每个玩家阶段按存活舰船顺序排队，自动选中队首并弹出底部操作菜单。
- 本轮又完成：底部弹性菜单替代轮盘、结算阶段自动惯性移动（Tween）、相机滚轮缩放联动仰角、旧场景路径修复、炮击点击失效修复。
- 以上内容大部分仍在工作区未提交（见第 4 节），下一手可以直接继续。

## 2. 本轮改动明细

### 文件结构路径修复

- `Scripts/UI/Menu/MapSelectMenuController.cs`：`EditorScenePath` 改为 `res://Scenes/UI/Editor/editor_scene.tscn`，`MainMenuScenePath` 改为 `res://Scenes/UI/Menu/MainMenu/main_menu.tscn`。
- `Scripts/Editor/EditorSceneController.cs`、`Scripts/Editor/EditorUIController.cs`：主菜单路径同步指向新目录。
- 用户已自行更新的 `project.godot`、`MainMenuController.cs`、`PauseMenuController.cs`、`battle_scene.tscn` 保持原样，未回退。

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

- 去掉手动移动指令；机动改为结算阶段按航向/速度自动推算。
- 保留逐船队列：`BeginPhaseAction` 建队，`SelectNextShip` 自动轮到下一艘，点击非队首船会提示按顺序操作。
- 新增 `skip`（待命）指令，避免某船无可操作时卡住。
- 炮击：菜单点“炮击”后进入待命，点击敌舰/敌格确认目标；命中进入 `PendingDamage`，结算阶段落实。
- 相机反馈：变速以“船-推算到达格”中点聚焦，转向以船体俯视，炮击先拉高看射程、确认目标后以“船-敌舰”中点聚焦。

### 结算自动惯性移动

- `GameplayDirector.DoEndTurnSettlement` 改为 async：
  1. 落实所有船的 `PendingDamage`。
  2. 按 `SpeedTable.MoveForPhase(speed, 1..3, isOddTurn)` 求和，得到本回合总步数。
  3. 沿当前航向推算目标格，调用 `ShipComponent.AnimateMoveTo` 播放 Tween。
  4. 动画结束后进入下一回合。
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
- 工作区还有用户的文件结构重构未提交（场景移动/删除、`project.godot`、`export/maps/1.json` 等），提交时应与功能改动分开。

## 5. 已知问题与下一步

- 真人编辑器内炮击、结算移动动画、滚轮缩放需要实际点击验证。
- 敌方 AI 仍未接入阶段管线（`AIController` 仍是占位）。
- 结算移动未处理目标格占用/堆叠，后续需要碰撞或堆叠规则。
- 夜战照明阶段（`ReconLighting`）只有逻辑占位，尚无探照灯/照明弹玩法。
- 后续可做：舰船专属移动动画、炮口火光/水花特效、技能菜单卡片化、战役流程。