using CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;
using CatalogAPI.Application.Shared.Search;
using MediatR;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search;

// Busca no OpenSearch, independente do Postgres: o termo tolera erros de digitação (fuzzy)
// e ordena por relevância; gênero e faixa de preço filtram sem mexer no score. Sem cache
// de propósito: o resultado muda a cada jogo criado/editado e a busca já é a consulta rápida.
public class Handler(ISearchService searchService) : IRequestHandler<Request, Response>
{
    public async Task<Response> Handle(Request request, CancellationToken cancellationToken)
    {
        var res = Specification.Ensure(request);
        if (!res.IsValid)
            return new Response("Invalid request", 400, res.Notifications);

        var criteria = new GameSearchCriteria(
            string.IsNullOrWhiteSpace(request.Term) ? null : request.Term.Trim(),
            request.Genre,
            request.MinPrice,
            request.MaxPrice);

        var result = await searchService.SearchAsync(criteria, request.Page, request.Size, cancellationToken);

        return new Response(
            "Busca concluída",
            new PagedSearchResponse(criteria, result.Hits, request.Page, request.Size, result.Total));
    }
}
