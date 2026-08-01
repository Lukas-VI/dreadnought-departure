using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// Main menu hub: routes to editor scene, campaign scene, or quits.
/// Connected by signal in main_menu.tscn.
/// </summary>
public partial class MainMenuController : Control
{
	[Export] public string MapSelectMenuPath = "res://Scenes/UI/Menu/MainMenu/map_select_menu.tscn";

	/// <summary> 编辑器入口：先进入画布选择菜单，编辑器模式打开地图编辑器。 </summary>
	public void _OnEditorPressed()
	{
		MapSelectMenuController.PendingMode = "editor";
		GetTree().ChangeSceneToFile(MapSelectMenuPath);
	}

	/// <summary> 战役入口：先进入画布选择菜单，战役模式加载所选画布。 </summary>
	public void _OnCampaignPressed()
	{
		MapSelectMenuController.PendingMode = "campaign";
		GetTree().ChangeSceneToFile(MapSelectMenuPath);
	}

	/// <summary> Quit button: close the application. </summary>
	public void _OnQuitPressed() => GetTree().Quit();
}
