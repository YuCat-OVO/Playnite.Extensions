using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Text;
using Microsoft.Extensions.Logging;

namespace FanzaMetadata;

public class FanzaGameScrapper : IScrapper
{
    private const string BaseSearchUrl = "https://dlsoft.dmm.co.jp/search?service=pcgame&searchstr=";
    private const string BaseGamePageUrl = "https://dlsoft.dmm.co.jp/detail/";
    private const string PosterUrlPattern = "https://pics.dmm.co.jp/digital/pcgame/{0}/{0}pl.jpg";

    private const string BaseMonoSearchUrl = "https://www.dmm.co.jp/search/=/searchstr={0}/limit=30/sort=rankprofile/";
    private const string BaseMonoGamePageUrl = "https://www.dmm.co.jp/mono/pcgame/-/detail/=/cid=";

    private readonly ILogger<FanzaGameScrapper> _logger;
    private readonly IConfiguration _configuration;

    public FanzaGameScrapper(ILogger<FanzaGameScrapper> logger)
    {
        _logger = logger;
        var clientHandler = new HttpClientHandler();
        clientHandler.Properties.Add("User-Agent", "Playnite.Extensions");
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Cookie("age_check_done", "1", "/", ".dmm.co.jp")
        {
            Expires = DateTime.Now + TimeSpan.FromDays(30),
            HttpOnly = true
        });

