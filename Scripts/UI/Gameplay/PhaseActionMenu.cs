using Godot;
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
		var actions = AllowedActions(ship, phase);
		if (actions.Count == 0)
		{
			HideMenu();
			return;
		}

		ClearCards();
		foreach (string id in actions)
		{
			var btn = new Button
			{
				Text = LabelFor(id),
				CustomMinimumSize = new Vector2(CardWidth, CardHeight)
			};
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
				if (ship.MainAmmo > 0 && ship.DamageState != DamageState.Heavy
					&& ship.DamageState != DamageState.Sunk)
					list.Add("attack");
				if (ship.Data?.SkillIds != null)
					list.AddRange(ship.Data.SkillIds);
				break;
		}
		list.Add("skip");
		return list;
	}

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
		"skip" => "待命",
		_ => id
	};
}
