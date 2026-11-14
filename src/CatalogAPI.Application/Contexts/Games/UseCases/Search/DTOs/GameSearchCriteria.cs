using System;
using System.Collections.Generic;
using System.Text;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;

public record GameSearchCriteria(
    string? Term,
    string? Genre,
    decimal? MinPrice,
    decimal? MaxPrice);