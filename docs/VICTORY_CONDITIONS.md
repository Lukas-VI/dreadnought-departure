# 关卡胜负条件接口

在画布 JSON 的顶层增加 `Victory` 字段即可覆盖默认“全灭 / PV”判定。

## 声明式示例

```json
{
  "Name": "map_02",
  "Version": 3,
  "MaxTurns": 10,
  "Victory": {
    "turnLimit": 10,
    "timeout": "loss",
    "conditions": [
      { "type": "reach", "side": "player", "hexes": ["2,0", "3,-1"], "count": 2 },
      { "type": "checkpoint", "checkpoint": "story_map_2_mid" },
      { "type": "destroy", "side": "player", "count": 2 }
    ],
    "defeatConditions": [
      { "type": "destroy", "side": "enemy", "count": 3 },
      { "type": "alive", "side": "player", "count": 0 }
    ]
  }
}
```

## 字段说明

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `turnLimit` | int | 规定回合数；达到后按 `timeout` 判定 |
| `timeout` | string | `loss`（默认）或 `draw` |
| `conditions` | array | 任意一条满足即胜利 |
| `defeatConditions` | array | 任意一条满足即失败 |

## 条件类型

### reach

指定阵营到达指定格：

```json
{ "type": "reach", "side": "player", "hexes": ["2,0", "3,-1"], "count": 2 }
```

### checkpoint

全局剧情检查点已播放：

```json
{ "type": "checkpoint", "checkpoint": "story_map_2_mid" }
```

也可以传 `checkpoints` 数组并设置 `count`。

### action

玩家累计执行指定操作次数：

```json
{ "type": "action", "action": "turn_left", "count": 3 }
```

### destroy

击毁指定数量的敌方舰船：

```json
{ "type": "destroy", "side": "player", "count": 2 }
```

### alive

指定阵营存活数量达到阈值：

```json
{ "type": "alive", "side": "player", "count": 1 }
```

`side` 为 `player` 或 `enemy`。未配置 `Victory` 时保持原有全灭 / PV 判定，不破坏现有地图。
