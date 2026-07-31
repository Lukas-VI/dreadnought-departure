using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 编辑器调色板条目：分类（Terrain/Generation/Special）、tileset 源 ID、显示名、色块。
/// 独立成文件是为了让 .tres 子资源正确实例化为本类型。
/// </summary>
[GlobalClass]
public partial class EditorTileEntry : Resource
{
	[Export] public string Category = "Terrain";
	[Export] public int SourceId;
	[Export] public string DisplayName = "未命名";
	[Export] public Color Swatch = Colors.White;
}
