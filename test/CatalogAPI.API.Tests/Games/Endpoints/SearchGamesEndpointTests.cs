using System.Net;
using CatalogAPI.API.Tests.Fixtures;

namespace CatalogAPI.API.Tests.Games.Endpoints;

// A ApiFactory sobe a API com a busca desligada (Search:Enabled ausente): o catálogo
// funciona normalmente e só o /search responde 503. O comportamento da busca em si
// (fuzzy, relevância) é testado contra um OpenSearch real em
// CatalogAPI.Infrastructure.Tests/Search/OpenSearchServiceTests.
[Collection("Api")]
public class SearchGamesEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateApiClient();

    [Fact]
    public async Task Get_WithoutTermAndFilters_ShouldReturn400()
    {
        var response = await _client.GetAsync("/api/v1/games/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_WhenSearchIsDisabled_ShouldReturn503()
    {
        var response = await _client.GetAsync("/api/v1/games/search?term=witcher");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Get_WhenSearchIsDisabled_GameListingShouldStillWork()
    {
        var response = await _client.GetAsync("/api/v1/games");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
