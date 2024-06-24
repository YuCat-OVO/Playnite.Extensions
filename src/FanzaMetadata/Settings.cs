using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Extensions.Common;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace FanzaMetadata;

public class Settings : ISettings
{
    private IPlayniteAPI? _playniteAPI;
    private Plugin? _plugin;

    public PlayniteProperty GenreProperty { get; set; } = PlayniteProperty.Tags;
    public PlayniteProperty GameGenreProperty { get; set; } = PlayniteProperty.Genres;

    public List<Regex> TagFilter { get; set; } = new()
    {
        new Regex(@"Windows\d+"),
        new Regex("還元"),
        new Regex("独占販売"),
        new Regex("初回購入者"),
        new Regex("スプリングセール"),
        new Regex("体験版"),
    };

    public Settings()
    {

    }

    public Settings(Plugin plugin, IPlayniteAPI playniteAPI)
    {
        _plugin = plugin;
        _playniteAPI = playniteAPI;

        var savedSettings = plugin.LoadPluginSettings<Settings>();
        if (savedSettings is not null)
        {
            GenreProperty = savedSettings.GenreProperty;
            GameGenreProperty = savedSettings.GameGenreProperty;
            TagFilter = savedSettings.TagFilter;
        }
    }

    private Settings? _previousSettings;

    public void BeginEdit()
    {
        _previousSettings = new Settings
        {
            GenreProperty = GenreProperty,
            GameGenreProperty = GameGenreProperty,
            TagFilter = TagFilter
        };
    }

    public void EndEdit()
    {
        _previousSettings = null;
        _plugin?.SavePluginSettings(this);
    }

    public void CancelEdit()
    {
        if (_previousSettings is null) return;

        GenreProperty = _previousSettings.GenreProperty;
        GameGenreProperty = _previousSettings.GameGenreProperty;
        TagFilter = _previousSettings.TagFilter;
    }

    public bool VerifySettings(out List<string> errors)
    {
        errors = new List<string>();

        if (!Enum.IsDefined(typeof(PlayniteProperty), GenreProperty))
        {
            errors.Add($"Unknown value \"{GenreProperty}\"");
        }

        if (!Enum.IsDefined(typeof(PlayniteProperty), GameGenreProperty))
        {
            errors.Add($"Unknown value \"{GameGenreProperty}\"");
        }

        return !errors.Any();
    }
}
