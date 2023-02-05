using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Extensions.Common;
using Microsoft.Extensions.Logging;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace GetchuMetadata;

public class GetchuMetadataProvider : OnDemandMetadataProvider
{
    private readonly IPlayniteAPI _playniteAPI;
    private readonly Settings _settings;
    private readonly ILogger<GetchuMetadataProvider> _logger;

    private readonly MetadataRequestOptions _options;
    private Game Game => _options.GameData;
    private bool IsBackgroundDownload => _options.IsBackgroundDownload;

    public override List<MetadataField> AvailableFields => GetchuMetadataPlugin.Fields;

    public GetchuMetadataProvider(IPlayniteAPI playniteAPI, Settings settings, MetadataRequestOptions options)
    {
        _playniteAPI = playniteAPI;
        _settings = settings;
        _options = options;

        _logger = CustomLogger.GetLogger<GetchuMetadataProvider>(nameof(GetchuMetadataProvider));
    }

    private ScrapperResult? _result;
    private bool _didRun;

    private static string? GetLinkFromGame(Game game)
    {
        if (game.Name is not null)
        {
            if (game.Name.StartsWith(Scrapper.SiteBaseUrl)) return game.Name;
        }

        var getchuSiteLink =
            game.Links?.FirstOrDefault(link => link.Name.Equals("Getchu", StringComparison.OrdinalIgnoreCase));
        return getchuSiteLink?.Url;
    }

    private ScrapperResult? GetResult(GetMetadataFieldArgs args)
    {
        if (_didRun) return _result;

        var scrapper = new Scrapper(CustomLogger.GetLogger<Scrapper>(nameof(Scrapper)), new HttpClientHandler());

        var link = GetLinkFromGame(Game);
        if (link is null)
        {
            if (IsBackgroundDownload)
            {
                // background download so we just choose the first item

                var searchTask = scrapper.ScrapSearchPage(Game.Name, args.CancelToken, _settings.MaxSearchResults,
                    _settings.PreferredLanguage ?? Scrapper.DefaultLanguage);
                searchTask.Wait(args.CancelToken);

                var searchResult = searchTask.Result;
                if (searchResult is null || !searchResult.Any())
                {
                    _logger.LogError("Search return nothing for {Name}", Game.Name);
                    _didRun = true;
                    return null;
                }

                link = searchResult.First().Href;
            }
            else
            {
                var item = _playniteAPI.Dialogs.ChooseItemWithSearch(
                    new List<GenericItemOption>(),
                    searchString =>
                    {
                        var searchTask = scrapper.ScrapSearchPage(searchString, args.CancelToken,
                            _settings.MaxSearchResults, _settings.PreferredLanguage ?? Scrapper.DefaultLanguage);
                        searchTask.Wait(args.CancelToken);

                        var searchResult = searchTask.Result;
                        if (searchResult is null || !searchResult.Any())
                        {
                            _logger.LogError("Search return nothing for {Name}", searchString);
                            _didRun = true;
                            return null;
                        }

                        var items = searchResult
                            .Select(x => new GenericItemOption(x.Title, x.Href))
                            .ToList();

                        return items;
                    }, Game.Name, "Search Getchu");

                if (item is null)
                {
                    _didRun = true;
                    return null;
                }

                link = item.Description;
            }
        }

        if (link is null)
        {
            _didRun = true;
            return null;
        }

        var task = scrapper.ScrapGamePage(link, args.CancelToken,
            _settings.PreferredLanguage ?? Scrapper.DefaultLanguage);
        task.Wait(args.CancelToken);
        _result = task.Result;
        _didRun = true;

        return _result;
    }

    public override string GetName(GetMetadataFieldArgs args)
    {
        return GetResult(args)?.Title ?? base.GetName(args);
    }

