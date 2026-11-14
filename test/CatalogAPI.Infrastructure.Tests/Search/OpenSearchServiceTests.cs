using CatalogAPI.Application.Shared.Search;
using CatalogAPI.Domain.Contexts.Games.Enums;
using CatalogAPI.Infrastructure.Search;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSearch.Client;
using Testcontainers.OpenSearch;

namespace CatalogAPI.Infrastructure.Tests.Search;

// Um container OpenSearch para a classe toda (subir um por teste leva ~15 s cada).
public class OpenSearchFixture : IAsyncLifetime
{
    private readonly OpenSearchContainer _container = new OpenSearchBuilder("opensearchproject/opensearch:2.19.1")
        .WithSecurityEnabled(false)
        .Build();

    public OpenSearchService Service { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var settings = new ConnectionSettings(new Uri(_container.GetConnectionString()))
            .DefaultMappingFor<GameIndexDocument>(m => m.IndexName(OpenSearchService.IndexAlias));
        Service = new OpenSearchService(new OpenSearchClient(settings), NullLogger<OpenSearchService>.Instance);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

// Contra um OpenSearch de verdade (container): prova o fuzzy, a ordenação por relevância
// e que a indexação acompanha criação, edição e exclusão. Cada teste parte de um índice
// novo (ReindexAllAsync troca o alias), então a ordem dos testes não importa.
public class OpenSearchServiceTests(OpenSearchFixture fixture) : IClassFixture<OpenSearchFixture>, IAsyncLifetime
{
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    private readonly OpenSearchService _service = fixture.Service;

    private static readonly GameSearchDocument Witcher = Doc("The Witcher 3: Wild Hunt", "Open world RPG with monster hunting", GameGenre.RPG);
    private static readonly GameSearchDocument Gwent = Doc("Gwent", "Card game from the world of The Witcher", GameGenre.Strategy);
    private static readonly GameSearchDocument Fifa = Doc("FIFA 26", "Football simulation with real clubs", GameGenre.Sports, price: 49.9m);

    public Task InitializeAsync() => _service.ReindexAllAsync([Witcher, Gwent, Fifa]);

    public Task DisposeAsync() => Task.CompletedTask;

    private static GameSearchDocument Doc(
        string title, string description, GameGenre genre, GameStatus status = GameStatus.Active, decimal price = 99.9m)
        => new(Guid.NewGuid(), title, description, price, genre, status, Tomorrow);

    private static GameSearchCriteria Term(string term) => new(term, null, null, null);

    [Fact]
    public async Task SearchAsync_WithTypo_ShouldStillFindTheGame()
    {
        var result = await _service.SearchAsync(Term("witchr"), 1, 20);

        Assert.Contains(result.Hits, h => h.Id == Witcher.Id);
        // "with" (na descrição do FIFA) está a 2 edições de "witchr": não pode casar.
        Assert.DoesNotContain(result.Hits, h => h.Id == Fifa.Id);
    }

    [Fact]
    public async Task SearchAsync_ShouldRankTitleMatchAboveDescriptionMatch()
    {
        var result = await _service.SearchAsync(Term("witcher"), 1, 20);

        Assert.Equal(2, result.Total);
        Assert.Equal(Witcher.Id, result.Hits.First().Id);
        Assert.Equal(Gwent.Id, result.Hits.Last().Id);
        Assert.True(result.Hits.First().Score > result.Hits.Last().Score);
    }

    [Fact]
    public async Task IndexAsync_NewAndEditedGames_ShouldBeSearchableRightAway()
    {
        var created = Doc("Hollow Knight", "Metroidvania", GameGenre.Action);
        await _service.IndexAsync(created);
        Assert.Contains((await _service.SearchAsync(Term("hollow"), 1, 20)).Hits, h => h.Id == created.Id);

        await _service.IndexAsync(created with { Title = "Hollow Knight: Silksong" });
        var hit = Assert.Single((await _service.SearchAsync(Term("silksong"), 1, 20)).Hits);
        Assert.Equal("Hollow Knight: Silksong", hit.Title);
    }

    [Fact]
    public async Task RemoveAsync_ShouldTakeGameOutOfSearch()
    {
        await _service.RemoveAsync(Fifa.Id);

        Assert.Empty((await _service.SearchAsync(Term("fifa"), 1, 20)).Hits);
    }

    [Fact]
    public async Task SearchAsync_ShouldIgnoreInactiveGames()
    {
        await _service.IndexAsync(Witcher with { Status = GameStatus.Inactive });

        Assert.DoesNotContain((await _service.SearchAsync(Term("witcher"), 1, 20)).Hits, h => h.Id == Witcher.Id);
    }

    [Fact]
    public async Task SearchAsync_ShouldIgnoreAccents()
    {
        var pokemon = Doc("Pokémon Legends", "Catch them all", GameGenre.RPG);
        await _service.IndexAsync(pokemon);

        Assert.Contains((await _service.SearchAsync(Term("pokemon"), 1, 20)).Hits, h => h.Id == pokemon.Id);
    }

    [Fact]
    public async Task ReindexAllAsync_RunTwice_ShouldKeepOneIndexBehindTheAlias()
    {
        await _service.ReindexAllAsync([Witcher, Gwent, Fifa]);

        var result = await _service.SearchAsync(Term("witcher"), 1, 20);
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task SearchAsync_WithGenreFilterOnly_ShouldReturnOnlyThatGenre()
    {
        var result = await _service.SearchAsync(new GameSearchCriteria(null, GameGenre.RPG, null, null), 1, 20);

        var hit = Assert.Single(result.Hits);
        Assert.Equal(Witcher.Id, hit.Id);
    }

    [Fact]
    public async Task SearchAsync_WithTermAndGenre_ShouldFilterWithoutChangingRelevance()
    {
        var result = await _service.SearchAsync(new GameSearchCriteria("witcher", GameGenre.Strategy, null, null), 1, 20);

        var hit = Assert.Single(result.Hits);
        Assert.Equal(Gwent.Id, hit.Id);
    }

    [Fact]
    public async Task SearchAsync_WithPriceRange_ShouldReturnOnlyGamesInRange()
    {
        var result = await _service.SearchAsync(new GameSearchCriteria(null, null, 0m, 50m), 1, 20);

        var hit = Assert.Single(result.Hits);
        Assert.Equal(Fifa.Id, hit.Id);
    }
}
