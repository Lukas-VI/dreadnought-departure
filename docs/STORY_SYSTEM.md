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

## 触发器

`Data/Stories/triggers.json` 配置事件与脚本的映射：

```json
{
  "triggers": [
    { "event": "battle_start", "key": "", "script": "battle_start" },
    { "event": "player_action", "key": "turn_left", "script": "tutorial_turn" },
    { "event": "special_cell", "key": "1", "script": "special_1" }
  ]
}
```

当前事件：

- `battle_start`：单机战斗开始
- `battle_end`：战斗结束
- `player_action`：玩家选择操作（`key` 为操作 ID）
- `special_cell`：舰船进入特殊格（`key` 为 Special 表值）

脚本只播放一次；`StoryDirector.SetFlag / GetFlag` 可用来做后续条件判断。

## 接口入口

- 主菜单“剧情示例”按钮直接播放 `tutorial`
- 战斗内由 `StoryDirector` 监听 `EventBus` 自动触发
- 任意代码可调用 `StoryDirector.Instance?.Play("script_id")`
