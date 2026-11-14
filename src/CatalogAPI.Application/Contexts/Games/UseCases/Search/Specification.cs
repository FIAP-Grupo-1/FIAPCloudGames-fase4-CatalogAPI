using Flunt.Notifications;
using Flunt.Validations;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search;

public static class Specification
{
    public const int MaxQueryLength = 100;

    public static Contract<Notification> Ensure(Request request)
        => new Contract<Notification>()
            .IsNotNullOrWhiteSpace(request.Q, "Q", "Search term (q) is required")
            .IsLowerOrEqualsThan(request.Q?.Length ?? 0, MaxQueryLength, "Q",
                $"Search term (q) cannot exceed {MaxQueryLength} characters")
            .IsGreaterThan(request.Page, 0, "Page", "Page must be greater than 0")
            .IsGreaterThan(request.PageSize, 0, "PageSize", "PageSize must be greater than 0")
            .IsLowerOrEqualsThan(request.PageSize, 50, "PageSize", "PageSize cannot be greater than 50");
}