    public override IEnumerable<MetadataProperty> GetDevelopers(GetMetadataFieldArgs args)
    {
        var result = GetResult(args);
        if (result is null) return base.GetDevelopers(args);

        var staff = new List<string>();
        if (result.Maker is not null)
        {
            staff.Add(result.Maker);
        }

        if (result.Illustrators is not null && _settings.IncludeIllustrators)
        {
            staff.AddRange(result.Illustrators);
        }

        if (result.MusicCreators is not null && _settings.IncludeMusicCreators)
        {
            staff.AddRange(result.MusicCreators);
        }

        if (result.ScenarioWriters is not null && _settings.IncludeScenarioWriters)
        {
            staff.AddRange(result.ScenarioWriters);
        }

        if (result.VoiceActors is not null && _settings.IncludeVoiceActors)
        {
            staff.AddRange(result.VoiceActors);
        }

        var developers = staff
            .Select(name => (name,
                _playniteAPI.Database.Companies.Where(x => x.Name is not null).FirstOrDefault(company =>
                    company.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
            .Select(tuple =>
            {
                var (name, company) = tuple;
                if (company is not null) return (MetadataProperty)new MetadataIdProperty(company.Id);
                return new MetadataNameProperty(name);
            })
            .ToList();

        return developers;
    }

    public override IEnumerable<Link> GetLinks(GetMetadataFieldArgs args)
    {
        var link = GetResult(args)?.Link;
        if (link is null) yield break;

        yield return new Link("Getchu", link);
    }

    private MetadataFile? SelectImage(GetMetadataFieldArgs args, string caption)
    {
        var images = GetResult(args)?.ProductImages;
        if (images is null || !images.Any()) return null;

        if (IsBackgroundDownload)
        {
            return new MetadataFile(images.First());
        }

        var imageFileOption =
            _playniteAPI.Dialogs.ChooseImageFile(images.Select(image => new ImageFileOption(image)).ToList(), caption);
        return imageFileOption == null ? null : new MetadataFile(imageFileOption.Path);
    }

    public override MetadataFile? GetCoverImage(GetMetadataFieldArgs args)
    {
        var icon = GetResult(args)?.Cover;
        return icon is null ? base.GetIcon(args) : new MetadataFile(icon);
    }

    public override MetadataFile? GetBackgroundImage(GetMetadataFieldArgs args)
    {
        return SelectImage(args, "Select Background Image");
    }

    public override ReleaseDate? GetReleaseDate(GetMetadataFieldArgs args)
    {
        var result = GetResult(args);
        if (result is null) return base.GetReleaseDate(args);

        var releaseDate = result.DateReleased;
        return releaseDate.Equals(DateTime.MinValue) ? base.GetReleaseDate(args) : new ReleaseDate(releaseDate);
    }

    private IEnumerable<MetadataProperty>? GetProperties(GetMetadataFieldArgs args, PlayniteProperty currentProperty)
    {
        // Categories
        var categoryProperties = PlaynitePropertyHelper.ConvertValuesIfPossible(
            _playniteAPI,
            _settings.CategoryProperty,
            currentProperty,
            () => GetResult(args)?.Categories);

        // Genres
        var genreProperties = PlaynitePropertyHelper.ConvertValuesIfPossible(
            _playniteAPI,
            _settings.GenreProperty,
            currentProperty,
            () => GetResult(args)?.Genres);

        return PlaynitePropertyHelper.MultiConcat(categoryProperties, genreProperties);
    }

    public override IEnumerable<MetadataProperty> GetTags(GetMetadataFieldArgs args)
    {
        return GetProperties(args, PlayniteProperty.Tags) ?? base.GetTags(args);
    }

    public override IEnumerable<MetadataProperty> GetFeatures(GetMetadataFieldArgs args)
    {
        return GetProperties(args, PlayniteProperty.Features) ?? base.GetFeatures(args);
    }

    public override IEnumerable<MetadataProperty> GetGenres(GetMetadataFieldArgs args)
    {
        return GetProperties(args, PlayniteProperty.Genres) ?? base.GetGenres(args);
    }

    public override IEnumerable<MetadataProperty> GetSeries(GetMetadataFieldArgs args)
    {
        var result = GetResult(args);
        if (result?.SeriesNames is null) return base.GetSeries(args);

        var series = _playniteAPI.Database.Series
            .Where(x => x.Name is not null)
            .FirstOrDefault(series => series.Name.Equals(result.SeriesNames));

        var property = series is null
            ? (MetadataProperty)new MetadataNameProperty(result.SeriesNames)
            : new MetadataIdProperty(series.Id);

        return new[] { property };
    }

    public override IEnumerable<MetadataProperty> GetPublishers(GetMetadataFieldArgs args)
    {
        return new[] { new MetadataNameProperty("Getchu") };
    }

    public override string GetDescription(GetMetadataFieldArgs args)
    {
        var result = GetResult(args);
        if (result is null) return base.GetDescription(args);

        return result.DescriptionHtml ?? "";
    }

    public override IEnumerable<MetadataProperty> GetRegions(GetMetadataFieldArgs args)
    {
        return new[] { new MetadataNameProperty("Japan") };
    }
}
