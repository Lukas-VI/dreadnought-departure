using Godot;

namespace DreadnoughtDeparture.Story;

/// <summary>剧情播放的全局开关，持久化到 user://story_settings.cfg。</summary>
public static class StorySettings
{
	public const string FilePath = "user://story_settings.cfg";

	public static bool WatchStory { get; private set; } = true;

	public static void Load()
	{
		var config = new ConfigFile();
		if (config.Load(FilePath) == Error.Ok)
		{
			WatchStory = config.GetValue("story", "watch", true).AsBool();
		}
	}

	public static void Save(bool watch)
	{
		WatchStory = watch;
		var config = new ConfigFile();
		config.SetValue("story", "watch", watch);
		config.Save(FilePath);
	}
}
