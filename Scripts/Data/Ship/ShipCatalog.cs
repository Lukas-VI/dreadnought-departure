using Godot;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 舰船目录扫描器——运行时从 res://Ships/*/ 目录检索 ShipData（*_data.tres）与对应预制体（同名 .tscn）。
/// ShipId 使用目录名小写（如 "dreadnought" / "frigate"），是编辑器与生成器共用的全局 ID。
/// 结果静态缓存，供编辑器检查器、UnitSpawner 等模块直接查询。
/// </summary>
public static class ShipCatalog
{
	/// <summary>单条舰船目录项：全局 ID、显示名、数据资源、场景预制体。</summary>
	public sealed class Entry
	{
		public string ShipId { get; init; } = "";
		public string DisplayName { get; init; } = "";
		public ShipData Data { get; init; }
		public PackedScene Scene { get; init; }
	}

	private static List<Entry> _entries;
	private static Dictionary<string, Entry> _byId;

	/// <summary>返回全部舰船条目；目录为空时返回空列表。</summary>
	public static IReadOnlyList<Entry> Entries
	{
		get
		{
			EnsureScanned();
			return _entries;
		}
	}

	/// <summary>按全局 ID 查找条目，找不到返回 null。</summary>
	public static Entry Get(string shipId)
	{
		EnsureScanned();
		if (string.IsNullOrEmpty(shipId)) return null;
		return _byId.GetValueOrDefault(shipId.ToLowerInvariant());
	}

	/// <summary>按全局 ID 获取预制体，找不到返回 null。</summary>
	public static PackedScene GetScene(string shipId) => Get(shipId)?.Scene;

	/// <summary>返回所有可用的 ShipId 列表（供下拉框使用）。</summary>
	public static IReadOnlyList<string> ShipIds => Entries.Select(e => e.ShipId).ToList();

	private static void EnsureScanned()
	{
		if (_entries != null) return;
		_entries = new List<Entry>();
		_byId = new Dictionary<string, Entry>();

		DirAccess shipsDir = DirAccess.Open("res://Ships");
		if (shipsDir == null) return;

		foreach (string folder in shipsDir.GetDirectories())
		{
			string dirPath = $"res://Ships/{folder}";
			DirAccess subDir = DirAccess.Open(dirPath);
			if (subDir == null) continue;

			string dataPath = null;
			string scenePath = null;
			foreach (string file in subDir.GetFiles())
			{
				if (file.EndsWith("_data.tres")) dataPath = $"{dirPath}/{file}";
				else if (file.EndsWith(".tscn")) scenePath = $"{dirPath}/{file}";
			}
			if (dataPath == null) continue;

			ShipData data = ResourceLoader.Load<ShipData>(dataPath);
			PackedScene scene = scenePath != null ? ResourceLoader.Load<PackedScene>(scenePath) : null;
			string shipId = folder.ToLowerInvariant();
			var entry = new Entry
			{
				ShipId = shipId,
				DisplayName = data?.ShipName ?? folder,
				Data = data,
				Scene = scene
			};
			_entries.Add(entry);
			_byId[shipId] = entry;
		}
	}
}
