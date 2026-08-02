using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 底部弹性操作菜单（Control）。
/// 替代轮盘菜单：每个玩家阶段在屏幕底部居中弹出该阶段允许的操作卡片，
/// 卡片带 Back 缓动弹出动画，点击后发出 ActionSelected 信号。
/// </summary>
public partial class PhaseActionMenu : Control
{
	[Signal] public delegate void ActionSelectedEventHandler(string actionId);

	[Export] public float CardWidth = 150f;
	[Export] public float CardHeight = 58f;
	[Export] public float PopDuration = 0.35f;

	private HBoxContainer _row;

	public override void _Ready()
	{
		_row = GetNode<HBoxContainer>("CardRow");
		Hide();
	}

	/// <summary>按当前阶段为指定舰船构建允许的操作并弹出；无可用操作则隐藏。</summary>
	public void ShowFor(ShipComponent ship, BattlePhase phase)
	{
		var director = GetNodeOrNull<GameplayDirector>("../../..");
		var actions = AllowedActions(ship, phase);
		if (actions.Count == 0)
		{
			HideMenu();
			return;
		}

		ClearCards();
		var friendly = new List<ShipComponent>();
		foreach (Node node in GetTree().GetNodesInGroup("Ships"))
			if (node is ShipComponent s && GodotObject.IsInstanceValid(s)
				&& s.BattleSide == ship.BattleSide)
				friendly.Add(s);

		foreach (string id in actions)
		{
			int cost = ActionCost(ship, id);
			bool enabled = IsActionEnabled(ship, phase, id, director);
			bool highlighted = IsPendingAction(ship, id);
			string formationLabel = FormationEffectLabel(ship, friendly, id);
			bool threeLine = formationLabel.Length > 0;
			var btn = new Button
			{
				Text = $"{LabelFor(id)}\n{(cost > 0 ? $"-{cost} CP" : "无消耗")}"
					+ (threeLine ? $"\n{formationLabel}" : ""),
				CustomMinimumSize = new Vector2(CardWidth, CardHeight + (threeLine ? 24f : 12f)),
				Disabled = !enabled
			};
			if (highlighted)
			{
				btn.Position = new Vector2(0f, -4f);
				btn.Scale = new Vector2(1.06f, 1.06f);
				btn.Modulate = new Color(1f, 1f, 0.55f, 1f);
			}
			else if (!enabled)
			{
				btn.Position = new Vector2(0f, 3f);
				btn.Scale = new Vector2(0.98f, 0.98f);
				btn.Modulate = new Color(0.55f, 0.55f, 0.55f, 1f);
			}
			else if (formationLabel == "组成")
				btn.Modulate = new Color(0.45f, 1f, 0.6f, 1f);
			else if (formationLabel == "切断")
				btn.Modulate = new Color(1f, 0.6f, 0.42f, 1f);
			btn.Pressed += () => EmitSignal(SignalName.ActionSelected, id);
			_row.AddChild(btn);
		}

		Show();
		PlayPopAnimation();
	}

	/// <summary>隐藏菜单并清空操作卡片。</summary>
	public void HideMenu()
	{
		Visible = false;
		Scale = Vector2.One;
		Modulate = Colors.White;
		ClearCards();
	}

	/// <summary>每个阶段允许的操作：速度阶段可变速/转向，移动阶段仅转向，炮击阶段攻击+技能，全部附待命。</summary>
	private static List<string> AllowedActions(ShipComponent ship, BattlePhase phase)
	{
		var list = new List<string>();
		switch (phase)
		{
			case BattlePhase.SpeedAdjust:
				list.Add("speed_up");
				list.Add("speed_down");
				list.Add("turn_left");
				list.Add("turn_right");
				break;
			case BattlePhase.MovePhase1:
			case BattlePhase.MovePhase2:
			case BattlePhase.MovePhase3:
				list.Add("turn_left");
				list.Add("turn_right");
				break;
			case BattlePhase.Gunfire:
				list.Add("attack");
				if (ship.Data?.SkillIds != null)
					list.AddRange(ship.Data.SkillIds);
				break;
		}
		list.Add("skip");
		return list;
	}

	private static int ActionCost(ShipComponent ship, string id) => id switch
	{
		"speed_up" or "speed_down" or "turn_left" or "turn_right" => 1,
		"attack" => CommandRulesEvaluator.FireCPCost(ship),
		"radar" => 0,
		_ => 0
	};

