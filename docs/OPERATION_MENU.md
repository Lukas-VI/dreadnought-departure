# 作战菜单

主菜单新增“作战”入口，先进入章节选择，再进入关卡选择。

两个独立场景：

- `chapter_select.tscn`：章节列表，进入时触发 `chapter_enter`
- `level_select.tscn`：当前章节的关卡数轴，进入关卡时触发 `level_enter`

## 关卡命名

官方关卡放在 `export/maps/`，文件名按“章节-关卡”：

```text
01-01.json
01-02.json
02-01.json
```

章节场景会自动扫描这类文件并分组；关卡场景把当前章节的关卡按序号横向排开。

## 章节记忆

选择的章节会保存到 `user://operation_state.cfg`，下次进入自动停在上一章。

## 剧情事件

- 进入章节：`chapter_enter`，`key` 为章节号（`chapter_select.tscn`）
- 进入关卡：`level_enter`，`key` 为 `章节-关卡`（`level_select.tscn`）
- 完成关卡：`level_complete`，`key` 为地图名

示例触发规则见 `Data/Stories/global.json`。

## 开发者挂载

关卡只是普通地图 JSON，可直接在文本编辑器里写 `Terrain / Generation / Special / Ships / Victory`；
也可以把自定义场景挂到 `chapter_select.tscn` 或 `level_select.tscn` 的节点下，
再在对应 Controller 里扩展按钮或事件。
