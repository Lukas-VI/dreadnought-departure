using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 编辑器场景根控制器。
/// 画布库、工具栏、调色板与生成点检查器由 EditorUIController 管理；
/// 本类只负责返回主菜单的出口。
/// </summary>
public partial class EditorSceneController : Node2D
{
	[Export] public string MainMenuScenePath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";

	/// <summary>Connected to BackButton.pressed signal。</summary>
	public void _OnBackPressed() => GetTree().ChangeSceneToFile(MainMenuScenePath);
}
