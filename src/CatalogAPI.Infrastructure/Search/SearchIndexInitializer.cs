using CatalogAPI.Application.Shared.Search;
using CatalogAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CatalogAPI.Infrastructure.Search;

// Carga completa do índice de busca a partir do Postgres, toda vez que a API sobe.
// Cobre a carga inicial (o Postgres do EKS nasce vazio a cada cluster-up e o domínio
// OpenSearch também) e corrige qualquer jogo que tenha ficado fora do índice porque o
// OpenSearch estava fora do ar na hora da escrita. Roda em segundo plano: a API já
// atende enquanto o índice é montado.
public class SearchIndexInitializer(
    IServiceScopeFactory scopeFactory,
    ISearchService searchService,
    ILogger<SearchIndexInitializer> logger) : BackgroundService
{
    private const int MaxAttempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var games = await db.Games.AsNoTracking().ToListAsync(stoppingToken);
                await searchService.ReindexAllAsync(games.Select(GameSearchDocument.From), stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Busca ainda indisponível para a carga do índice; nova tentativa em {Delay}s ({Attempt}/{MaxAttempts})",
                    RetryDelay.TotalSeconds, attempt, MaxAttempts);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        logger.LogError("Carga do índice de busca desistiu após {MaxAttempts} tentativas", MaxAttempts);
    }
}
