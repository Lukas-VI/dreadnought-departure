# PVP 与单机操作逻辑审计

> 目标：PvP 与单机尽量共用操作意图、结算规则与表现层，避免演化为两套系统。

## 1. 共享操作意图（已收敛）

单机与 PvP 的玩家输入共用同一套原生操作：

1. `PlayerController.BeginPhaseAction` 逐船选中，`PhaseActionMenu` 写 `ShipComponent` 待命字段：
   - `PendingSpeed`：速度调整
   - `PendingDirection`：移动阶段转向
   - `PendingAttackTarget`：炮击目标
2. 单机结算：`GameplayDirector.CommitPendingCommands` 通过 `CommandIntentBuilder.Build` 消费同一批字段并本地判定。
3. PvP 提交：同一个 `CommandIntentBuilder.Build` 生成 `ShipCommandIntent`，再通过 `ToWire()` 打包成服务端 `battle.command`。

因此“选船 → 卡片 → 待命字段”这一段只有一套实现；差异只发生在结算入口。

## 2. 共享表现层（已收敛）

- 3D 地图：`LevelDataManager` + `MapGenerator`，编辑器、战役、PvP 共用同一份 JSON。
- 舰船模型：`ShipCatalog.GetScene(ShipId)`，按地图 `Ships` 初设选预制体。
- 朝向：服务端 `facing` 与 `HexDirection` 对齐（N/NE/SE/S/SW/NW），客户端直接转换。
- 移动补间：`ShipComponent` position tween。
- 转向补间：`ShipComponent.AnimateTurnTo`，初设朝向不标记转向。
- 堆叠：同格同阵营按局部侧向轴并排。
- 单纵阵：`MoveRulesEvaluator.SyncFormationGroups`（单机）与服务端 `computeFormations` 使用同一几何规则。

## 3. 逐项差异审计

| 项目 | 单机 | PvP | 状态 |
| --- | --- | --- | --- |
| 玩家输入 | 原生逐船操作 | 同一套原生操作，提交走 `CommandIntentBuilder` | 一致 |
| 阶段流程 | 速度 → 三段移动 → 视野（夜战）→ 炮击 → 鱼雷（开关）→ 结算 | 速度 → 三段移动 → 炮击 → 结算；视野/鱼雷按地图开关自动跳过 | 有差异 |
| 结算权威 | 本地 `GameplayDirector` | 服务端 `battleState`，客户端只渲染 | 有意差异 |
| 速度调整 | 本地 SpeedTable + 延迟降速 | 服务端 SpeedTable，降速即时生效 | 有差异 |
| 移动格数 | `SpeedTable.MoveForPhase` | 服务端 `SPEED_TABLE` 镜像 | 基本一致 |
| 移动阻挡 | 岛屿沉没、舰船阻挡、冲撞规则 | 仅敌格/堆叠上限阻挡，无岛屿/冲撞 | 有差异 |
| 堆叠 | 本地结算，上限 2 | 服务端权威上限 2，超限回滚 | 规则一致 |
| 单纵阵 | 客户端 `FormationTrail` 逐格消费 | 服务端 `trails` 逐格消费并广播 | 已对齐 |
| 炮击判定 | CP 消耗、弹药、射界、距离、装甲伤害 | 仅 70% 命中 + 1-4 随机伤，无 CP/弹药/射界/距离 | 有差异 |
| CP / 指挥值 / PV | 本地规则 | 服务端按客户端上传 ShipData 镜像计算 | 基本一致 |
| 计时 | 玩家侧 + AI 侧本地倒计时 | 服务端授时（start/end），客户端按服务器时钟展示，截止前 0.5s 提前提交 | 已对齐 |
| 暂停 | Tree 暂停全部 | PvP 指挥器保持 Always，按服务器时间继续 | 已对齐 |
| 地图 | 本地 `export/maps` | 房主上传、房间缓存、对方下载 | 已对齐 |
| ShipData | 本地 `.tres` | 客户端随房间上传数值表 | 已对齐 |
| 敌方操作 | 本地 AI | 服务端对端玩家 | 有意差异 |
| 镜头 | 本地相机规则 | PvP 进入时按地形包围盒取景 | 有差异 |

## 4. 仍需收敛的差异

- 视野阶段：单机夜战有视野/雷达阶段；PvP 当前自动跳过。
- 鱼雷阶段：单机按地图开关启用；PvP 当前自动跳过（符合“鱼雷后推”安排）。
- 炮击结算：PvP 需要补齐 CP 消耗、弹药、射界、距离与装甲伤害，与 `CombatRulesEvaluator.FireEx` 对齐。
- 移动阻挡：PvP 需要补齐岛屿沉没与冲撞规则，与 `ResolveMoveSteps` 对齐。
- 降速时机：单机在回合结算延迟降速；PvP 当前即时生效。
- 主动权初设：单机读地图 `InitiativeOwner`；PvP 当前开战掷骰并每回合交换。
- 镜头演绎：PvP 仍需补 AI 回合/敌方行动镜头（当前对端是真实玩家，尚无演绎需求）。

## 5. 结论

输入、指令意图、速力表、堆叠、单纵阵轨迹、服务器授时已收敛为同一套语义；主要剩余差异集中在 PvP 服务端炮击结算与移动阻挡规则，以及视野/鱼雷交互阶段。
