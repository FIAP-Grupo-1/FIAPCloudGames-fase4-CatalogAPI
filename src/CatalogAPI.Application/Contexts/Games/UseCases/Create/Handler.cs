using CatalogAPI.Application.Contexts.Games.UseCases.Create.DTOs;
using CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;
using CatalogAPI.Application.Shared.Cache;
using CatalogAPI.Application.Shared.ElasticSearch;
using CatalogAPI.Domain.Contexts.Games.Commands;
using CatalogAPI.Domain.Contexts.Games.Entities;
using Elastic.Clients.Elasticsearch;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Create;

public class Handler(ICreate repository, ICacheService cacheService, IElasticSearchService elasticSearchService, ILogger<Handler> logger)
    : IRequestHandler<Request, Response>
{
    public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
    {
        var res = Specification.Ensure(request);
        if (!res.IsValid)
            return new Response("Invalid request", 400, res.Notifications);

        // Exceções de domínio (ex.: título/preço inválidos) e de infraestrutura propagam para o
        // GlobalExceptionHandler, que as traduz para o status HTTP adequado.
        var game = await repository.CreateAsync(
            new Game(
                request.Title,
                request.Description,
                request.Price,
                request.Genre,
                request.ReleaseDate),
            cancellationToken);

        // O novo jogo precisa aparecer nas listagens já em cache.
        await cacheService.RemoveByPrefixAsync("games:all:", cancellationToken);


        // Sincroniza o índice de busca. O banco é a fonte da verdade:
        // falha aqui não pode desfazer nem derrubar a criação.
        var indexed = await elasticSearchService.Create(
            GameDocumentExtension.ToDocument(game),
            "games",
            game.Id.ToString(),
            cancellationToken);


        return new Response(
            "Game created",
            new CreateGameResponse(
                game.Id,
                game.Title.Value,
                game.Description,
                game.Price.Amount,
                game.Genre)
            );
    }
}