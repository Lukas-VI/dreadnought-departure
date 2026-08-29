# 剧情 / 关卡协作工单

> 剧情与关卡设计侧提出的接口缺口，供引擎 / 战斗侧代理领取。

## TICKET-001：阶段倒计时会在剧情播放期间启动（教学关体验受影响）

- 状态：待处理
- 优先级：P1
- 关联：00-01 移动教学关（`export/maps/00-01.json` + `Data/Stories/maps/00-01.json`）

### 现象

`caf5271` 已实现“剧情播放时暂停阶段倒计时”（`GameplayDirector.OnStoryPlaybackStarted` 调用
`CancelPhaseTimer()`，结束恢复）。但只在剧情开始那一刻取消倒计时，没有阻止剧情进行中的阶段启动：

1. `StartBattle()` 先 `EmitSignal("BattleStarted")`（同步触发 `battle_start` 剧情），随后才
   `BeginPlayerPhase()` → `StartPhaseTimer(true)`。因此 00-01 的开场剧情（同步协议说明，约 5 步）
   播放期间，速度阶段倒计时（约 10 秒）仍在走，超时会把阶段自动推进到对话底下。
2. 移动阶段到达特殊格时（如 00-01 的目标点 `4,4`），`SpecialCellEntered` 在移动动画完成后发出，
   此时阶段过渡尚未完成；`FinishPhaseTransition` → `BeginPlayerPhase` 仍会启动倒计时，
   到达剧情（`campaign/00/01/arrive`）在倒计时下播放。

### 建议修复

- `GameplayDirector.StartPhaseTimer(bool forPlayer)` 开头增加守卫：
  `if (_storyPlaying) return;`（剧情结束时 `OnStoryPlaybackEnded` 已有恢复逻辑，会重新启动）。
- 可选：`BeginPlayerPhase` / `FinishPhaseTransition` 在剧情播放期间跳过启动，统一由
  `OnStoryPlaybackEnded` 恢复，避免重复启动 / 重置倒计时。

### 验收

- 00-01 开场剧情与到达剧情播放期间，阶段倒计时不递减、不自动推进；
- 剧情结束后倒计时从完整时长重新开始。

## TICKET-002：脚本里 8 位色值 #RRGGBBAA 的 alpha 会被默认值覆盖

- 状态：待处理
- 优先级：P2
- 关联：`Scripts/Story/NarrativeState.cs`（`ParseBackground` / `ParseColor`）

### 现象

`ParseColor` 支持 `#RRGGBBAA`，但 `ParseBackground` 对字符串背景的 `alpha` 默认是 `1f`，
因此剧本里写 `"background": "#0e2a4500"` 时，`DialogueRunner` 传入 `alpha=1f` 会覆盖
十六进制的 `00`，背景仍是全不透明。只有对象写法
`"background": { "color": "#0e2a45", "alpha": 0 }` 或顶层 `"alpha"` 字段能生效。

### 建议修复

`ParseBackground` 解析到含 alpha 的 8 位色值且没有显式 `alpha` 字段时，
返回 `alpha = -1f`，让 `ParseColor` 采用十六进制末两位，保证 `#RRGGBBAA` 按文档生效。
