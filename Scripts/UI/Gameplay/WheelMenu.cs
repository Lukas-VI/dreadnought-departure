using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 舰船操作轮盘菜单（Control）。
/// 选中舰船后显示一个圆形按钮菜单，包含基础指令（移动/攻击/左右转/加减速）
/// 以及舰船技能。按钮数量根据可用操作动态生成，等角分布。
/// </summary>
public partial class WheelMenu : Control
{
	[Signal] public delegate void ActionSelectedEventHandler(string actionId);

	[Export] public float Radius = 100f;
	[Export] public float ButtonSize = 48f;

	private static readonly string[] _baseActions = { "move", "attack", "turn_left", "turn_right", "speed_up", "speed_down" };

	public override void _Ready()
	{
		Hide();
	}

	/// <summary>在屏幕坐标 screenPos 处显示轮盘菜单，包含基础操作与舰船技能。</summary>
	public void Show(Vector2 screenPos, ShipComponent ship)
	{
		var skills = ship.Data?.SkillIds ?? Array.Empty<string>();
		var actions = _baseActions.Concat(skills).ToList();
		int count = actions.Count;
		if (count == 0) return;

		foreach (var child in GetChildren())
			if (child is Button b) b.QueueFree();

		for (int i = 0; i < count; i++)
		{
			float angle = -Mathf.Pi / 2 + Mathf.Pi * 2 * i / count;
			Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius;

			Button btn = new();
			btn.Text = LabelFor(actions[i]);
			btn.Size = new Vector2(ButtonSize, ButtonSize);
			btn.Position = btnPos(offset);
			string actionId = actions[i];
			btn.Pressed += () => EmitSignal(SignalName.ActionSelected, actionId);
			AddChild(btn);
		}

		Position = screenPos;
		Visible = true;
	}

	/// <summary>隐藏轮盘菜单。</summary>
	public void HideMenu() { Visible = false; }

	private Vector2 btnPos(Vector2 o) => o - new Vector2(ButtonSize / 2, ButtonSize / 2);

	private static string LabelFor(string id) => id switch
	{
		"move" => "🚢", "attack" => "💥",
		"turn_left" => "左", "turn_right" => "右",
		"speed_up" => "加", "speed_down" => "减",
		_ => id.Length > 3 ? id[..3] : id
	};
}
