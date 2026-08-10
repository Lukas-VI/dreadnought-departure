# 对话与演绎系统

## 脚本格式

对话脚本放在 `res://Data/Stories/<script_id>.json`：

`script_id` 可以是嵌套路径，例如 `campaign/01/00-01/start`，对应
`Data/Stories/campaign/01/00-01/start.json`。运行时允许把剧本按章节、事件等目录整理，
避免全部平铺在 `Data/Stories` 根目录。

```json
{
  "background": { "color": "#0b0f17", "alpha": 0.92 },
  "steps": [
    {
      "type": "say",
      "speaker": "指挥系统",
      "text": "对话内容",
      "avatar": {
        "path": "res://Ships/Xuefeng/portrait.png",
        "expression": "smile",
        "action": "wave",
        "position": "left",
        "scale": 1.0
      }
    },
    { "type": "choice", "text": "问题", "options": [
      { "text": "选项 A", "next": 2 },
      { "text": "选项 B", "next": 4 }
    ] },
    { "type": "wait", "seconds": 1 },
    { "type": "flag", "key": "tutorial_done", "value": true },
    { "type": "background", "background": { "color": "#101a2b", "alpha": 0.85 } }
  ]
}
```

支持步骤：

- `say`：显示说话人与正文；可带 `avatar`（立绘路径或对象）、`avatar_position`（`left` / `center` / `right`，默认 `left`）、`avatar_scale`
- `choice`：显示选项，`next` 指向步骤下标
- `wait`：等待秒数
- `flag`：写入剧情状态
- `background`：切换背景；`background` 或 `color` 字段支持 `#RGB / #RGBA / #RRGGBB / #RRGGBBAA`

立绘支持表情/动作差分，适合视觉小说演出。对象写法：

```json
{
  "avatar": {
    "path": "res://Ships/Xuefeng/portrait.png",
    "expression": "smile",
    "action": "wave",
    "position": "left",
    "scale": 1.0
  }
}
```

也可以拆成顶层字段：`avatar`、`avatar_expression`、`avatar_action`（或 `avatar_pose`）、
`avatar_position`、`avatar_scale`。路径中可使用 `{expression}` / `{action}` / `{pose}`
占位符；没有占位符时会依次尝试 `portrait_smile_wave.png`、`portrait_smile.png`、
`portrait_wave.png`，以及 `portrait/smile/wave.png` 这类目录结构，都找不到时回退到原路径。

背景可写成对象，用于视觉小说式配置：

```json
{
  "background": {
    "color": "#0e2a45",
    "alpha": 0.4,
    "image": "res://Data/Stories/backgrounds/harbor.png",
    "overlay": "#00000066"
  }
}
```

`color` / `alpha` 控制底色与透明度，`image` 为背景图，`overlay` 为叠加在背景上的遮罩色。
没有显式给出 `image` 时沿用上一帧背景图，立绘同理：不写 `avatar` 则沿用，写
`"avatar": "hidden"` 或 `"avatar_position": "hidden"` 可隐藏。

## 触发器与检查点

触发器拆成两份：

- `Data/Stories/global.json`：全局规则，不绑定地图
- `Data/Stories/maps/<mapName>.json`：当前地图配套规则

```json
{
  "triggers": [
    { "event": "battle_start", "checkpoint": "story_00_01_started", "script": "campaign/00/01/start" },
    { "event": "special_cell", "key": "1", "checkpoint": "story_00_01_turn_seen", "script": "campaign/00/01/turn" }
  ]
}
```

当前事件：

- `battle_start`：单机战斗开始
- `battle_end`：战斗结束
- `player_action`：玩家选择操作（`key` 为操作 ID）
- `special_cell`：舰船进入特殊格（`key` 为 Special 表值）

`checkpoint` 是全局剧情检查点，写入 `user://story_flags.json`，可供关卡目标与剧情标记使用。
是否播放由关卡选择界面的“观看剧情”开关决定：勾选时正常播放（已看过的剧情也会在重新进入关卡时再次播放），
取消勾选时跳过所有剧情触发，便于快速挑战与调试。开关持久化在 `user://story_settings.cfg`。
`StoryDirector.SetFlag / GetFlag` 可用于更细的剧情条件。

## 树形剧情索引

`Data/Stories/index.json` 是可选的树形索引，用于给剧本提供稳定的逻辑 id、
标题与事件元数据，也便于编辑器/调试界面浏览：

```json
{
  "title": "第一章",
  "children": [
    { "title": "进入章节", "script": "chapter_01_enter" },
    {
      "title": "01-01 剧情",
      "children": [
        { "title": "开场", "script": "00_01_start" },
        { "title": "转向教程", "script": "00_01_turn", "event": "special_cell", "key": "1" }
      ]
    }
  ]
}
```

- `id`：可选，不填时使用 `script`；子节点 id 会拼上父节点前缀，形成树形路径。
- `script`：相对 `Data/Stories` 的脚本路径（不含 `.json`）。
- `event` / `key`：可选触发器元数据，供关卡编辑器与剧情浏览器使用。
- `children`：嵌套子节点。

没有 `index.json` 时，`NarrativeCatalog` 会递归扫描 `Data/Stories` 自动建树，
跳过 `index.json` 与 `global.json`。根目录 `Story/` 是原稿草稿目录（已 gitignore），
不参与运行时剧情解析。

## NarrativeState 状态机

播放中的每一步都由 `NarrativeState` 持有：

- `ScriptId / Index / Status / Steps`：当前脚本、步骤下标、状态与全部步骤。
- `History`：已播放台词履历，`Flags`：剧情 flag。
- `Advance() / Back() / Jump(index) / SelectChoice(index)`：前进、回退、跳转、选选项。
- `Capture() / Restore(snapshot)`：保存/恢复完整快照，可用于调试、存档与演示。

`DialogueRunner` 只负责把 `NarrativeState` 渲染到 UI，推进、回退、恢复都直接操作状态机，
因此回溯后 UI、履历与 flag 会一致刷新。

## 对话 UI

对话界面是节点化场景 `Scenes/UI/Dialogue/dialogue_ui.tscn`：

- `CanvasLayer.layer = 100`，播放时始终在最顶层
- 背景半透明，战斗/主界面仍可见
- 底部对话框、说话人、正文、继续按钮、选项区均为独立节点

`DialogueRunner` 只负责读取 JSON 脚本并驱动 UI，不再在代码里拼 UI。

播放时右上角出现调试面板：

- 上一步 / 下一步：在状态机内回退或前进，立即重新渲染当前步骤。
- 跳转：输入步骤下标直接跳转。
- 保存快照 / 恢复快照：写入或读取 `user://narrative_snapshot.json`。

调试按钮调用 `StoryDirector.CaptureState() / RestoreState() / DebugBack() /
DebugNext() / DebugJump(index)`。

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

战场内使用 `Scenes/Map/Tile/Prefab/Overlay3D/Special/` 下的 3D overlay 场景显示特殊格；
已导入 Blender 模型为 `.tscn` ，`GridOverlayController.BuildSpecialCellOverlays()`
会自动按地图 `Special` 表实例化。

## 接口入口

- 主菜单“剧情示例”按钮直接播放 `tutorial/demo`
- 战斗内由 `StoryDirector` 监听 `EventBus` 自动触发
- 任意代码可调用 `StoryDirector.Instance?.Play("script_id")`
