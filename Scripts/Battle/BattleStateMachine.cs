using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

public partial class BattleStateMachine : Node
{
	private GridOverlayController _overlay;
	private BattleHudBroker _hud;
	private MapGenerator _map;
	private Dictionary<Vector2I, ShipComponent> _activeShips;
	private ShipComponent _selectedShip;

	public void Setup(GridOverlayController overlay, BattleHudBroker hud, MapGenerator map,
					  Dictionary<Vector2I, ShipComponent> activeShips)
	{
		_overlay = overlay;
		_hud = hud;
		_map = map;
		_activeShips = activeShips;
	}

	public void OnHexClicked(Vector2I clickedHex)
	{
		if (_selectedShip == null)
		{
			if (_activeShips.TryGetValue(clickedHex, out var ship))
			{
				_selectedShip = ship;
				_selectedShip.ShowSelected(true);
				_overlay.DrawTacticalRange(_selectedShip.HexCoords, _selectedShip.MoveRange, _selectedShip.AttackRange);
				_hud.DisplayShipSelected(_selectedShip);
			}
		}
		else
		{
			if (_activeShips.TryGetValue(clickedHex, out var target) && target != _selectedShip)
			{
				int dist = BattleRulesEvaluator.GetHexDistance(_selectedShip.HexCoords, clickedHex);
				if (dist <= _selectedShip.AttackRange)
				{
					target.TakeDamage(_selectedShip.AttackPower);
					_hud.DisplayConsoleLog($"💥 主炮齐射！对 {target.ShipName} 造成 {_selectedShip.AttackPower} 点伤害！");
				}
				else _hud.DisplayConsoleLog("❌ 报告长官：目标在射程之外！");
			}
			else
			{
				int dist = BattleRulesEvaluator.GetHexDistance(_selectedShip.HexCoords, clickedHex);
				if (dist <= _selectedShip.MoveRange)
				{
					_selectedShip.MoveToHex(_map, clickedHex);
					_hud.DisplayConsoleLog($"⚓ 舰队已机动至：{clickedHex}");
				}
				else _hud.DisplayConsoleLog("❌ 报告长官：超出机动范围！");
			}

			_selectedShip.ShowSelected(false);
			_overlay.ClearOverlay();
			_selectedShip = null;
		}
	}
}
