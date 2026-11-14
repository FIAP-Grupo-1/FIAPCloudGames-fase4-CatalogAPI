using CatalogAPI.Domain.Contexts.Games.Entities;
using CatalogAPI.Domain.Contexts.Games.Enums;

namespace CatalogAPI.Application.Shared.Search;

// O que vai para o índice de busca: só os campos que a busca usa ou devolve.
public record GameSearchDocument(
    Guid Id,
    string Title,
    string Description,
    decimal Price,
    GameGenre Genre,
    GameStatus Status,
    DateOnly ReleaseDate)
{
    public static GameSearchDocument From(Game game) => new(
        game.Id,
        game.Title.Value,
        game.Description,
        game.Price.Amount,
        game.Genre,
        game.Status,
        game.ReleaseDate);
}
