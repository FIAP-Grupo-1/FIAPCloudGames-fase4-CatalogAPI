namespace CatalogAPI.Application.Shared.Search;

// Motor de busca de jogos (OpenSearch), separado da consulta ao Postgres.
// O Postgres continua sendo a fonte da verdade: IndexAsync e RemoveAsync não lançam
// exceção quando o motor de busca está fora do ar (registram o erro e seguem), para uma
// falha na busca nunca desfazer a escrita no catálogo. A carga completa feita na subida
// da API (ReindexAllAsync) corrige qualquer documento que tenha ficado para trás.
public interface ISearchService
{
    Task IndexAsync(GameSearchDocument game, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    Task ReindexAllAsync(IEnumerable<GameSearchDocument> games, CancellationToken cancellationToken = default);

    Task<GameSearchResult> SearchAsync(
        GameSearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
