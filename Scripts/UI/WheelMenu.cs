using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

public partial class WheelMenu : Control
{
	[Signal] public delegate void ActionSelectedEventHandler(string actionId);

	[Export] public float Radius = 100f;
	[Export] public float ButtonSize = 48f;

	private static readonly string[] _baseActions = { "move", "attack", "turn_left", "turn_right" };

	public override void _Ready()
	{
		Hide();
	}

	public void Show(Vector2 screenPos, ShipComponent ship)
	{
		// 基础动作 + 船的专属技能
		var skills = ship.Data?.SkillIds ?? Array.Empty<string>();
		var actions = _baseActions.Concat(skills).ToList();
		int count = actions.Count;
		if (count == 0) return;

		// 清除旧按钮
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
			string actionId = actions[i]; // capture
			btn.Pressed += () => EmitSignal(SignalName.ActionSelected, actionId);
			AddChild(btn);
		}

		Position = screenPos - new Vector2(Radius + ButtonSize, Radius + ButtonSize) / 2;
		Visible = true;
	}

	public void HideMenu()
	{
		Visible = false;
	}

	private Vector2 btnPos(Vector2 o) => o - new Vector2(ButtonSize / 2, ButtonSize / 2);

	private static string LabelFor(string id) => id switch
	{
		"move" => "🚢", "attack" => "💥",
		"turn_left" => "↩", "turn_right" => "↪",
		_ => id.Length > 3 ? id[..3] : id
	};
}
