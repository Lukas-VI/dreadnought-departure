using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 编辑器调色板资源。左侧面板按 Category 分组渲染 Entries 里的条目。
/// 条目类型为 EditorTileEntry，新增地形/标记时只需在 .tres 里增加一条。
/// </summary>
[GlobalClass]
public partial class EditorTileCatalog : Resource
{
	[Export] public Godot.Collections.Array<EditorTileEntry> Entries = new();
}
