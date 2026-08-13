# 战斗系统架构

> 目标：让 `GameplayDirector` 从“上帝类”退化为编排者，规则与状态放进可测试的小类。

## GameplayDirector 拆分

`GameplayDirector` 现在是 partial 类，按职责分散到 `Scripts/Battle/`：

| 文件 | 职责 |
| --- | --- |
| `GameplayDirector.cs` | 核心初始化、故事暂停、计时器入口、经济接口、公共属性 |
| `GameplayDirector.Turn.cs` | 阶段推进、敌我回合调度、限时、新回合补给/装填 |
| `GameplayDirector.Commands.cs` | pending 指令提交、炮击、雷击发射 |
| `GameplayDirector.Move.cs` | 移动阶段编排入口 |
| `GameplayDirector.Settlement.cs` | 回合结算、胜负判定、关卡完成 |
| `GameplayDirector.Pvp.cs` | PvP 启动、连接、消息与远程状态应用 |

## 独立服务

- `BattlePhaseMachine`：阶段流转与“跳过照明/炮击/鱼雷”纯决策。
- `VictoryJudge`：自定义条件 / 全灭 / 回合上限的胜负纯决策。
- `MoveSettlementService`：逐格移动、阻挡、单纵阵轨迹、堆叠偏移与先走再转。
- `CombatSettlementService`：检定日志、命中演绎排序、PendingDamage 落实。
- `BattleEconomyState`：双方指挥值、CP、上限、PV 与延迟降速。
- `PvpSyncService`：远程阶段映射、远程舰船/鱼雷实体同步。

## 约定

- 规则求值尽量保持静态或实例化小服务，不依赖场景节点。
- 表现层（相机、Tween、反馈）留在 `GameplayDirector` 及其 partial，不混进规则服务。
- 单机与 PvP 共用 `CommandIntentBuilder`、`TorpedoRulesEvaluator`、`MoveSettlementService` 等同一套语义。
