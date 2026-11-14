namespace CatalogAPI.Application.Shared.Search;

// Busca desligada (Search:Enabled=false) ou motor de busca fora do ar.
// O GlobalExceptionHandler responde 503: o resto do catálogo continua funcionando.
public class SearchUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
