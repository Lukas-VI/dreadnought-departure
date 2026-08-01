using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 船名覆盖层（Node2D）。
/// 在带生成点且有船初设的图块上，用 DrawString 渲染最多 3 行舰船名，
/// 水平/垂直居中于图块内部。阵营决定文字颜色（玩家蓝、敌方红）。
/// </summary>
public partial class ShipNameOverlay : Node2D
{
	[Export] public int FontSize = 13;
	[Export] public Color PlayerColor = new(0.25f, 0.55f, 1f, 1f);
	[Export] public Color EnemyColor = new(1f, 0.35f, 0.35f, 1f);

	private LevelDataManager _data;

	public override void _Ready()
	{
		_data = GetNodeOrNull<LevelDataManager>("../../LevelDataManager");
	}

	/// <summary>数据变化后调用，触发重新绘制。</summary>
	public void RedrawAll() => QueueRedraw();

	public override void _Draw()
	{
		if (_data == null) return;
		Font font = ThemeDB.FallbackFont;

		foreach (var kv in _data.ShipSpawns)
		{
			Vector2I hex = kv.Key;
			if (!_data.GenerationPoints.TryGetValue(hex, out var gen)) continue;
			if (kv.Value == null || kv.Value.Count == 0) continue;

			Vector2 center = HexMath.HexToLocal(_data.MapOrientation, hex, HexMath.EditorHexRadius);
			Color color = gen.Side == GenerationSide.Enemy ? EnemyColor : PlayerColor;

			int count = Mathf.Min(kv.Value.Count, 3);
			int lineHeight = FontSize + 3;
			float blockHeight = count * lineHeight;
			float startY = center.Y - blockHeight / 2f + FontSize;

			for (int i = 0; i < count; i++)
			{
				string name = ShipCatalog.Get(kv.Value[i].ShipId)?.DisplayName ?? kv.Value[i].ShipId;
				DrawString(font, new Vector2(center.X, startY + i * lineHeight),
					name, HorizontalAlignment.Center, HexMath.EditorHexRadius * 1.7f, FontSize, color);
			}
		}
	}
}
