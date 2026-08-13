using Godot;
using System.Linq;
using System.Threading.Tasks;

namespace DreadnoughtDeparture.Core;

public partial class GameplayDirector
{
	/// <summary>移动阶段自动执行该阶段位移：按 SpeedTable 推算格数，用 Tween 播放位移动画。</summary>
	private async Task AnimateMovePhase(int phase)
	{
		var bus = GetNode<EventBus>("EventBus");
		var allShips = _playerShips.Concat(_enemyShips).ToList();
		var occupiedShips = _moveSettlement.PrepareOccupied(allShips);
		bool oddTurn = _turnNumber % 2 == 1;
		var (ordered, chains) = _moveSettlement.OrderShips(allShips);

		float longest = 0f;
		for (int i = 0; i < ordered.Count; i++)
		{
			var ship = ordered[i];
			var chain = chains.FirstOrDefault(c => ReferenceEquals(c[0], ship));
			if (chain != null)
			{
				longest = Mathf.Max(longest,
					_moveSettlement.AnimateFormationChain(chain, phase, oddTurn, bus, occupiedShips));
				i += chain.Count - 1;
				continue;
			}
			longest = Mathf.Max(longest,
				_moveSettlement.AnimateStraightShip(ship, phase, oddTurn, bus, occupiedShips));
		}

		if (longest > 0f)
		{
			await ToSignal(GetTree().CreateTimer(longest + 0.1f), "timeout");
		}
		_moveSettlement.ApplyPendingTurns(allShips);
		_moveSettlement.RefreshStackOffsets(_playerShips, _enemyShips);
		RefreshDirectionOverlays();
		await _torpedoController.MoveTorpedoesAsync(_mapGenerator, _dataManager,
			phase, oddTurn, allShips);
		foreach (ShipComponent ship in allShips)
		{
			if (IsShipAlive(ship)
				&& _dataManager?.SpecialTiles.TryGetValue(ship.HexCoords, out int specialId) == true)
			{
				if (ship.BattleSide == GenerationSide.Player)
				{
					_playerReachedHexes.Add(ship.HexCoords);
				}
				else
				{
					_enemyReachedHexes.Add(ship.HexCoords);
				}
				bus.EmitSignal("SpecialCellEntered", ship.HexCoords, specialId);
			}
		}
	}

	/// <summary>刷新方向标记：只标记单纵阵头与独行舰，跟随舰不显示。</summary>
	private void RefreshDirectionOverlays()
	{
		if (_overlay == null) return;
		var entries = _playerShips.Concat(_enemyShips)
			.Where(IsShipAlive)
			.Where(ship => ship.FormationLead == null || ReferenceEquals(ship.FormationLead, ship))
			.Select(ship => (ship.HexCoords, ship.Direction))
			.ToList();
		_overlay.RefreshDirections(entries);
	}
}
