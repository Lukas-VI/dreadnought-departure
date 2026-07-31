using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 调色板条目按钮（Scenes/Editor/palette_swatch.tscn）。
/// Entry 由 EditorUIController 在实例化后注入，_Ready 时把色块与名称填到子节点。
/// </summary>
public partial class PaletteSwatchButton : Button
{
	[Export] public EditorTileEntry Entry;

	public override void _Ready()
	{
		if (Entry == null) return;
		GetNode<ColorRect>("Content/Swatch").Color = Entry.Swatch;
		GetNode<Label>("Content/NameLabel").Text = Entry.DisplayName;
		TooltipText = Entry.DisplayName;
	}
}
