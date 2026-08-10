using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DreadnoughtDeparture.Story;

/// <summary>剧情树节点：脚本、文件夹或索引条目。</summary>
public sealed class StoryNode
{
	public string Id = "";
	public string Title = "";
	public string Script = "";
	public string Event = "";
	public string Key = "";
	public List<StoryNode> Children = new();
}

/// <summary>
/// 剧情结构化索引：优先读取 Data/Stories/index.json 树形索引；
/// 没有索引时自动扫描 Data/Stories 下的目录与脚本文件。
/// </summary>
public sealed class NarrativeCatalog
{
	public const string RootPath = "res://Data/Stories";
	private const string IndexFileName = "index.json";

	public StoryNode Root { get; private set; } = new();

	public void Scan()
	{
		if (TryLoadIndex()) return;
		Root = new StoryNode { Title = "剧情" };
		ScanDirectory(RootPath, Root, "");
	}

	public StoryNode Find(string id)
	{
		foreach (StoryNode node in Flatten())
		{
			if (node.Id == id || (!string.IsNullOrEmpty(node.Script) && node.Script == id))
			{
				return node;
			}
		}
		return null;
	}

	public IEnumerable<StoryNode> Flatten()
		=> Flatten(Root);

	private static IEnumerable<StoryNode> Flatten(StoryNode node)
	{
		yield return node;
		foreach (StoryNode child in node.Children)
		{
			foreach (StoryNode descendant in Flatten(child))
			{
				yield return descendant;
			}
		}
	}

	private bool TryLoadIndex()
	{
		string path = $"{RootPath}/{IndexFileName}";
		if (!FileAccess.FileExists(path)) return false;
		try
		{
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			using var document = JsonDocument.Parse(file.GetAsText());
			Root = ParseNode(document.RootElement, "");
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static StoryNode ParseNode(JsonElement element, string prefix)
	{
		var node = new StoryNode
		{
			Title = Str(element, "title"),
			Script = Str(element, "script"),
			Event = Str(element, "event"),
			Key = Str(element, "key"),
		};
		string id = Str(element, "id");
		if (string.IsNullOrEmpty(id))
		{
			id = node.Script;
		}
		if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(id))
		{
			node.Id = $"{prefix}/{id}";
		}
		else
		{
			node.Id = id;
		}
		if (element.TryGetProperty("children", out JsonElement children)
			&& children.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement child in children.EnumerateArray())
			{
				node.Children.Add(ParseNode(child, node.Id));
			}
		}
		return node;
	}

	private static void ScanDirectory(string dirPath, StoryNode parent, string prefix)
	{
		using var dir = DirAccess.Open(dirPath);
		if (dir == null) return;
		foreach (string name in dir.GetDirectories())
		{
			string childPrefix = string.IsNullOrEmpty(prefix) ? name : $"{prefix}/{name}";
			var folderNode = new StoryNode { Id = childPrefix, Title = name };
			parent.Children.Add(folderNode);
			ScanDirectory($"{dirPath}/{name}", folderNode, childPrefix);
		}
		foreach (string file in dir.GetFiles())
		{
			if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
				|| file == IndexFileName
				|| file == "global.json")
			{
				continue;
			}
			string scriptId = file.Substring(0, file.Length - 5);
			if (!string.IsNullOrEmpty(prefix))
			{
				scriptId = $"{prefix}/{scriptId}";
			}
			parent.Children.Add(new StoryNode
			{
				Id = scriptId,
				Script = scriptId,
				Title = file.Substring(0, file.Length - 5),
			});
		}
	}

	private static string Str(JsonElement element, string property)
		=> element.TryGetProperty(property, out JsonElement value) ? value.GetString() ?? "" : "";
}
