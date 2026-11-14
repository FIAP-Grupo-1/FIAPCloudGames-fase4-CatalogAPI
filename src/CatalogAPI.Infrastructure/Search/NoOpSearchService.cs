using CatalogAPI.Application.Shared.Search;

namespace CatalogAPI.Infrastructure.Search;

// Search:Enabled=false: as escritas no catálogo seguem normalmente e a busca responde 503.
public class NoOpSearchService : ISearchService
{
    public Task IndexAsync(GameSearchDocument game, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ReindexAllAsync(IEnumerable<GameSearchDocument> games, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<GameSearchResult> SearchAsync(
        string query, int page, int pageSize, CancellationToken cancellationToken = default)
        => throw new SearchUnavailableException("Search is disabled (Search:Enabled=false).");
}
