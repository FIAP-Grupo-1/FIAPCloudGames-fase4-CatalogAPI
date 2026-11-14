using CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;
using CatalogAPI.Application.Shared.Search;
using MediatR;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search;

// Busca no OpenSearch, independente do Postgres: tolera erros de digitação (fuzzy) e
// devolve os jogos ordenados por relevância. Sem cache de propósito: o resultado muda a
// cada jogo criado/editado e a busca já é a consulta rápida.
public class Handler(ISearchService searchService) : IRequestHandler<Request, Response>
{
    public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
    {
        var res = Specification.Ensure(request);
        if (!res.IsValid)
            return new Response("Invalid request", 400, res.Notifications);

        var query = request.Q!.Trim();
        var result = await searchService.SearchAsync(query, request.Page, request.PageSize, cancellationToken);

        return new Response(
            "Success",
            new PagedSearchResponse(query, result.Hits, request.Page, request.PageSize, result.Total));
    }
}
