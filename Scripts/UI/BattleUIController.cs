using Godot;
using System;

namespace DreadnoughtDeparture.Core;

public partial class BattleUIController : Control
{
	[Signal] public delegate void ActionSelectedEventHandler(string actionId);
	[Signal] public delegate void EndTurnClickedEventHandler();

	private BattleHudBroker _hud;
	private WheelMenu _wheel;

	public override void _Ready()
	{
		_hud = GetNodeOrNull<BattleHudBroker>("InfoLabel");
		_wheel = GetNodeOrNull<WheelMenu>("WheelMenu");
		if (_wheel != null) _wheel.ActionSelected += (id) => EmitSignal(SignalName.ActionSelected, id);

		var endTurnBtn = GetNodeOrNull<Button>("BottomBar/EndTurnBtn");
		if (endTurnBtn != null) endTurnBtn.Pressed += () => EmitSignal(SignalName.EndTurnClicked);

		var retryBtn = GetNodeOrNull<Button>("BottomBar/RetryBtn");
		if (retryBtn != null) retryBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/Battle/battle_scene.tscn");
	}

	public void Log(string msg) => _hud?.DisplayConsoleLog(msg);
	public void ShowShipInfo(ShipComponent s) => _hud?.DisplayShipSelected(s);

	public void ShowWheel(Vector2 screenPos, ShipComponent ship) => _wheel?.Show(screenPos, ship);
	public void HideWheel() => _wheel?.HideMenu();
}
