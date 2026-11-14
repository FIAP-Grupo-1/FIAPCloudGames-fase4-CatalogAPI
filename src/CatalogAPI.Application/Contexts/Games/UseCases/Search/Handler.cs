using CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;
using CatalogAPI.Application.Shared.ElasticSearch;
using CatalogAPI.Domain.Contexts.Games.Entities;
using MediatR;


namespace CatalogAPI.Application.Contexts.Games.UseCases.Search
{
    public class Handler(IElasticSearchService elasticSearchService) : IRequestHandler<Request, Response>
    {
        public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
        {
            var res = Specification.Ensure(request);
            if (!res.IsValid)
                return new Response("Invalid request", 400, res.Notifications);

            var criteria = new GameSearchCriteria(
                request.Term,
                request.Genre?.ToString(),
                request.MinPrice,
                request.MaxPrice);

            var games = await elasticSearchService.Search<GameDocument>(
                criteria,
                request.Page,
                request.Size,
                "games",
                cancellationToken);

            return new Response(
                "Search completed",
                new SearchGameResponse(games, request.Page, request.Size));
        }
    }
}
