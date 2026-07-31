using Godot;
using System;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 战斗场景 UI 控制器（CanvasLayer 下的 Control）。
/// 监听 EventBus 信号更新底部 HUD 信息栏、阶段标签、CP 显示；
/// 管理 WheelMenu 的显示/隐藏；提供阶段推进和重试按钮回调。
/// </summary>
public partial class BattleUIController : Control
{
	private BattleHudBroker _hud;
	private WheelMenu _wheel;
	private ShipComponent _pendingShip;
	private Label _phaseLabel;

	public override void _Ready()
	{
		_hud = GetNodeOrNull<BattleHudBroker>("InfoLabel");
		_wheel = GetNodeOrNull<WheelMenu>("WheelMenu");

		// 阶段标签（VBoxContainer 下的第一个 Label）
		var vbox = GetNodeOrNull<VBoxContainer>("MarginContainer/MarginContainerMain/MarginContainer/HBoxContainer/BtnPanel/VBoxContainer");
		_phaseLabel = vbox?.GetNodeOrNull<Label>("Label");

		var bus = GetNode<EventBus>("../../EventBus");
		bus.LogMessage += (msg) => _hud?.DisplayConsoleLog(msg);
		bus.ShipInfoRequested += OnShipInfoRequested;
		bus.ActionSelected += OnActionSelected;
		bus.OverlayClearRequested += HideWheel;
		bus.PhaseChanged += OnPhaseChanged;
		bus.CpUpdated += OnCpUpdated;

		if (_wheel != null)
			_wheel.ActionSelected += OnWheelAction;
	}

/// <summary>选中舰船：更新 HUD 信息并缓存引用供轮盘菜单使用。</summary>
	private void OnShipInfoRequested(ShipComponent ship)
	{
		_hud?.DisplayShipSelected(ship);
		_pendingShip = ship;
	}

/// <summary>收到 actionId 后，若为 _show_wheel 则在舰船屏幕位置弹出轮盘菜单。</summary>
	private void OnActionSelected(string actionId)
	{
		if (actionId == "_show_wheel" && _pendingShip != null)
			_wheel?.Show(GetShipScreenCenter(_pendingShip), _pendingShip);
	}

/// <summary>轮盘菜单选中操作：隐藏轮盘并将 actionId 转发至 EventBus。</summary>
	private void OnWheelAction(string actionId)
	{
		HideWheel();
		GetNode<EventBus>("../../EventBus").EmitSignal("ActionSelected", actionId);
	}

/// <summary>计算舰船在屏幕空间的中心坐标，用于定位轮盘菜单。</summary>
	private Vector2 GetShipScreenCenter(ShipComponent ship)
	{
		if (!GodotObject.IsInstanceValid(ship))
			return GetViewport().GetMousePosition();

		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null)
			return GetViewport().GetMousePosition();

		return camera.UnprojectPosition(ship.GlobalPosition);
	}

/// <summary>阶段变更时更新 UI 中的阶段标签。</summary>
	private void OnPhaseChanged(string phaseName, int phaseIndex)
	{
		if (_phaseLabel != null)
			_phaseLabel.Text = phaseName;
	}

/// <summary>CP 更新时追加到阶段标签尾部。</summary>
	private void OnCpUpdated(int current, int max)
	{
		if (_phaseLabel != null)
			_phaseLabel.Text += $"  |  CP: {current}/{max}";
	}

/// <summary>隐藏轮盘菜单。</summary>
	public void HideWheel() => _wheel?.HideMenu();

	// 改为推进阶段，而非直接结束回合
/// <summary>推进阶段按钮回调——改为 AdvancePhase 而非 EndTurn。</summary>
	public void _OnEndTurnPressed()
	{
		HideWheel();
		GetNode<EventBus>("../../EventBus").EmitSignal("AdvancePhaseClicked");
	}

/// <summary>重试按钮：重新加载战斗场景。</summary>
	public void _OnRetryPressed() => GetTree().ChangeSceneToFile("res://Scenes/Battle/battle_scene.tscn");
}
