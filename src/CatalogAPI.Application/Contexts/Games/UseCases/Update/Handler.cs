using CatalogAPI.Application.Shared.Cache;
using CatalogAPI.Application.Shared.Search;
using CatalogAPI.Domain.Contexts.Games.Commands;
using CatalogAPI.Domain.Contexts.Games.Exceptions;
using CatalogAPI.Domain.Contexts.Games.Queries;
using MediatR;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Update;

public class Handler(
    IUpdate repository,
    IGetById getById,
    ICacheService cacheService,
    ISearchService searchService) : IRequestHandler<Request, Response>
{
    public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
    {
        var cacheKey = $"games:{request.Id}";

        var res = Specification.Ensure(request);
        if (!res.IsValid)
            return new Response("Invalid request", 400, res.Notifications);

        var isSuccess = await repository
            .UpdateAsync(
                request.Id,
                request.Title,
                request.Description,
                request.Price,
                request.Genre,
                request.Status,
                request.ReleaseDate,
                cancellationToken);

        if (!isSuccess)
            throw new GameNotFoundException($"Game {request.Id} not found in the catalog.");

        await cacheService.RemoveAsync(cacheKey, cancellationToken);

        // Título, preço e gênero também aparecem nas listagens de jogos e nas bibliotecas.
        await cacheService.RemoveByPrefixAsync("games:all:", cancellationToken);
        await cacheService.RemoveByPrefixAsync("libraries:all:", cancellationToken);

        // Reindexa com o estado salvo no banco (o repositório só devolve se atualizou),
        // para a busca refletir título, descrição, preço e status novos.
        var updated = await getById.GetByIdAsync(request.Id, cancellationToken);
        if (updated is not null)
            await searchService.IndexAsync(GameSearchDocument.From(updated), cancellationToken);

        return new Response();
    }
}