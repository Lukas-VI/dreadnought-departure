using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>双方指挥值、CP、上限与 PV 得分的单一状态源，变更时通知订阅者。</summary>
public sealed class BattleEconomyState
{
	public event Action Changed;

	private int _playerCommandValue = 5;
	private int _enemyCommandValue = 4;
	private int _playerMaxCP = 12;
	private int _enemyMaxCP = 12;
	private int _playerCurrentCP = 8;
	private int _enemyCurrentCP = 8;
	private int _playerScore;
	private int _enemyScore;

	public int PlayerCommandValue { get => _playerCommandValue; set { _playerCommandValue = value; Changed?.Invoke(); } }
	public int EnemyCommandValue { get => _enemyCommandValue; set { _enemyCommandValue = value; Changed?.Invoke(); } }
	public int MaxCP { get => _playerMaxCP; set { _playerMaxCP = value; Changed?.Invoke(); } }
	public int EnemyMaxCP { get => _enemyMaxCP; set { _enemyMaxCP = value; Changed?.Invoke(); } }
	public int CurrentCP { get => _playerCurrentCP; set { _playerCurrentCP = value; Changed?.Invoke(); } }
	public int EnemyCurrentCP { get => _enemyCurrentCP; set { _enemyCurrentCP = value; Changed?.Invoke(); } }
	public int PlayerScore { get => _playerScore; set { _playerScore = value; Changed?.Invoke(); } }
	public int EnemyScore { get => _enemyScore; set { _enemyScore = value; Changed?.Invoke(); } }

	public bool TryConsumePlayer(int amount)
	{
		if (CurrentCP < amount) return false;
		CurrentCP -= amount;
		return true;
	}

	public void AddPlayer(int amount)
		=> CurrentCP = Math.Min(CurrentCP + amount, MaxCP);

	public bool TryConsumeEnemy(int amount)
	{
		if (EnemyCurrentCP < amount) return false;
		EnemyCurrentCP -= amount;
		return true;
	}

	public void AddEnemy(int amount)
		=> EnemyCurrentCP = Math.Min(EnemyCurrentCP + amount, EnemyMaxCP);

	public void Refresh(
		IEnumerable<ShipComponent> playerShips,
		IEnumerable<ShipComponent> enemyShips,
		int basePlayerCommand,
		int baseEnemyCommand)
	{
		PlayerCommandValue = CommandRulesEvaluator.CommandValue(playerShips, basePlayerCommand);
		EnemyCommandValue = CommandRulesEvaluator.CommandValue(enemyShips, baseEnemyCommand);
		MaxCP = Math.Max(1, PlayerCommandValue * 2);
		EnemyMaxCP = Math.Max(1, EnemyCommandValue * 2);
		CurrentCP = Math.Min(CurrentCP, MaxCP);
		EnemyCurrentCP = Math.Min(EnemyCurrentCP, EnemyMaxCP);
		RefreshScores(playerShips, enemyShips);
	}

	public void RefreshScores(
		IEnumerable<ShipComponent> playerShips,
		IEnumerable<ShipComponent> enemyShips)
	{
		PlayerScore = VictoryRulesEvaluator.FleetScore(enemyShips);
		EnemyScore = VictoryRulesEvaluator.FleetScore(playerShips);
	}

	public void ApplyDeferredSpeedCaps(IEnumerable<ShipComponent> ships, Action<string> log)
	{
		foreach (ShipComponent ship in ships)
		{
			if (!Godot.GodotObject.IsInstanceValid(ship) || ship.CurrentHp <= 0) continue;
			int cap = ship.MaxSpeedForCurrentState;
			if (ship.CurrentSpeed > cap)
			{
				int old = ship.CurrentSpeed;
				ship.CurrentSpeed = cap;
				log?.Invoke($"{ship.ShipName} 因损伤强制降速 {old} → {cap}");
			}
		}
	}
}
