using CatalogAPI.Application.Shared.Search;
using CatalogAPI.Domain.Contexts.Games.Enums;
using Microsoft.Extensions.Logging;
using OpenSearch.Client;
using OpenSearch.Net;

namespace CatalogAPI.Infrastructure.Search;

// Busca de jogos no OpenSearch (Amazon OpenSearch na AWS, container localmente).
//
// Índice: a API sempre fala com o alias "games". A carga completa (ReindexAllAsync)
// monta um índice novo "games-<timestamp>", troca o alias de uma vez e apaga os antigos,
// então a busca nunca fica vazia durante um rolling update (cada pod que sobe refaz a
// carga sem derrubar o índice que o outro está usando).
public class OpenSearchService(IOpenSearchClient client, ILogger<OpenSearchService> logger) : ISearchService
{
    public const string IndexAlias = "games";
    private const string IndexPrefix = IndexAlias + "-";
    private const string FoldingAnalyzer = "folding";
    private const int BulkBatchSize = 1000;

    public async Task IndexAsync(GameSearchDocument game, CancellationToken cancellationToken = default)
    {
        try
        {
            // Refresh WaitFor: o jogo já aparece na próxima busca (sem esperar o refresh de 1s).
            var response = await client.IndexAsync(
                GameIndexDocument.From(game),
                i => i.Index(IndexAlias).Id(game.Id.ToString()).Refresh(Refresh.WaitFor),
                cancellationToken);

            if (!response.IsValid)
                logger.LogError(response.OriginalException,
                    "Falha ao indexar o jogo {GameId} na busca: {Error}", game.Id, response.DebugInformation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao indexar o jogo {GameId} na busca", game.Id);
        }
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.DeleteAsync<GameIndexDocument>(
                id.ToString(),
                d => d.Index(IndexAlias).Refresh(Refresh.WaitFor),
                cancellationToken);

            // 404: o jogo já não estava no índice, nada a fazer.
            if (!response.IsValid && response.ApiCall.HttpStatusCode != 404)
                logger.LogError(response.OriginalException,
                    "Falha ao remover o jogo {GameId} da busca: {Error}", id, response.DebugInformation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao remover o jogo {GameId} da busca", id);
        }
    }

    // Lança exceção em caso de falha: quem chama (SearchIndexInitializer) tenta de novo.
    public async Task ReindexAllAsync(
        IEnumerable<GameSearchDocument> games, CancellationToken cancellationToken = default)
    {
        var newIndex = $"{IndexPrefix}{DateTime.UtcNow:yyyyMMddHHmmssfff}";

        EnsureValid(await client.Indices.CreateAsync(newIndex, DefineIndex, cancellationToken),
            $"criar o índice {newIndex}");

        var total = 0;
        foreach (var batch in games.Select(GameIndexDocument.From).Chunk(BulkBatchSize))
        {
            var bulk = await client.BulkAsync(b => b
                    .Index(newIndex)
                    .IndexMany(batch, (op, doc) => op.Id(doc.Id)),
                cancellationToken);

            EnsureValid(bulk, $"carregar {batch.Length} jogos em {newIndex}");
            if (bulk.Errors)
                throw new SearchUnavailableException(
                    $"Bulk em {newIndex} com {bulk.ItemsWithErrors.Count()} erro(s): " +
                    string.Join("; ", bulk.ItemsWithErrors.Take(3).Select(i => i.Error?.Reason)));

            total += batch.Length;
        }

        EnsureValid(await client.Indices.RefreshAsync(newIndex, ct: cancellationToken),
            $"atualizar o índice {newIndex}");

        // Troca atômica do alias para o índice novo.
        var current = await client.Indices.GetAliasAsync(Indices.All, a => a.Name(IndexAlias), cancellationToken);
        var previous = current.IsValid ? current.Indices.Keys.Select(i => i.Name).ToList() : [];

        EnsureValid(await client.Indices.BulkAliasAsync(a =>
        {
            foreach (var old in previous)
                a.Remove(r => r.Index(old).Alias(IndexAlias));
            return a.Add(ad => ad.Index(newIndex).Alias(IndexAlias));
        }, cancellationToken), $"apontar o alias {IndexAlias} para {newIndex}");

        // Apaga os índices antigos (o que o alias usava e sobras de cargas anteriores).
        var all = await client.Indices.GetAsync($"{IndexPrefix}*", ct: cancellationToken);
        var stale = all.IsValid
            ? all.Indices.Keys.Select(i => i.Name).Where(name => name != newIndex).ToList()
            : [];
        if (stale.Count > 0)
        {
            var deleted = await client.Indices.DeleteAsync(Indices.Index(stale), ct: cancellationToken);
            if (!deleted.IsValid)
                logger.LogWarning("Não foi possível apagar índices antigos de busca ({Indices}): {Error}",
                    string.Join(", ", stale), deleted.DebugInformation);
        }

        logger.LogInformation("Índice de busca recriado: {Total} jogos em {Index}", total, newIndex);
    }

    public async Task<GameSearchResult> SearchAsync(
        string query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ISearchResponse<GameIndexDocument> response;
        try
        {
            response = await client.SearchAsync<GameIndexDocument>(s => s
                    .Index(IndexAlias)
                    .From((page - 1) * pageSize)
                    .Size(pageSize)
                    .TrackTotalHits()
                    .Query(q => q.Bool(b => b
                        // Fuzzy: AUTO aceita 1 erro em termos de 3-5 letras e 2 a partir de 6
                        // ("witchr" -> "witcher"). prefix_length 1: a 1a letra tem de bater,
                        // o que corta falsos positivos e deixa a consulta mais barata.
                        // Título pesa 3x a descrição na relevância (score).
                        .Must(m => m.MultiMatch(mm => mm
                            .Query(query)
                            .Fields(f => f
                                .Field(d => d.Title, 3)
                                .Field(d => d.Description))
                            .Fuzziness(Fuzziness.Auto)
                            .PrefixLength(1)))
                        // Jogos desativados não aparecem na busca.
                        .MustNot(mn => mn.Term(t => t
                            .Field(d => d.Status)
                            .Value(GameStatus.Inactive.ToString())))))
                // Sem .Sort(): o padrão do OpenSearch é ordenar por _score (relevância).
                , cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SearchUnavailableException("Search engine unavailable.", ex);
        }

        // Índice ainda não criado (a API acabou de subir): nenhum resultado, não é erro.
        if (!response.IsValid && response.ServerError?.Error?.Type == "index_not_found_exception")
            return new GameSearchResult([], 0);

        if (!response.IsValid)
            throw new SearchUnavailableException(
                $"Search engine error: {response.ServerError?.Error?.Reason ?? "unknown"}",
                response.OriginalException);

        var hits = response.Hits
            .Select(h => new GameSearchHit(
                Guid.Parse(h.Source.Id),
                h.Source.Title,
                h.Source.Price,
                Enum.Parse<GameGenre>(h.Source.Genre),
                h.Score ?? 0))
            .ToList();

        return new GameSearchResult(hits, response.Total);
    }

    private static ICreateIndexRequest DefineIndex(CreateIndexDescriptor c) => c
        // 1 shard, 0 réplicas: o domínio tem 1 nó (réplica nunca seria alocada e o índice
        // ficaria amarelo).
        .Settings(s => s
            .NumberOfShards(1)
            .NumberOfReplicas(0)
            // "Pokemon" acha "Pokémon" e vice-versa; maiúsculas não importam.
            .Analysis(a => a.Analyzers(an => an
                .Custom(FoldingAnalyzer, ca => ca
                    .Tokenizer("standard")
                    .Filters("lowercase", "asciifolding")))))
        .Map<GameIndexDocument>(m => m
            .Properties(p => p
                .Keyword(k => k.Name(d => d.Id))
                .Text(t => t
                    .Name(d => d.Title)
                    .Analyzer(FoldingAnalyzer)
                    .Fields(f => f.Keyword(k => k.Name("raw"))))
                .Text(t => t.Name(d => d.Description).Analyzer(FoldingAnalyzer))
                .Number(n => n.Name(d => d.Price).Type(NumberType.ScaledFloat).ScalingFactor(100))
                .Keyword(k => k.Name(d => d.Genre))
                .Keyword(k => k.Name(d => d.Status))
                .Date(dt => dt.Name(d => d.ReleaseDate))));

    private static void EnsureValid(IResponse response, string action)
    {
        if (!response.IsValid)
            throw new SearchUnavailableException(
                $"Falha ao {action}: {response.ServerError?.Error?.Reason ?? response.OriginalException?.Message}",
                response.OriginalException);
    }
}
