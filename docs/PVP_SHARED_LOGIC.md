# PVP 与单机操作逻辑审计

> 目标：PvP 与单机尽量共用操作意图、结算规则与表现层，避免演化为两套系统。
> 审计日期：2026-08-03；客户端 `codex/pvp-client`，服务端 `Lukas-VI/dreadnought-server`。

## 1. 阶段序列现状

阶段以“我方 / 敌方各操作一轮算一回合”为周期，不按奇偶回合区分行动方。

| 阶段 | 单机 `GameplayDirector` | PvP 服务端 `battleState.js` |
| --- | --- | --- |
| 速度调整 | `SpeedAdjust` | `speed` |
| 第一移动 | `MovePhase1` | `move1` |
| 第二移动 | `MovePhase2` | `move2` |
| 第三移动 | `MovePhase3` | `move3` |
| 视野/照明 | 仅夜战进入 `ReconLighting` | 夜战进入 `recon`，双方自动提交待命 | 已对齐（无交互，自动待命） |
| 炮击 | `Gunfire` | `gunnery` |
| 鱼雷 | 仅开关启用时进入 `Torpedo` | 开关启用进入 `torpedo`，服务端按左右舷雷击指令权威结算 | 已对齐（简化玩法已接入） |
| 回合结算 | `EndTurn` | `settlement` |

单机流转：`SpeedAdjust → MovePhase1/2/3 →（夜战 ReconLighting）→ Gunfire →（鱼雷开关 Torpedo）→ EndTurn → SpeedAdjust`。

PvP 服务端当前实际流转：`speed → move1 → move2 → move3 →（夜战 recon）→ gunnery →（鱼雷开关 torpedo）→ speed`。每阶段由 `activePlayer` 提交，双方都提交或倒计时归零后推进；`recon`/`torpedo` 由客户端自动提交待命，`gunnery` 后结算全灭/回合上限，再回到 `speed` 并交换先手。

## 2. 共享操作意图（已收敛）

单机与 PvP 的玩家输入共用同一套原生操作：

1. `PlayerController.BeginPhaseAction` 逐船选中，`PhaseActionMenu` 写 `ShipComponent` 待命字段：
   - `PendingSpeed`：速度调整
   - `PendingDirection`：移动阶段转向
   - `PendingAttackTarget`：炮击目标
2. 单机结算：`GameplayDirector.CommitPendingCommands` 通过 `CommandIntentBuilder.Build` 消费同一批字段并本地判定。
3. PvP 提交：同一个 `CommandIntentBuilder.Build` 生成 `ShipCommandIntent`，再通过 `ToWire()` 打包成服务端 `battle.command`。

因此“选船 → 卡片 → 待命字段”这一段只有一套实现；差异只发生在结算入口。

## 3. 共享表现层（已收敛）

- 3D 地图：`LevelDataManager` + `MapGenerator`，编辑器、战役、PvP 共用同一份 JSON。
- 舰船模型：`ShipCatalog.GetScene(ShipId)`，按地图 `Ships` 初设选预制体。
- 朝向：服务端 `facing` 与 `HexDirection` 对齐（N/NE/SE/S/SW/NW），客户端直接转换。
- 移动补间：`ShipComponent` position tween。
- 转向补间：`ShipComponent.AnimateTurnTo`，初设朝向不标记转向。
- 堆叠：同格同阵营按局部侧向轴并排。
- 单纵阵：`MoveRulesEvaluator.SyncFormationGroups`（单机）与服务端 `computeFormations` 使用同一几何规则。

## 4. 逐项差异审计

