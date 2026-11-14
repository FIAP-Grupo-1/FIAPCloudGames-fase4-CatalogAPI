using CatalogAPI.Domain.Contexts.Games.Enums;
using MediatR;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search;

public record Request(
    string? Term,
    GameGenre? Genre,
    decimal? MinPrice,
    decimal? MaxPrice,
    int Page = 1,
    int Size = 10) : IRequest<Response>;