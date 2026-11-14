using CatalogAPI.Domain.Contexts.Games.Enums;

namespace CatalogAPI.Application.Shared.Search;

// Critérios da busca: termo livre (fuzzy, define a relevância) e filtros exatos
// (gênero e faixa de preço, que só recortam o resultado e não mexem no score).
public record GameSearchCriteria(
    string? Term,
    GameGenre? Genre,
    decimal? MinPrice,
    decimal? MaxPrice);
