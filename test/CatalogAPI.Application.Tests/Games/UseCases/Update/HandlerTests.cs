using CatalogAPI.Application.Contexts.Games.UseCases.Update;
using CatalogAPI.Application.Shared.Cache;
using CatalogAPI.Application.Shared.Search;
using CatalogAPI.Domain.Contexts.Games.Commands;
using CatalogAPI.Domain.Contexts.Games.Entities;
using CatalogAPI.Domain.Contexts.Games.Enums;
using CatalogAPI.Domain.Contexts.Games.Exceptions;
using CatalogAPI.Domain.Shared;
using NSubstitute;

namespace CatalogAPI.Application.Tests.Games.UseCases.Update;

public class HandlerTests
{
    private readonly IUpdate _repository;
    private readonly ICacheService _cacheService;
    private readonly ISearchService _searchService;
    private readonly Handler _handler;
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    public HandlerTests()
    {
        _repository = Substitute.For<IUpdate>();
        _cacheService = Substitute.For<ICacheService>();
        _searchService = Substitute.For<ISearchService>();
        _handler = new Handler(_repository, _cacheService, _searchService);
    }

    // O repositório devolve o jogo já atualizado (null quando não existe).
    private static Game UpdatedGame() =>
        new("Updated Title", "Updated description text here", 59.99m, GameGenre.RPG, Tomorrow);

    private static Request ValidRequest(Guid? id = null) =>
        new(id ?? Guid.NewGuid(), "Updated Title", "Updated description text here", 59.99m, GameGenre.RPG, GameStatus.Active, Tomorrow);

    [Fact]
    public async Task Handle_WithValidRequest_ShouldInvalidateGameAndListCaches()
    {
        var request = ValidRequest();

        _repository.UpdateAsync(
                request.Id, request.Title, request.Description, request.Price,
                request.Genre, request.Status, request.ReleaseDate, Arg.Any<CancellationToken>())
            .Returns(UpdatedGame());

        await _handler.Handle(request, CancellationToken.None);

        await _cacheService.Received(1).RemoveAsync($"games:{request.Id}", Arg.Any<CancellationToken>());
        await _cacheService.Received(1).RemoveByPrefixAsync("games:all:", Arg.Any<CancellationToken>());
        await _cacheService.Received(1).RemoveByPrefixAsync("libraries:all:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenGameNotFound_ShouldNotInvalidateCache()
    {
        _repository.UpdateAsync(
                Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<decimal?>(),
                Arg.Any<GameGenre?>(), Arg.Any<GameStatus?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Game?>(null));

        await Assert.ThrowsAsync<GameNotFoundException>(() => _handler.Handle(ValidRequest(), CancellationToken.None));

        await _cacheService.DidNotReceive().RemoveByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldReturnNoContent()
    {
        var request = ValidRequest();

        _repository.UpdateAsync(
                request.Id, request.Title, request.Description, request.Price,
                request.Genre, request.Status, request.ReleaseDate, Arg.Any<CancellationToken>())
            .Returns(UpdatedGame());

        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(204, response.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenGameNotFound_ShouldThrowGameNotFound()
    {
        var request = ValidRequest();

        _repository.UpdateAsync(
                Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<decimal?>(),
                Arg.Any<GameGenre?>(), Arg.Any<GameStatus?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Game?>(null));

        // GlobalExceptionHandler mapeia GameNotFoundException -> 404.
        await Assert.ThrowsAsync<GameNotFoundException>(() => _handler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WithEmptyGuid_ShouldReturnBadRequestWithoutCallingRepository()
    {
        var request = ValidRequest(Guid.Empty);

        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.False(response.IsSuccess);
        Assert.Equal(400, response.StatusCode);
        await _repository.DidNotReceive().UpdateAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<decimal?>(),
            Arg.Any<GameGenre?>(), Arg.Any<GameStatus?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenRepositoryThrowsDomainException_ShouldPropagate()
    {
        var request = ValidRequest();

        _repository.UpdateAsync(
                Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<decimal?>(),
                Arg.Any<GameGenre?>(), Arg.Any<GameStatus?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Game?>(new DomainException("Domain rule violated")));

        await Assert.ThrowsAsync<DomainException>(() => _handler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenRepositoryThrowsException_ShouldPropagate()
    {
        var request = ValidRequest();

        _repository.UpdateAsync(
                Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<decimal?>(),
                Arg.Any<GameGenre?>(), Arg.Any<GameStatus?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Game?>(new Exception("Unexpected error")));

        await Assert.ThrowsAsync<Exception>(() => _handler.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldReindexUpdatedGame()
    {
        var request = ValidRequest();
        var updated = UpdatedGame();

        _repository.UpdateAsync(
                request.Id, request.Title, request.Description, request.Price,
                request.Genre, request.Status, request.ReleaseDate, Arg.Any<CancellationToken>())
            .Returns(updated);

        await _handler.Handle(request, CancellationToken.None);

        await _searchService.Received(1).IndexAsync(
            Arg.Is<GameSearchDocument>(d => d.Id == updated.Id && d.Title == "Updated Title" && d.Genre == GameGenre.RPG),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenGameNotFound_ShouldNotTouchSearch()
    {
        _repository.UpdateAsync(
                Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<decimal?>(),
                Arg.Any<GameGenre?>(), Arg.Any<GameStatus?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Game?>(null));

        await Assert.ThrowsAsync<GameNotFoundException>(() => _handler.Handle(ValidRequest(), CancellationToken.None));

        await _searchService.DidNotReceive().IndexAsync(Arg.Any<GameSearchDocument>(), Arg.Any<CancellationToken>());
    }
}
