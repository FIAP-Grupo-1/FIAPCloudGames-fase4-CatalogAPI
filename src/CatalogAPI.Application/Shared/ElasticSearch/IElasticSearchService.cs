using CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;
using Elastic.Clients.Elasticsearch;

namespace CatalogAPI.Application.Shared.ElasticSearch
{
    public interface IElasticSearchService
    {
        Task<IReadOnlyCollection<T>> Search<T>(GameSearchCriteria criteria, int page, int size, IndexName index, CancellationToken cancellationToken = default);
        Task<bool> Create<T>(T document, IndexName index, string id, CancellationToken cancellationToken = default);
        Task<bool> Update<T>(T document, IndexName index, string id, CancellationToken cancellationToken = default);
        Task<bool> Delete(string id, IndexName index, CancellationToken cancellationToken = default);
    }
}
