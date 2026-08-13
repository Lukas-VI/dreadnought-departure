namespace DreadnoughtDeparture.Core;

/// <summary>阶段流转机：集中处理跳过照明/炮击/鱼雷的下一步判定。</summary>
public static class BattlePhaseMachine
{
	public static readonly string[] PhaseLabels =
	{
		"1 速度", "2 ▶ 第一移动", "3 ▶▶ 第二移动", "4 ▶▶▶ 第三移动",
		"5 视野", "6 火炮", "7 鱼雷", "8 结算"
	};

	public sealed record Transition(
		BattlePhase Next,
		bool SkipLighting,
		bool SkipGunfire,
		bool SkipTorpedo);

	public static Transition Plan(
		BattlePhase current,
		bool isNight,
		bool gunfireEnabled,
		bool torpedoEnabled)
	{
		bool skipLighting = current == BattlePhase.MovePhase3 && !isNight;
		bool skipTorpedo = !torpedoEnabled
			&& current is BattlePhase.Gunfire or BattlePhase.MovePhase3 or BattlePhase.ReconLighting;
		bool skipGunfire = current is BattlePhase.MovePhase3 or BattlePhase.ReconLighting
			&& !gunfireEnabled;

		BattlePhase next = current switch
		{
			BattlePhase.SpeedAdjust => BattlePhase.MovePhase1,
			BattlePhase.MovePhase1 => BattlePhase.MovePhase2,
			BattlePhase.MovePhase2 => BattlePhase.MovePhase3,
			BattlePhase.MovePhase3 => skipLighting
				? (skipGunfire
					? (skipTorpedo ? BattlePhase.EndTurn : BattlePhase.Torpedo)
					: BattlePhase.Gunfire)
				: BattlePhase.ReconLighting,
			BattlePhase.ReconLighting => skipGunfire
				? (skipTorpedo ? BattlePhase.EndTurn : BattlePhase.Torpedo)
				: BattlePhase.Gunfire,
			BattlePhase.Gunfire => skipTorpedo ? BattlePhase.EndTurn : BattlePhase.Torpedo,
			BattlePhase.Torpedo => BattlePhase.EndTurn,
			BattlePhase.EndTurn => BattlePhase.SpeedAdjust,
			_ => BattlePhase.SpeedAdjust,
		};
		return new Transition(next, skipLighting, skipGunfire, skipTorpedo);
	}
}
