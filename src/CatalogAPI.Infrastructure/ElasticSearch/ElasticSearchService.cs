using CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;
using CatalogAPI.Application.Shared.ElasticSearch;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace CatalogAPI.Infrastructure.ElasticSearch
{
    public class ElasticSearchService(ElasticsearchClient client, ILogger<ElasticSearchService> logger) : IElasticSearchService
    {
        public async Task<bool> Create<T>(T document, IndexName index, string id, CancellationToken cancellationToken = default)
        {
            var response = await client.IndexAsync(document, i => i.Index(index).Id(id), cancellationToken);
            if (!response.IsValidResponse)
            {
                logger.LogError(
                "Falha ao indexar documento no índice {Index}: {Debug}",
                index, response.DebugInformation);
            }
            return response.IsValidResponse;
        }

        public async Task<bool> Update<T>(T document, IndexName index, string id, CancellationToken cancellationToken = default)
        {
            var response = await client.UpdateAsync<T, T>(
                index,
                id,
                u => u.Doc(document).DocAsUpsert(true),
                cancellationToken);

            if (!response.IsValidResponse)
                logger.LogError(
                    "Falha ao atualizar documento {Id} no índice {Index}: {Debug}",
                    id, index, response.DebugInformation);

            return response.IsValidResponse;
        }
        public async Task<bool> Delete(string id, IndexName index, CancellationToken cancellationToken = default)
        {
            var response = await client.DeleteAsync(index, id, cancellationToken);

            if (!response.IsValidResponse)
                logger.LogError("Falha ao remover {Id} do índice {Index}: {Debug}", id, index, response.DebugInformation);

            return response.IsValidResponse;
        }
        public async Task<IReadOnlyCollection<T>> Search<T>(
            GameSearchCriteria criteria, int page, int size, IndexName index, CancellationToken cancellationToken = default)
        {
            var response = await client.SearchAsync<T>(s => s
                .Indices(index)
                .From((page - 1) * size)
                .Size(size)
                .Query(BuildQuery(criteria)),
                cancellationToken);

            EnsureValid(response, index);

            return response.Documents;
        }

        private void EnsureValid<T>(SearchResponse<T> response, IndexName index)
        {
            if (response.IsValidResponse)
                return;

            logger.LogError("Falha na busca no índice {Index}: {Debug}", index, response.DebugInformation);

            throw new InvalidOperationException(
                $"Erro ao buscar no Elasticsearch: {response.ElasticsearchServerError?.Error.Reason}");
        }

        private static Query BuildQuery(GameSearchCriteria criteria)
        {
            var must = new List<Query>();
            var filter = new List<Query>();

            // Texto: fuzzy + relevância (title vale 3x a descrição)
            if (!string.IsNullOrWhiteSpace(criteria.Term))
            {
                must.Add(new MultiMatchQuery
                {
                    Query = criteria.Term,
                    Fields = new[] { "title^3", "description" },
                    Fuzziness = new Fuzziness("AUTO")
                });
            }

            // Filtros exatos: não alteram o score
            if (!string.IsNullOrWhiteSpace(criteria.Genre))
                filter.Add(new TermQuery { Field = "genre", Value = criteria.Genre });

            if (criteria.MinPrice is not null || criteria.MaxPrice is not null)
            {
                filter.Add(new NumberRangeQuery("price")
                {
                    Gte = (double?)criteria.MinPrice,
                    Lte = (double?)criteria.MaxPrice
                });
            }

            return new BoolQuery { Must = must, Filter = filter };
        }

    }
}
