using MediatR;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search;

public record Request(string? Q, int Page = 1, int PageSize = 20) : IRequest<Response>;
