
using TestUtils;
using Xunit;
using Xunit.Abstractions;

namespace GetchuMetadata.Test;

public class GetchuTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public GetchuTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async void ShouldGetSearchResult()
    {
        var scrapper = new Scrapper(new XunitLogger<Scrapper>(_testOutputHelper));
        var res = await scrapper.ScrapSearchPage("BALDRSKY ZERO");
        Assert.NotEmpty(res);
    }

    [Fact]
    public async void ShouldGetGameDetail()
    {
        var scrapper = new Scrapper(new XunitLogger<Scrapper>(_testOutputHelper));
        var res = await scrapper.ScrapGamePage("https://www.getchu.com/soft.phtml?id=748164");
        Assert.NotNull(res?.Title);
        Assert.NotEmpty(res?.ProductImages);
    }
}
