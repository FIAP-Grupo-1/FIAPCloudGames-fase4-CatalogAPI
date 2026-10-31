using CatalogAPI.Domain.Contexts.Games.Entities;

namespace CatalogAPI.Domain.Contexts.Libraries.Commands;

public interface IGamePurchase
{
    Task<Game> ExecuteAsync(Guid userId, Guid gameId, CancellationToken cancellationToken);
}
