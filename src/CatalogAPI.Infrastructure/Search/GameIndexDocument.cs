using CatalogAPI.Application.Shared.Search;

namespace CatalogAPI.Infrastructure.Search;

// Formato do documento no índice. Classe com setters (e não o record da Application)
// para o serializador do cliente conseguir ler de volta; enums e datas como texto.
public class GameIndexDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Genre { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ReleaseDate { get; set; }

    public static GameIndexDocument From(GameSearchDocument game) => new()
    {
        Id = game.Id.ToString(),
        Title = game.Title,
        Description = game.Description,
        Price = game.Price,
        Genre = game.Genre.ToString(),
        Status = game.Status.ToString(),
        ReleaseDate = game.ReleaseDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
    };
}
