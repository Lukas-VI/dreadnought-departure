# PVP 与单机共享逻辑

> 目标：PvP 与单机尽量共用操作意图、结算规则与表现层，避免演化为两套系统。

## 1. 共享操作意图

单机与 PvP 的玩家输入都来自同一套原生操作：

1. `PlayerController.BeginPhaseAction` 逐船选中，`PhaseActionMenu` 写 `ShipComponent` 待命字段：
   - `PendingSpeed`：速度调整
   - `PendingDirection`：移动阶段转向
   - `PendingAttackTarget`：炮击目标
2. 单机结算：`GameplayDirector.CommitPendingCommands` 通过 `CommandIntentBuilder.Build` 消费同一批字段并本地判定。
3. PvP 提交：同一个 `CommandIntentBuilder.Build` 生成 `ShipCommandIntent`，再通过 `ToWire()` 打包成服务端 `battle.command`。

因此“选船 → 卡片 → 待命字段”这一段只有一套实现；差异只发生在结算入口。

## 2. 共享表现层

- 3D 地图：`LevelDataManager` + `MapGenerator`，编辑器、战役、PvP 共用同一份 JSON。
- 舰船模型：`ShipCatalog.GetScene(ShipId)`，按地图 `Ships` 初设选预制体。
- 朝向：服务端 `facing` 与 `HexDirection` 对齐（N/NE/SE/S/SW/NW），客户端直接转换。
- 移动补间：`ShipComponent.AnimateMoveTo` / 自定义 position tween。
- 转向补间：`ShipComponent.AnimateTurnTo`。
- 堆叠：同格同阵营按局部侧向轴并排。
- 单纵阵：`MoveRulesEvaluator.SyncFormationGroups` 重建 `FormationLead / FormationIndex`，左右列表分组一致。

## 3. 当前差异

| 项目 | 单机 | PvP |
| --- | --- | --- |
| 阶段 | 速度/三段移动/视野/炮击/鱼雷/结算 | 速度/三段移动/炮击/结算 |
| 结算权威 | 本地 `GameplayDirector` | 服务端 `battleState`，客户端只渲染 |
| CP / 指挥值 / PV | 本地 HUD 与判定 | 服务端同步（简化：PV=10、指挥值按沉没数扣减） |
| 单纵阵 / 堆叠 | 结算与表现都走本地规则 | 堆叠由服务端校验；单纵阵标记由客户端共享规则重建 |
| 计时 | 玩家侧 + AI 侧倒计时 | 仅当前操作方倒计时，超时自动提交待命 |
| 地图 | 从 `export/maps` 加载 | 房主经战役选图上传，房间缓存，对方下载 |

## 4. 剩余补全项

- 服务端补视野/鱼雷阶段，或按地图开关跳过。
- CP / 指挥值 / PV 使用与单机相同的完整 ShipData 规则计算。
- 服务端权威计算单纵阵分组（当前客户端用 `MoveRulesEvaluator` 重建）。
- 服务端结算改为消费 `ShipCommandIntent` 的 wire 结构，让本地与 PvP 共用同一份“意图 → 结算”语义。
- 计时改为服务端权威阶段时限，客户端只展示。
