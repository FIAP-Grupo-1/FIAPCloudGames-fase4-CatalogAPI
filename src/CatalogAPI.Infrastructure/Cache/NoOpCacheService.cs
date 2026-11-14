using CatalogAPI.Application.Shared.Cache;

namespace CatalogAPI.Infrastructure.Cache;

public class NoOpCacheService : ICacheService
{
    public Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T?>> buscarDados,
        TimeSpan expiracao,
        CancellationToken cancellationToken = default)
        => buscarDados();

    public Task SetAsync<T>(
        string key,
        T valor,
        TimeSpan expiracao,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}