        clientHandler.CookieContainer = cookieContainer;
        _configuration = Configuration.Default.WithRequesters(clientHandler).WithDefaultLoader();
        clientHandler.UseCookies = true;
    }

    public FanzaGameScrapper(ILogger<FanzaGameScrapper> logger, HttpClientHandler messageHandler)
    {
        _logger = logger;
        _configuration = Configuration.Default.WithRequesters(messageHandler).WithDefaultLoader();
        messageHandler.UseCookies = true;
    }


    public async Task<List<SearchResult>> ScrapSearchPage(string searchName,
        CancellationToken cancellationToken = default)
    {
        var url = BaseSearchUrl + Uri.EscapeUriString(searchName);
        var context = BrowsingContext.New(_configuration);
        var document = await context.OpenAsync(url, cancellationToken);

        await Console.Out.WriteAsync(document.Title);

        var dlResults = document
            .QuerySelectorAll(".component-legacy-productTile .component-legacy-productTile__detailLink")
            .Where(element => !string.IsNullOrEmpty(element.HyperReference("")?.Href))
            .Select(element =>
                {
                    var title = element.GetElementsByClassName("component-legacy-productTile__title").First().Text();
                    var anchorElement = (IHtmlAnchorElement)element;
                    var href = anchorElement.Href;
                    var id = new Uri(href).Segments.Last().Replace("/", "");
                    return new SearchResult(title, id, href);
                }
            ).ToList();
        if (dlResults.Count > 0)
        {
            return dlResults;
        }

        var monoSearchUrl = string.Format(BaseMonoSearchUrl, searchName);
        context = BrowsingContext.New(_configuration);
        document = await context.OpenAsync(monoSearchUrl, cancellationToken);

        return document.QuerySelectorAll("#list li")
            .Select(element =>
            {
                var aElement = (IHtmlAnchorElement)element.QuerySelectorAll(".tmb a").First();
                var title = aElement.QuerySelectorAll("img").First().GetAttribute("alt") + "";

                var id = new Uri(aElement.Href).Segments
                    .Where(x => x.StartsWith("cid="))
                    .Select(x => x.ReplaceFirst("cid=", "").Replace("/", ""))
                    .First() + "";
                return new SearchResult(title, id, aElement.Href);
            }).ToList();
    }


    public async Task<ScrapperResult?> ScrapGamePage(SearchResult searchResult,
        CancellationToken cancellationToken = default)
    {
        return await ScrapGamePage(searchResult.Href, cancellationToken);
    }

    public async Task<ScrapperResult?> ScrapGamePage(string link, CancellationToken cancellationToken)
    {
        var id = GetGameIdFromLinks(new List<string> { link });
        if (string.IsNullOrEmpty(id)) return null;

        var context = BrowsingContext.New(_configuration);
        var document = await context.OpenAsync(link, cancellationToken);
        if (document.StatusCode == HttpStatusCode.NotFound) return null;

        var result = new ScrapperResult()
        {
            Link = link
        };
        if (link.StartsWith(BaseGamePageUrl, StringComparison.OrdinalIgnoreCase))
        {
            result.Title = document.GetElementById("title")?.Text().Trim();
            result.Circle = document.GetElementsByClassName("brand").Children(".content").First().Text().Trim();

            result.PreviewImages = document.GetElementsByClassName("image-slider").Children("li").Children("img")
                .Cast<IHtmlImageElement>().Select(x => x.Source ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();

            const string ratingPrefix = "d-rating-";
            result.Rating = document.QuerySelector(".review div")!.ClassList
                .Where(className => className.StartsWith(ratingPrefix))
                .Select(className => className.Replace(ratingPrefix, ""))
                .Select(rating => double.Parse(rating) / 10D).First();

            result.Description = document.QuerySelector(".read-text-area p.text-overflow")?.OuterHtml.Trim();

            var r18NavigationBarLength = document.GetElementsByClassName("_n4v1-link-r18-name").Length;
            result.Adult = r18NavigationBarLength <= 0;

            // <tr>
            //  <td class="type-left">ダウンロード版対応OS</td>
            //  <td class="type-center">：</td>
            //  <td class="type-right"></td>
            // </tr>
            // key is type-left class text, value is type-right class element
            var productDetailDict =
                document.QuerySelectorAll(".main-area-center .container02 table tbody tr .type-left")
                    .ToDictionary(ele => ele.Text().Trim(),
                        v => v.ParentElement?.GetElementsByClassName("type-right").First());

            var dateStr = productDetailDict["配信開始日"]?.Text().Trim();
            if (DateTime.TryParseExact(dateStr, "yyyy/MM/dd", null, DateTimeStyles.None, out var releaseDate))
            {
                result.ReleaseDate = releaseDate;
            }

            const string noneVal = "----";
            var gameGenre = productDetailDict["ゲームジャンル"]?.Text().Trim();
            if (!noneVal.Equals(gameGenre))
            {
                result.GameGenre = gameGenre;
            }

            var series = productDetailDict["シリーズ"]?.Text().Trim();
            if (!noneVal.Equals(series))
            {
                result.Series = series;
            }

            var tags = productDetailDict["ジャンル"]?.GetElementsByTagName("a").Select(x => x.Text().Trim()).ToList();
            result.Genres = tags;

            result.IconUrl = string.Format(PosterUrlPattern, id);
        }
        else
        {
            result.Title = document.GetElementById("title")?.Text().Trim();
            var productDetailDict = document.QuerySelectorAll("table.mg-b20 tbody tr")
                .ToDictionary(ele => ele.FirstElementChild?.Text().Replace("：", "").Trim(),
                    v => v.LastElementChild);

            result.Circle = productDetailDict["ブランド"]?.Text().Trim();

            result.PreviewImages = document.QuerySelectorAll("#sample-image-block a img")
                .Cast<IHtmlImageElement>()
                .Select(x => x.Source ?? "")
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();


            result.Description = document.QuerySelector("div.page-detail div.mg-b20.lh4 p.mg-b20")?.OuterHtml.Trim();

            var rating = productDetailDict["平均評価"]?.QuerySelectorAll("img")
                .Cast<IHtmlImageElement>()
                .Select(x => x.Source)
                .Where(x => x?.EndsWith(".gif") ?? false)
                .Select(x =>
                {
                    var str = x + "";
                    var idx = str.LastIndexOf("/", StringComparison.Ordinal);
                    if (idx < 0)
                    {
                        return -1D;
                    }

                    var dd = str.Substring(idx + 1).Replace(".gif", "");
                    return dd.ToDouble();
                }).First(x => x > 0) ?? 0D;
            result.Rating = rating / 10D;

            var r18NavigationBarLength = document.GetElementsByClassName("_n4v1-link-r18-name").Length;
            result.Adult = r18NavigationBarLength <= 0;

            var dateStr = productDetailDict["発売日"]?.Text().Trim();
            if (DateTime.TryParseExact(dateStr, "yyyy/MM/dd", null, DateTimeStyles.None, out var releaseDate))
            {
                result.ReleaseDate = releaseDate;
            }

            result.GameGenre = productDetailDict["ゲームジャンル"]?.Text().Trim();
            result.Series = productDetailDict["シリーズ"]?.Text().Trim();

            var tags = productDetailDict["関連タグ"]?.QuerySelectorAll("li a")
                .Select(x => x.Text().Replace("#", "").Trim()).ToList();
            result.Genres = tags;
            result.IconUrl = string.Format("https://pics.dmm.co.jp/mono/game/{0}/{0}pl.jpg", id);
        }
        return result;
    }

    public static string? ParseLinkId(string? link)
    {
        if (link == null) return null;
        return new Uri(link).Segments.Last().Replace("/", "").Replace("cid=", "");
    }

    public string? GetGameIdFromLinks(IEnumerable<string> links)
    {
        return links.Where(link =>
                link.StartsWith(BaseGamePageUrl, StringComparison.OrdinalIgnoreCase)
                || link.StartsWith(BaseMonoGamePageUrl, StringComparison.OrdinalIgnoreCase)
            )
            .Select(ParseLinkId).Where(x => !string.IsNullOrEmpty(x)).FirstOrDefault();
    }
}
