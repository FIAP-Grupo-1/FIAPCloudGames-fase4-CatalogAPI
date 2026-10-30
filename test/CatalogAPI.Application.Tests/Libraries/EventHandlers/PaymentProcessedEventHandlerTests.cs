using CatalogAPI.Application.Contexts.Libraries.EventHandlers;
using CatalogAPI.Application.Shared.Cache;
using CatalogAPI.Domain.Contexts.Games.Entities;
using CatalogAPI.Domain.Contexts.Games.Enums;
using CatalogAPI.Domain.Contexts.Games.Exceptions;
using CatalogAPI.Domain.Contexts.Libraries.Commands;
using FiapCloudGames.Contracts.Payments;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace CatalogAPI.Application.Tests.Libraries.EventHandlers;

public class PaymentProcessedEventHandlerTests
{
    private readonly IGamePurchase _repository;
    private readonly ICacheService _cacheService;
    private readonly PaymentProcessedEventHandler _handler;
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    public PaymentProcessedEventHandlerTests()
    {
        _repository = Substitute.For<IGamePurchase>();
        _cacheService = Substitute.For<ICacheService>();
        _handler = new PaymentProcessedEventHandler(
            _repository, _cacheService, Substitute.For<ILogger<PaymentProcessedEventHandler>>());
    }

    [Fact]
    public async Task HandleAsync_WhenPaymentApproved_ShouldInvalidateUserLibraryCache()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        _repository.ExecuteAsync(userId, gameId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(NewGame()));

        await _handler.HandleAsync(new PaymentProcessedEvent(userId, gameId, PaymentStatus.Approved), CancellationToken.None);

        await _cacheService.Received(1).RemoveByPrefixAsync($"libraries:all:userId={userId}:", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPaymentRejected_ShouldNotInvalidateCache()
    {
        await _handler.HandleAsync(
            new PaymentProcessedEvent(Guid.NewGuid(), Guid.NewGuid(), PaymentStatus.Rejected), CancellationToken.None);

        await _cacheService.DidNotReceive().RemoveByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static Game NewGame() =>
        new("Game Title Test", "A valid description", 49.99m, GameGenre.Action, Tomorrow);

    [Fact]
    public async Task HandleAsync_WhenPaymentApproved_ShouldAddGameToLibrary()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        _repository.ExecuteAsync(userId, gameId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(NewGame()));

        await _handler.HandleAsync(new PaymentProcessedEvent(userId, gameId, PaymentStatus.Approved), CancellationToken.None);

        await _repository.Received(1).ExecuteAsync(userId, gameId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPaymentRejected_ShouldNotTouchTheLibrary()
    {
        await _handler.HandleAsync(
            new PaymentProcessedEvent(Guid.NewGuid(), Guid.NewGuid(), PaymentStatus.Rejected), CancellationToken.None);

        await _repository.DidNotReceive().ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenRepositoryThrows_ShouldPropagateException()
    {
        _repository.ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Game>(new GameNotFoundException("Game not found in the catalog.")));

        await Assert.ThrowsAsync<GameNotFoundException>(() =>
            _handler.HandleAsync(
                new PaymentProcessedEvent(Guid.NewGuid(), Guid.NewGuid(), PaymentStatus.Approved), CancellationToken.None));
    }
}
