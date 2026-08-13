namespace DreadnoughtDeparture.Core;

/// <summary>胜负判定服务：把“自定义条件/全灭/回合上限”收敛成纯决策。</summary>
public static class VictoryJudge
{
	public sealed record Verdict(
		VictoryRulesEvaluator.VictoryResult Outcome,
		string Result,
		string Detail);

	public static Verdict Judge(
		VictoryRulesEvaluator.VictoryResult customResult,
		bool customConfigured,
		int playerAlive,
		int enemyAlive,
		int turn,
		int maxTurns,
		int playerScore,
		int enemyScore,
		bool atEndTurn)
	{
		if (customResult != VictoryRulesEvaluator.VictoryResult.None)
		{
			string customText = customResult switch
			{
				VictoryRulesEvaluator.VictoryResult.PlayerWin => "胜利",
				VictoryRulesEvaluator.VictoryResult.EnemyWin => "失败",
				_ => "平局",
			};
			return new Verdict(customResult, customText, $"关卡目标达成判定：{customText}");
		}

		if (customConfigured)
		{
			return new Verdict(VictoryRulesEvaluator.VictoryResult.None, "", "");
		}

		if (playerAlive > 0 && enemyAlive > 0)
		{
			if (atEndTurn && turn >= maxTurns)
			{
				string scoreResult = playerScore > enemyScore
					? "胜利"
					: playerScore < enemyScore
						? "失败"
						: "平局";
				VictoryRulesEvaluator.VictoryResult outcome = scoreResult switch
				{
					"胜利" => VictoryRulesEvaluator.VictoryResult.PlayerWin,
					"失败" => VictoryRulesEvaluator.VictoryResult.EnemyWin,
					_ => VictoryRulesEvaluator.VictoryResult.Draw,
				};
				return new Verdict(outcome, scoreResult,
					$"回合数已到，PV 我方 {playerScore} / 敌方 {enemyScore}");
			}
			return new Verdict(VictoryRulesEvaluator.VictoryResult.None, "", "");
		}

		bool playerWon = playerAlive > 0;
		string result = playerWon ? "胜利" : "失败";
		string detail = playerWon
			? $"敌方舰队已全灭（PV 我方 {playerScore} / 敌方 {enemyScore}）"
			: $"我方舰队已全灭（PV 我方 {playerScore} / 敌方 {enemyScore}）";
		return new Verdict(
			playerWon
				? VictoryRulesEvaluator.VictoryResult.PlayerWin
				: VictoryRulesEvaluator.VictoryResult.EnemyWin,
			result,
			detail);
	}
}
