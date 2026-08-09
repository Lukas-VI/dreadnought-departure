# 对话与演绎系统

## 脚本格式

对话脚本放在 `res://Data/Stories/<script_id>.json`：

```json
{
  "background": "#0b0f17",
  "steps": [
    { "type": "say", "speaker": "指挥系统", "text": "对话内容" },
    { "type": "choice", "text": "问题", "options": [
      { "text": "选项 A", "next": 2 },
      { "text": "选项 B", "next": 4 }
    ] },
    { "type": "wait", "seconds": 1 },
    { "type": "flag", "key": "tutorial_done", "value": true },
    { "type": "background", "color": "#101a2b" }
  ]
}
```

支持步骤：

- `say`：显示说话人与正文
- `choice`：显示选项，`next` 指向步骤下标
- `wait`：等待秒数
- `flag`：写入剧情状态
- `background`：切换背景色

## 触发器与检查点

触发器拆成两份：

- `Data/Stories/global.json`：全局规则，不绑定地图
- `Data/Stories/maps/<mapName>.json`：当前地图配套规则

```json
{
  "triggers": [
    { "event": "battle_start", "checkpoint": "story_map_1_started", "script": "battle_start_map_1" },
    { "event": "special_cell", "key": "1", "checkpoint": "story_special_1", "script": "special_1" }
  ]
}
```

当前事件：

- `battle_start`：单机战斗开始
- `battle_end`：战斗结束
- `player_action`：玩家选择操作（`key` 为操作 ID）
- `special_cell`：舰船进入特殊格（`key` 为 Special 表值）

`checkpoint` 是全局剧情检查点，写入 `user://story_flags.json`。检查点已播放后，即使换关卡也不会重复触发；没有 `checkpoint` 的规则只在当前会话内播放一次。
`StoryDirector.SetFlag / GetFlag` 可用于更细的剧情条件。

## 对话 UI

对话界面是节点化场景 `Scenes/UI/Dialogue/dialogue_ui.tscn`：

- `CanvasLayer.layer = 100`，播放时始终在最顶层
- 背景半透明，战斗/主界面仍可见
- 底部对话框、说话人、正文、继续按钮、选项区均为独立节点

`DialogueRunner` 只负责读取 JSON 脚本并驱动 UI，不再在代码里拼 UI。

## 特殊格类型约定

地图 JSON 的 `Special` 表数值对应：

| 值 | 类型 | 用途 |
| --- | --- | --- |
| 1 | 剧情触发 | 进入后播放 `special_cell` 剧情 |
| 2 | 遭遇战 | 触发遭遇 / 敌人增援 |
| 3 | 补给点 | 补给、回复、领奖励 |
| 4 | 雷达站 | 解锁视野 / 雷达范围 |
| 5 | 危险区 | 警告、地形伤害、事件惩罚 |
| 6 | 目标点 | 任务目标、胜利/计分点 |

特殊格资源占位位于 `Scenes/Map/Tile/Prefab/SpecialCell/`，六个类型各一个 `.tscn`。编辑器绘制顺序为：地形 → 生成点 → 特殊格 → 网格 → 选区，特殊格在最顶层。
可通过 `SpecialCellCatalog.Name / ScenePath / ColorFor(specialId)` 读取名称、场景路径与颜色。

## 接口入口

- 主菜单“剧情示例”按钮直接播放 `tutorial`
- 战斗内由 `StoryDirector` 监听 `EventBus` 自动触发
- 任意代码可调用 `StoryDirector.Instance?.Play("script_id")`
