using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

public partial class PlayerController : Node, IUnitController
{
	private MapGenerator _map;
	private GridOverlayController _overlay;
	private BattleUIController _ui;
	private List<ShipComponent> _myUnits, _enemyUnits;
	private Action _onComplete;
	private ShipComponent _selected;
	private string _pendingAction; // 轮盘选的动作：move/attack/turn_left/...
	private int _actionsLeft;

	public void Setup(BattleInputDetector input, BattleUIController ui)
	{
		input.HexClicked += OnHexClicked;
		_ui = ui;
		ui.ActionSelected += OnWheelAction;
		ui.EndTurnClicked += () => ForceEndTurn();
	}

	public void TakeTurn(List<ShipComponent> myUnits, List<ShipComponent> enemyUnits,
						 MapGenerator map, GridOverlayController overlay, BattleHudBroker hud,
						 Action onComplete)
	{
		_map = map; _overlay = overlay;
		_myUnits = myUnits; _enemyUnits = enemyUnits;
		_onComplete = onComplete;
		_selected = null; _pendingAction = null;
		_actionsLeft = myUnits.Count;
		_ui.Log("⚓ 您的回合，请下达指令。");
	}

	private void OnWheelAction(string actionId)
	{
		if (_selected == null) return;
		_pendingAction = actionId;
		
		if (actionId == "move")
			_overlay.DrawTacticalRange(_selected.HexCoords, _selected.MoveRange, 0);
		else if (actionId == "attack")
			_overlay.DrawTacticalRange(_selected.HexCoords, 0, _selected.AttackRange);
		else
			ExecuteInstantAction(actionId); // turn/speed 不需要点格子
	}

	private void OnHexClicked(Vector2I hex)
	{
		if (_myUnits == null) return;
		if (_selected == null)
		{
			var ship = _myUnits.Find(s => s.HexCoords == hex && GodotObject.IsInstanceValid(s));
			if (ship != null && ship.CurrentHp > 0)
			{
				_selected = ship;
				_selected.ShowSelected(true);
				_ui.ShowShipInfo(ship);
				_ui.ShowWheel(GetScreenPos(ship), ship);
			}
		}
		else if (_pendingAction != null)
		{
			ExecutePendingAction(hex);
		}
	}

	private void ExecutePendingAction(Vector2I hex)
	{
		if (_enemyUnits == null) return;
		int d = BattleRulesEvaluator.GetHexDistance(_selected.HexCoords, hex);

		if (_pendingAction == "attack")
		{
			var target = _enemyUnits.Find(s => s.HexCoords == hex && GodotObject.IsInstanceValid(s));
			if (target != null && d <= _selected.AttackRange)
			{
				target.TakeDamage(_selected.AttackPower);
				_ui.Log($"💥 对 {target.ShipName} 造成 {_selected.AttackPower} 点伤害！");
				EndAction();
			}
			else _ui.Log("❌ 目标无效或不在射程内！");
		}
		else if (_pendingAction == "move")
		{
			if (d <= _selected.MoveRange)
			{
				_selected.MoveToHex(_map, hex);
				_ui.Log($"⚓ 舰队已机动至：{hex}");
				EndAction();
			}
			else _ui.Log("❌ 超出机动范围！");
		}
	}

	private void ExecuteInstantAction(string id)
	{
		if (id == "turn_left")
			_ui.Log("↩ 左转 60°（航向系统待实现）");
		else if (id == "turn_right")
			_ui.Log("↪ 右转 60°（航向系统待实现）");
		else if (id == "speed_up")
			_ui.Log("⬆ 加速（航速系统待实现）");
		else if (id == "speed_down")
			_ui.Log("⬇ 减速（航速系统待实现）");

		EndAction();
	}

	public void ForceEndTurn() { _onComplete?.Invoke(); }

	private void EndAction()
	{
		if (_selected != null) _selected.ShowSelected(false);
		_overlay.ClearOverlay();
		_ui.HideWheel();
		_selected = null; _pendingAction = null;
		_actionsLeft--;
		if (_actionsLeft <= 0) _onComplete?.Invoke();
		else _ui.Log($"⚓ 还有 {_actionsLeft} 次行动。");
	}

	private Vector2 GetScreenPos(ShipComponent ship)
	{
		var cam = GetViewport().GetCamera3D();
		return cam != null ? cam.UnprojectPosition(ship.GlobalPosition) : Vector2.Zero;
	}
}
