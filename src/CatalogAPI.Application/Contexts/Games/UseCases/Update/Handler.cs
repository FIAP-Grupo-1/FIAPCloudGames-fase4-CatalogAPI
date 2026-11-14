using CatalogAPI.Application.Contexts.Games.UseCases.Create.DTOs;
using CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;
using CatalogAPI.Application.Shared.Cache;
using CatalogAPI.Application.Shared.ElasticSearch;
using CatalogAPI.Domain.Contexts.Games.Commands;
using CatalogAPI.Domain.Contexts.Games.Entities;
using CatalogAPI.Domain.Contexts.Games.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Update;

public class Handler(IUpdate repository, ICacheService cacheService, IElasticSearchService elasticSearchService) : IRequestHandler<Request, Response>
{
    public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
    {
        var cacheKey = $"games:{request.Id}";

        var res = Specification.Ensure(request);
        if (!res.IsValid)
            return new Response("Invalid request", 400, res.Notifications);

        var game = await repository
            .UpdateAsync(
                request.Id,
                request.Title,
                request.Description,
                request.Price,
                request.Genre,
                request.Status,
                request.ReleaseDate,
                cancellationToken);

        if (game is null)
            throw new GameNotFoundException($"Game {request.Id} not found in the catalog.");

        await cacheService.RemoveAsync(cacheKey, cancellationToken);

        var indexed = await elasticSearchService.Update(
           GameDocumentExtension.ToDocument(game),
           "games",
           game.Id.ToString(),
           cancellationToken);


        // Título, preço e gênero também aparecem nas listagens de jogos e nas bibliotecas.
        await cacheService.RemoveByPrefixAsync("games:all:", cancellationToken);
        await cacheService.RemoveByPrefixAsync("libraries:all:", cancellationToken);

        return new Response();
    }
}