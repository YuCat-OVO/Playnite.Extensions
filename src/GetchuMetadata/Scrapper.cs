using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.Logging;

namespace GetchuMetadata;

public class Scrapper
{
    public const string DefaultLanguage = "en_US";

    public const string SiteBaseUrl = "https://getchu.com/";
    public const string ProductBaseUrl = SiteBaseUrl + "soft.phtml?id=";

    public const string SearchFormatUrl = SiteBaseUrl +
                                          "php/nsearch.phtml?search_keyword={0}&list_count={1}&sort=sales&sort2=down&genre=pc_soft&list_type=list&search=search";

    private static readonly Regex _imageLinkRegex =
        new Regex(@"(https?://[a-zA-Z0-9\.\?/%-_]*).(jpg|jpeg|png)", RegexOptions.Compiled);

    private readonly ILogger<Scrapper> _logger;
    private readonly IConfiguration _configuration;

    public Scrapper(ILogger<Scrapper> logger, HttpMessageHandler messageHandler)
    {
        _logger = logger;

        _configuration = Configuration.Default
            .WithRequesters(messageHandler)
            .WithDefaultLoader();
    }

    public Scrapper(ILogger<Scrapper> logger)
    {
        _logger = logger;
        var clientHandler = new HttpClientHandler();
        clientHandler.Properties.Add("User-Agent", "Playnite.Extensions");
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Cookie("getchu_adalt_flag", "getchu.com", "/", "www.getchu.com")
        {
            Expires = DateTime.Now + TimeSpan.FromDays(30),
            HttpOnly = true
        });

        clientHandler.CookieContainer = cookieContainer;
        _configuration = Configuration.Default.WithRequesters(clientHandler).WithDefaultLoader();
        clientHandler.UseCookies = true;
    }

    public async Task<ScrapperResult?> ScrapGamePage(string url, CancellationToken cancellationToken = default,
        string language = DefaultLanguage)
    {
        var uri = new Uri(url);
        var id = uri.Query.Replace("?id=", "");
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var res = new ScrapperResult
        {
            Link = url
        };


        var context = BrowsingContext.New(_configuration);
        var document = await context.OpenAsync(url, cancellationToken);
        if (document.StatusCode == HttpStatusCode.NotFound) return null;

        // Title
        var titleElement = document.GetElementById("soft-title");
        res.Title = titleElement?.FirstChild?.Text().Trim();

        var productDetailDir= document.QuerySelectorAll("#soft_table tbody tr:nth-child(2) tr td:nth-child(1)")
            .ToDictionary(x => x.Text().Replace("：", "").Trim(),
                ele => ele.NextElementSibling?.NextElementSibling ?? ele.NextElementSibling
            );

        var brandName = "ブランド";
        if (productDetailDir.ContainsKey(brandName))
        {
            res.Maker = productDetailDir[brandName]?.FirstElementChild?.Text();
        }

        // https://www.getchu.com/brandnew/792201/c792201package.jpg
        res.Cover = string.Format("https://www.getchu.com/brandnew/{0}/c{0}package.jpg", id);

        var dataName = "発売日";
        if (productDetailDir.ContainsKey(dataName))
        {
            if (DateTime.TryParseExact(productDetailDir[dataName]?.FirstElementChild?.Text(), "yyyy/MM/dd", null, DateTimeStyles.None,
                    out var releaseDate))
            {
                res.DateReleased = releaseDate;
            }
        }

        res.Illustrators = new List<string>();
        var pName = "原画";
        if (productDetailDir.ContainsKey(pName))
        {
            var str = productDetailDir[pName]?.Text();
            if (str != null)
            {
                str = str.Trim();
                res.Illustrators.AddRange(str.Split('、'));
            }
        }

        var idkName = "シナリオ";
        if (productDetailDir.ContainsKey(idkName))
        {
            var str = productDetailDir[idkName]?.Text();
            if (str != null)
            {
                str = str.Trim();
                res.Illustrators.AddRange(str.Split('、'));
            }
        }

        var idk2Name = "アーティスト";
        if (productDetailDir.ContainsKey(idk2Name))
        {
            var str = productDetailDir[idk2Name]?.Text();
            if (str != null)
            {
                str = str.Trim();
                res.Illustrators.AddRange(str.Split('、'));
            }
        }

        //
        var categoryName = "ジャンル";
        if (productDetailDir.ContainsKey(categoryName))
        {
            var str = productDetailDir[categoryName]?.FirstChild?.Text();
            if (str != null)
            {
                str = str.Trim();
                res.Categories = new List<string>(
                    str.Split('、')
                );
            }
        }


        var genresName = "カテゴリ";
        if (productDetailDir.ContainsKey(genresName))
        {
            var str = productDetailDir[genresName]?.FirstChild?.Text();
            if (str != null)
            {
                str = str.Trim();
                res.Genres = new List<string>(
                    str.Split('、')
                );
            }
        }

        res.ProductImages = document.QuerySelectorAll(".tabletitle")
            .Where(ele => ele.Text().Contains("サンプル画像"))
            .SelectMany(ele => ele.NextElementSibling?.Children)
            .Select(x => x as IHtmlAnchorElement)
            .Where(x => x != null)
            .Cast<IHtmlAnchorElement>()
            .Select(ele => ele.Href)
            .ToList();

        // res.DescriptionHtml = descriptionHtml;
        // res.SeriesNames = dataElement.Text().CustomTrim();
        // res.ScenarioWriters
        // res.VoiceActors
        // res.MusicCreators
        return res;
    }

    public async Task<List<SearchResult>> ScrapSearchPage(string term, CancellationToken cancellationToken = default,
        int maxResults = 50, string language = DefaultLanguage)
    {
        var context = BrowsingContext.New(_configuration);

        var url = string.Format(SearchFormatUrl, term, maxResults);
        var document = await context.OpenAsync(url, cancellationToken);


        var result = document.QuerySelectorAll(".display li #detail_block tr:first-child .blueb")
            .Cast<IHtmlAnchorElement>()
            .Select(ele =>
            {
                var searchResult = new SearchResult(ele.Text(), ele.Href);
                return searchResult;
            }).ToList();
        return result;
    }
}
