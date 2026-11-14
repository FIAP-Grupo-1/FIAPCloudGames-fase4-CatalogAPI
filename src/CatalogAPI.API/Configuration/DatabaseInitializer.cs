using CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;
using CatalogAPI.Infrastructure.Data;
using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;

namespace CatalogAPI.API.Configuration;

public static class DatabaseInitializer
{
    private const int MaxRetries = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    public static async Task MigrateAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await MigrateWithRetryAsync(db, app.Logger);
    }

    /// <summary>
    /// Indexa todos os jogos existentes no Elasticsearch ao iniciar a aplicação.
    /// Só executa a carga se o índice estiver vazio, evitando reindexação desnecessária.
    /// </summary>
    public static async Task SeedElasticSearchAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger  = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
        var db      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var elastic = scope.ServiceProvider.GetRequiredService<ElasticsearchClient>();

        const string indexName = "games";

        try
        {
            // Verifica se o índice já possui documentos — evita reindexação duplicada
            var countResponse = await elastic.CountAsync(c => c.Indices(indexName));

            if (countResponse.IsValidResponse && countResponse.Count > 0)
            {
                logger.LogInformation(
                    "Elasticsearch já contém {Count} documento(s) no índice '{Index}'. Carga inicial ignorada.",
                    countResponse.Count, indexName);
                return;
            }

            // Busca todos os games do banco de uma vez (fonte da verdade)
            var games = await db.Games.AsNoTracking().ToListAsync();

            if (games.Count == 0)
            {
                logger.LogInformation(
                    "Nenhum jogo encontrado no banco. Carga inicial do Elasticsearch ignorada.");
                return;
            }

            logger.LogInformation(
                "Iniciando carga inicial de {Total} jogo(s) no Elasticsearch (índice '{Index}')...",
                games.Count, indexName);

            // Converte para documentos de índice
            var documents = games.Select(g => g.ToDocument()).ToList();

            // Indexação em bulk: cada documento usa seu próprio Id como _id do ES
            var bulkResponse = await elastic.BulkAsync(b => b
                .Index(indexName)
                .IndexMany(documents, (op, doc) => op.Id(doc.Id.ToString())));

            if (bulkResponse.IsValidResponse && !bulkResponse.Errors)
            {
                logger.LogInformation(
                    "Carga inicial concluída: {Total} jogo(s) indexados com sucesso no índice '{Index}'.",
                    documents.Count, indexName);
            }
            else
            {
                var failCount = bulkResponse.ItemsWithErrors.Count();
                logger.LogWarning(
                    "Carga inicial parcialmente concluída. {FailedCount} item(ns) com erro no índice '{Index}'.",
                    failCount, indexName);
            }
        }
        catch (Exception ex)
        {
            // Não derruba a aplicação: a API funciona sem o índice populado
            logger.LogError(ex,
                "Falha ao executar carga inicial do Elasticsearch. A API continuará operando normalmente.");
        }
    }

    private static async Task MigrateWithRetryAsync(AppDbContext db, ILogger logger)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
            try
            {
                await db.Database.MigrateAsync();
                return;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                logger.LogWarning(ex, "Database not ready yet, retrying in {Delay}s... ({Attempt}/{MaxRetries})",
                    RetryDelay.TotalSeconds, attempt, MaxRetries);
                await Task.Delay(RetryDelay);
            }
    }
}
