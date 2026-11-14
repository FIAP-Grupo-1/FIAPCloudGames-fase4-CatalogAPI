namespace CatalogAPI.Infrastructure.Search;

// Seção "Search" da configuração (Search__Enabled, Search__Endpoint... nas variáveis de ambiente).
public class SearchOptions
{
    public const string Section = "Search";

    // Desligada por padrão: Minikube e compose sem OpenSearch continuam subindo.
    public bool Enabled { get; set; }

    // Ex.: http://localhost:9200 (container local) ou https://search-fcg-search-xxx.us-east-1.es.amazonaws.com
    public string? Endpoint { get; set; }

    // Amazon OpenSearch com acesso por IAM: assina cada requisição (SigV4) com as
    // credenciais da cadeia padrão do SDK (Pod Identity no EKS, AWS_PROFILE localmente).
    public bool UseAwsSigV4 { get; set; }

    public string Region { get; set; } = "us-east-1";
}