	/// <summary>预测变速/转向后是否与附近舰船组成或切断单纵阵。</summary>
	private static string FormationEffectLabel(
		ShipComponent ship, List<ShipComponent> friendly, string id)
	{
		int? speedOverride = null;
		HexDirection? directionOverride = null;
		switch (id)
		{
			case "speed_up":
				speedOverride = ship.CurrentSpeed + 1;
				break;
			case "speed_down":
				speedOverride = ship.CurrentSpeed - 1;
				break;
			case "turn_left":
				directionOverride = HexDirectionUtility.TurnLeft(ship.Direction);
				break;
			case "turn_right":
				directionOverride = HexDirectionUtility.TurnRight(ship.Direction);
				break;
			default:
				return "";
		}

		// 运行时标记优先：贪吃蛇跟随途中首舰仍视为编队操作，不显示“切断”。
		if (MoveRulesEvaluator.IsRuntimeFormationLead(ship, friendly)) return "";
		var currentFormation = MoveRulesEvaluator.DetectLineAhead(ship, friendly);
		bool current = currentFormation.IsInFormation;
		if (current && ReferenceEquals(currentFormation.LeadShip, ship)) return "";
		bool predicted = MoveRulesEvaluator.DetectLineAhead(
			ship, friendly, directionOverride, speedOverride).IsInFormation;
		if (!current && predicted) return "组成";
		if (current && !predicted) return "切断";
		return "";
	}

	/// <summary>雷达技能可用条件：配表有雷达且未到中破（D1 中破起禁用雷达）。</summary>
	private static bool CanUseRadar(ShipComponent ship)
		=> !string.IsNullOrEmpty(ship.Data?.RadarType)
		&& ship.DamageState is DamageState.Intact or DamageState.Light;

	private static bool IsActionEnabled(ShipComponent ship, BattlePhase phase, string id,
		GameplayDirector director)
	{
		if (id == "skip") return true;
		int cp = director?.CurrentCP ?? 0;
		int cost = ActionCost(ship, id);
		if (cp < cost) return false;
		return id switch
		{
			"speed_up" => phase == BattlePhase.SpeedAdjust
				&& SpeedTable.CanAdjustSpeed(ship.CurrentSpeed, ship.CurrentSpeed + 1, ship.MaxSpeedForCurrentState),
			"speed_down" => phase == BattlePhase.SpeedAdjust
				&& SpeedTable.CanAdjustSpeed(ship.CurrentSpeed, ship.CurrentSpeed - 1, ship.MaxSpeedForCurrentState),
			"turn_left" or "turn_right" => phase is BattlePhase.SpeedAdjust
				or BattlePhase.MovePhase1 or BattlePhase.MovePhase2 or BattlePhase.MovePhase3,
			"attack" => phase == BattlePhase.Gunfire && ship.MainAmmo > 0
				&& ship.DamageState != DamageState.Heavy && ship.DamageState != DamageState.Sunk,
			"radar" => phase == BattlePhase.Gunfire && CanUseRadar(ship),
			_ => true
		};
	}

	private static bool IsPendingAction(ShipComponent ship, string id) => id switch
	{
		"speed_up" => ship.PendingSpeed > ship.CurrentSpeed,
		"speed_down" => ship.PendingSpeed >= 0 && ship.PendingSpeed < ship.CurrentSpeed,
		"turn_left" => ship.PendingDirection == HexDirectionUtility.TurnLeft(ship.Direction),
		"turn_right" => ship.PendingDirection == HexDirectionUtility.TurnRight(ship.Direction),
		"attack" => ship.PendingAttackTarget != null,
		"radar" => ship.PendingRadarActive,
		_ => false
	};

	/// <summary>整张菜单从底部中心弹入：缩放 + 透明度 Back 缓动。</summary>
	private void PlayPopAnimation()
	{
		PivotOffset = Size * 0.5f;
		Modulate = new Color(1f, 1f, 1f, 0f);
		Scale = new Vector2(0.7f, 0.7f);
		var tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Back);
		tween.SetEase(Tween.EaseType.Out);
		tween.Parallel().TweenProperty(this, "modulate:a", 1f, PopDuration);
		tween.Parallel().TweenProperty(this, "scale", Vector2.One, PopDuration);
	}

	private void ClearCards()
	{
		foreach (Node child in _row.GetChildren())
			if (child is Button btn)
			{
				_row.RemoveChild(btn);
				btn.QueueFree();
			}
	}

	private static string LabelFor(string id) => id switch
	{
		"speed_up" => "加速",
		"speed_down" => "减速",
		"turn_left" => "左转",
		"turn_right" => "右转",
		"attack" => "炮击",
		"radar" => "雷达",
		"skip" => "待命",
		_ => id
	};
}
