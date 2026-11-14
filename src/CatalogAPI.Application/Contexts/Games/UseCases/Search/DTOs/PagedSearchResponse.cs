using CatalogAPI.Application.Shared.Search;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;

public record PagedSearchResponse(
    GameSearchCriteria Criteria,
    IEnumerable<GameSearchHit> Games,
    int Page,
    int PageSize,
    long Total);
