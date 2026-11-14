using System;
using System.Collections.Generic;
using System.Text;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;

public record SearchGameResponse(
    IReadOnlyCollection<GameDocument> Items,
    int Page,
    int PageSize
    );
