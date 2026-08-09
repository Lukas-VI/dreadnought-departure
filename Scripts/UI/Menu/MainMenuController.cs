using Godot;
using DreadnoughtDeparture.Network;
using DreadnoughtDeparture.Story;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// Main menu hub: routes to editor scene, campaign scene, or quits.
/// Connected by signal in main_menu.tscn.
/// </summary>
public partial class MainMenuController : Control
{
	[Export] public string MapSelectMenuPath = "res://Scenes/UI/Menu/MainMenu/map_select_menu.tscn";
	[Export] public string DockScenePath = "res://Scenes/UI/Menu/Dock/dock_ui.tscn";
	[Export] public string PvpLoginMenuPath = "res://Scenes/UI/Network/login_menu.tscn";
	[Export] public string NetworkCenterScenePath = "res://Scenes/UI/Network/network_center.tscn";
	[Export] public string OperationMenuPath = "res://Scenes/UI/Menu/Operation/operation_menu.tscn";

	public override void _Ready()
	{
		var story = new StoryDirector();
		AddChild(story);
	}

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

	/// <summary> 船坞入口：查看已有舰娘数据卡。 </summary>
	public void _OnDockPressed() => GetTree().ChangeSceneToFile(DockScenePath);

	/// <summary> PvP 入口：先登录/注册，再进入联机大厅。 </summary>
	public void _OnPvpPressed()
	{
		PvpFlowState.LoginReturnPath = "res://Scenes/UI/Network/lobby_menu.tscn";
		GetTree().ChangeSceneToFile(PvpLoginMenuPath);
	}

	/// <summary> 网游中心：个人资料、背包、抽卡、商店、邮件。 </summary>
	public void _OnNetworkCenterPressed() => GetTree().ChangeSceneToFile(NetworkCenterScenePath);

	/// <summary> 剧情示例：演示主界面玩家操作可触发演绎。 </summary>
	public void _OnStoryPressed() => StoryDirector.Instance?.Play("tutorial");

	/// <summary> 作战入口：章节选择与官方剧情关卡。 </summary>
	public void _OnOperationPressed() => GetTree().ChangeSceneToFile(OperationMenuPath);

	/// <summary> Quit button: close the application. </summary>
	public void _OnQuitPressed() => GetTree().Quit();
}
