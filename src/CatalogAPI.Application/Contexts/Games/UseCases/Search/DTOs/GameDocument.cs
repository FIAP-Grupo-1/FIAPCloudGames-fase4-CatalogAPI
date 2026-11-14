using CatalogAPI.Domain.Contexts.Games.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs
{
    public record GameDocument(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("genre")] string Genre,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("releaseDate")] DateOnly ReleaseDate);
    public static class GameDocumentExtension 
    {
        public static GameDocument ToDocument(this Game game) => new(
            game.Id,
            game.Title.Value,
            game.Description,
            game.Price.Amount,
            game.Genre.ToString(),
            game.Status.ToString(),
            game.ReleaseDate);
    }

}
