using CatalogAPI.Application.Contexts.Games.UseCases.Search;
using CatalogAPI.Application.Shared.Search;
using CatalogAPI.Domain.Contexts.Games.Enums;
using NSubstitute;

namespace CatalogAPI.Application.Tests.Games.UseCases.Search;

public class HandlerTests
{
    private readonly ISearchService _searchService = Substitute.For<ISearchService>();
    private readonly Handler _handler;

    public HandlerTests() => _handler = new Handler(_searchService);

    [Fact]
    public async Task Handle_WithValidRequest_ShouldReturnHitsInRelevanceOrder()
    {
        var hits = new List<GameSearchHit>
        {
            new(Guid.NewGuid(), "The Witcher 3", 99.9m, GameGenre.RPG, 7.5),
            new(Guid.NewGuid(), "Gwent", 0m, GameGenre.Strategy, 1.2),
        };
        _searchService.SearchAsync("witchr", 1, 20, Arg.Any<CancellationToken>())
            .Returns(new GameSearchResult(hits, 2));

        var response = await _handler.Handle(new Request("  witchr  ", 1, 20), CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("witchr", response.Data!.Query);
        Assert.Equal(2, response.Data.Total);
        Assert.Equal(hits, response.Data.Games);
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldReturnBadRequestWithoutSearching()
    {
        var response = await _handler.Handle(new Request("", 1, 20), CancellationToken.None);

        Assert.Equal(400, response.StatusCode);
        await _searchService.DidNotReceive().SearchAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
