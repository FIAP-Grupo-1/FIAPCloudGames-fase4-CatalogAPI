using CatalogAPI.Domain.Contexts.Games.Enums;

namespace CatalogAPI.Application.Shared.Search;

public record GameSearchHit(
    Guid Id,
    string Title,
    decimal Price,
    GameGenre Genre,
    double Score);

// Hits já vêm ordenados por relevância (score), do maior para o menor.
public record GameSearchResult(IReadOnlyCollection<GameSearchHit> Hits, long Total);
