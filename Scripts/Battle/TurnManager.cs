using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

public partial class TurnManager : Node
{
	private IUnitController _player, _enemy;
	private List<ShipComponent> _playerShips, _enemyShips;
	private MapGenerator _map;
	private GridOverlayController _overlay;
	private BattleUIController _ui;

	public void Setup(IUnitController player, IUnitController enemy,
					  MapGenerator map, GridOverlayController overlay, BattleUIController ui)
	{
		_player = player; _enemy = enemy;
		_map = map; _overlay = overlay; _ui = ui;
	}

	public void Start(List<ShipComponent> playerShips, List<ShipComponent> enemyShips)
	{
		_playerShips = playerShips; _enemyShips = enemyShips;
		CallDeferred(nameof(ShowBannerThenRun));
	}

	private async void ShowBannerThenRun()
	{
		_ui.Log("⚓ —— 你的回合 ——");
		await ToSignal(GetTree().CreateTimer(1.5f), "timeout");
		RunPlayerTurn();
	}

	private void RunPlayerTurn()
	{
		if (_playerShips.Count(s => GodotObject.IsInstanceValid(s) && s.CurrentHp > 0) == 0)
		{ EndBattle("敌方"); return; }
		_player.TakeTurn(_playerShips, _enemyShips, _map, _overlay, null,
			() => CallDeferred(nameof(RunEnemyTurn)));
	}

	private void RunEnemyTurn()
	{
		if (_enemyShips.Count(s => GodotObject.IsInstanceValid(s) && s.CurrentHp > 0) == 0)
		{ EndBattle("我方"); return; }
		_ui.Log("💀 —— 敌方回合 ——");
		_enemy.TakeTurn(_enemyShips, _playerShips, _map, _overlay, null,
			() => CallDeferred(nameof(ShowBannerThenRun)));
	}

	private void EndBattle(string winner)
	{
		_ui.Log($"🏆 {winner}胜利！");
	}
}
