namespace CatalogAPI.Application.Shared.Cache;

public static class CacheExpiration
{
    public static readonly TimeSpan GamesList = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan GameDetails = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan UserLibrary = TimeSpan.FromMinutes(3);
}