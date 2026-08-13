using Godot;
using System.Linq;

namespace DreadnoughtDeparture.Core;

public partial class GameplayDirector
{
	/// <summary>手动推进时提交所有我方船的待命指令：速度、转向、炮击、雷击。</summary>
	private void CommitPendingCommands()
	{
		var bus = GetNode<EventBus>("EventBus");
		foreach (ShipComponent ship in _playerShips)
		{
			if (!IsShipAlive(ship))
			{
				ship.ClearPendingCommands();
			}
		}

		foreach (ShipCommandIntent intent in CommandIntentBuilder.Build(
			_playerShips.Where(IsShipAlive).ToList(),
			_currentPhase))
		{
			ShipComponent ship = intent.Ship;
			if (intent.Action == "accelerate" || intent.Action == "decelerate")
			{
				if (intent.TargetSpeed.HasValue && intent.TargetSpeed.Value != ship.CurrentSpeed)
				{
					int old = ship.CurrentSpeed;
					ship.CurrentSpeed = intent.TargetSpeed.Value;
					bus.EmitLog($"{ship.ShipName} 航速待命生效 {old} → {ship.CurrentSpeed}");
				}
			}
			else if (intent.Action == "turn_left" || intent.Action == "turn_right")
			{
				if (intent.TargetDirection.HasValue)
				{
					if (_currentPhase == BattlePhase.SpeedAdjust)
					{
						ship.AnimateTurnTo(intent.TargetDirection.Value);
						bus.EmitLog($"{ship.ShipName} 转向待命生效 → {ship.Direction}");
					}
					else
					{
						// 移动阶段先沿原航向行进，阶段结束时再执行转向。
						bus.EmitLog($"{ship.ShipName} 转向待命（先行进后转向 → {intent.TargetDirection.Value}）");
					}
				}
			}
			else if (intent.Action == "fire" &&
				intent.Target != null &&
				GodotObject.IsInstanceValid(intent.Target))
			{
				ShipComponent target = intent.Target;
				int attackCost = CommandRulesEvaluator.FireCPCost(ship);
				if (TryConsumeCP(attackCost) && ship.MainAmmo > 0
					&& CombatRulesEvaluator.CanFireInArc(ship, target))
				{
					ship.MainAmmo--;
					var (_, _, desc) = CombatRulesEvaluator.FireEx(ship, target,
						ship.PendingAttackDistance, ship.PendingRadarUsed);
					bus.EmitLog(desc);
				}
				else
				{
					bus.EmitLog($"{ship.ShipName} 炮击未执行（CP 或条件不足）");
				}
			}
			else if (intent.Action == "torpedo" && intent.TorpedoSide != 0)
			{
				LaunchTorpedo(ship, intent.TorpedoSide, false, intent.TorpedoBranch);
			}
		}

		bool deferTurn = _currentPhase is BattlePhase.MovePhase1
			or BattlePhase.MovePhase2 or BattlePhase.MovePhase3;
		foreach (ShipComponent ship in _playerShips)
		{
			if (IsShipAlive(ship))
			{
				ship.ClearPendingCommands(deferTurn);
			}
		}
	}

	/// <summary>发射一枚鱼雷齐射：校验 CP/资格、消耗鱼雷并生成占位实体。</summary>
	public bool LaunchTorpedo(ShipComponent ship, int side, bool enemySide = false,
		int branch = 0)
	{
		var bus = GetNode<EventBus>("EventBus");
		if (ship == null || !TorpedoRulesEvaluator.CanLaunch(ship, side))
		{
			bus.EmitLog($"{ship?.ShipName} 雷击未执行（无鱼雷管、状态不允许或本回合已雷击）");
			return false;
		}
		bool second = enemySide ? !IsPlayerSecondTurn : IsPlayerSecondTurn;
		int cost = CommandRulesEvaluator.TorpedoCPCost(ship, second);
		bool consumed = enemySide ? TryConsumeEnemyCP(cost) : TryConsumeCP(cost);
		if (!consumed)
		{
			bus.EmitLog($"{ship.ShipName} 雷击被拒绝（需要 {cost} CP）");
			return false;
		}

		int count = ship.ConsumeTorpedoSalvo(side);
		if (count <= 0)
		{
			bus.EmitLog($"{ship.ShipName} 该侧没有剩余鱼雷");
			return false;
		}
		int hitMode = enemySide
			? _dataManager?.TorpedoModeEnemy ?? 4
			: _dataManager?.TorpedoModePlayer ?? 7;
		TorpedoComponent torpedo = _torpedoController.SpawnTorpedo(
			$"t_{ship.ShipName}_{GD.Randi()}",
			(int)ship.BattleSide,
			ship.HexCoords,
			ship.Direction,
			ship.Data?.TorpedoSpeed ?? 6,
			ship.Data?.TorpedoRange ?? 4,
			count,
			hitMode,
			ship.Data?.TorpedoDamage ?? 30,
			ship.Data?.TorpedoType ?? "",
			ship,
			side,
			branch,
			_mapGenerator);
		if (torpedo != null)
		{
			bus.EmitLog($"💣 {ship.ShipName} 向{(side < 0 ? "左舷" : "右舷")}" +
				$"{(branch == 0 ? "·正" : "·斜")}发射 {count} 发鱼雷（{cost} CP）");
		}
		return torpedo != null;
	}
}