| 项目 | 单机 | PvP | 状态 |
| --- | --- | --- | --- |
| 玩家输入 | 原生逐船操作 | 同一套原生操作，提交走 `CommandIntentBuilder` | 一致 |
| 阶段流程 | 见 §1 | 见 §1；`recon`/`torpedo` 按地图配置进入并自动待命 | 已对齐 |
| 指令提交 | 手动推进后本地立即结算 | `battle.command` 每阶段提交，双方提交或超时后由服务端结算 | 已对齐 |
| 结算权威 | 本地 `GameplayDirector` | 服务端 `battleState`，客户端只渲染 | 有意差异 |
| 速度调整 | 本地 SpeedTable + 回合末延迟降速 | 服务端 `SPEED_TABLE`，动作扣 CP，损伤降速在回合结算延迟生效 | 已对齐 |
| 移动格数 | `SpeedTable.MoveForPhase` | 服务端 `SPEED_TABLE` 镜像 | 基本一致 |
| 转向时机 | 移动阶段先沿原航向移动、阶段结束再转向 | `buildMovePath` 先直行、末尾应用 turnDelta | 已对齐（规则勘误） |
| 移动阻挡 | 岛屿沉没、舰船阻挡、冲撞规则 | 逐格检查岛屿、堆叠上限与敌格，触发简易冲撞 | 已对齐（简易冲撞） |
| 堆叠 | 本地结算，上限 2 | 服务端权威上限 2，超限回滚 | 规则一致 |
| 同格堆叠选中 | 点击循环切换并记住每格上次选中 | PvP 沿用同一交互 | 已对齐 |
| 单纵阵 | 客户端 `FormationTrail` 逐格消费 | 服务端 `trails` 逐格消费并广播 | 已对齐 |
| 炮击判定 | CP 消耗、弹药、射界、距离、装甲伤害 | 服务端按上传 ShipData 结算 CP/弹药/射界/距离/装甲/雷达修正 | 已对齐 |
| CP / 指挥值 / PV | 本地规则，动作消耗 CP | 服务端扣 CP 并重算指挥值/PV；单纵阵整组变速/转向只扣 1 CP | 已对齐 |
| 计时 | 玩家侧 + AI 侧本地倒计时 | 服务端授时，客户端按服务器时钟展示并在截止前提交 | 已对齐 |
| 暂停 | Tree 暂停全部 | PvP 指挥器保持 Always，按服务器时间继续 | 已对齐 |
| 地图 | 本地 `export/maps` | 房主上传、房间缓存、对方下载 | 已对齐 |
| ShipData | 本地 `.tres` | 客户端随房间上传数值表 | 已对齐 |
| 敌方操作 | 本地 AI | 服务端对端玩家 | 有意差异 |
| 镜头 | 本地相机规则 | 进入按地形包围盒取景；选中船保持俯视聚焦，旗子平滑跟随 | 基本一致 |
| 主动权初设 | 单机读地图 `InitiativeOwner` | PvP 服务端读取同一字段，未配置时回退掷骰 | 已对齐 |
| 胜负判定 | 本地结果面板 | 全灭立即判胜；回合上限按 PV 判胜/平局并弹结果面板 | 已对齐 |
| 房间生命周期 | 无 | `finished` 房间从大厅隐藏，离开/断线后空房删除 | PvP 专有 |
| 断线重连/暂停 | 无 | 单方断线挂起对局且不删房；重连自动恢复；双方超时未回自动删除 | 已对齐 |

## 5. 仍需收敛的差异

- 视野交互：夜战 `recon` 已进入但双方自动待命；雷达/照明弹等交互玩法尚未实现。
- 鱼雷交互：`torpedo` 已按地图开关进入；客户端提交 `torpedo` 指令（左右舷 + 全数雷击），服务端发射、后续移动阶段移动并命中结算，鱼雷实体随 `battle.state` 广播。
- 完整冲撞表：双方目前都使用简化冲撞占位（1D10≤2 + 简易 A3 损伤），完整 A3 表待补。

## 6. 结论

输入、指令意图、速力表、堆叠、单纵阵轨迹、完整炮击、移动阻挡、CP 消耗与编队优惠、延迟降速、主动权初设、视野/鱼雷阶段骨架、服务器授时、胜负结算、房间生命周期与断线重连已收敛为同一套语义；主要剩余差异集中在视野/鱼雷交互玩法与完整冲撞表。

## 7. 本次审计的专项进展

- 战斗结束：PvP 弹结果面板；服务端全灭立即判胜，回合上限按 PV 判胜/平局。
- 房间生命周期：`finished` 房间不再出现在大厅，战斗结束标记房间，离开/断线后清理空房。
- 同格堆叠：点击六角格循环切换该格内的船，并记住每格上次选中的船。
- 旗子与相机：旗子作为船体子节点平滑跟随；相机保持俯视并聚焦选中船。
- 规则收敛：服务端按上传 ShipData 全字段结算完整炮击；逐格移动阻挡/岛屿沉没/冲撞；动作扣 CP；损伤延迟降速；地图主动权初设；远程弹药同步；新增 `rules` 测试。
- 阶段收敛：`recon`/`torpedo` 按地图配置进入并由客户端自动待命；单纵阵整组变速/转向只扣 1 CP。
- 断线收敛：单方断线服务端挂起对局并停止计时，重连后自动恢复；双方超时未回自动删除房间与战斗。
- 更早已收敛：单纵阵服务端轨迹重放、堆叠同步、服务器授时、ShipData 共用、共享 `CommandIntentBuilder`。

## 8. 鱼雷专项进展

- `ShipData` 导出鱼雷管/伤害/射程/航速/备用鱼雷；服务端载入并初始化每艘船的剩余鱼雷。
- 客户端 `CommandIntentBuilder` 新增 `torpedo` 指令（`side=-1/1`）；PvP 鱼雷阶段改为可交互，不再自动待命。
- 服务端 `battleState.js` 新增鱼雷实体：发射扣 CP、减少鱼雷管、按 A2 表在移动阶段移动、同格命中裁定并扣血、离图/耗尽消失，`publicState` 广播 `torpedoes`。
- 鱼雷扇面：客户端提交 `torpedo` 指令携带 `side` 与 `branch`；服务端按顶点侧双候选格贪心蛇行，`publicState` 同步 `fanSide / fanBranch`，客户端 `SyncRemoteTorpedoes` 与离线共用 `TorpedoController` / `torpedo.tscn`。
- 备用鱼雷：低速且未转向、上一回合未发射的舰船在新回合自动装填一次（离线与 PvP 一致）。
- 炮击开关：`GunfirePhaseEnabled` 默认开启，客户端与服务端均可跳过炮击阶段直接进入鱼雷/结算。
