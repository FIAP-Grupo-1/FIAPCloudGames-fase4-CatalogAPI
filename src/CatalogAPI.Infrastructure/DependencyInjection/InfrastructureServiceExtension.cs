using Amazon;
using CatalogAPI.Application.Shared.Cache;
using CatalogAPI.Application.Shared.Search;
using CatalogAPI.Domain.Contexts.Games.Commands;
using CatalogAPI.Domain.Contexts.Games.Queries;
using CatalogAPI.Infrastructure.Cache;
using CatalogAPI.Infrastructure.Contexts.Games.UseCases.Create;
using CatalogAPI.Infrastructure.Contexts.Libraries.Messaging;
using CatalogAPI.Infrastructure.Data;
using CatalogAPI.Infrastructure.Search;
using FiapCloudGames.Contracts.Payments;
using FiapCloudGames.RabbitMq.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenSearch.Client;
using OpenSearch.Net;
using OpenSearch.Net.Auth.AwsSigV4;
using StackExchange.Redis;

namespace CatalogAPI.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtension
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<
            ICreate,
            Repository>();

        services.AddScoped<
            IGetAll,
            Contexts.Games.UseCases.GetAll.Repository>();

        services.AddScoped<
            IGetById,
            Contexts.Games.UseCases.GetById.Repository>();

        services.AddScoped<
            IDelete,
            Contexts.Games.UseCases.Delete.Repository>();

        services.AddScoped<
            IUpdate,
            Contexts.Games.UseCases.Update.Repository>();

        services.AddScoped<
            Domain.Contexts.Libraries.Queries.IGetGameAndCheckOwnership,
            Contexts.Libraries.UseCases.InitiateGamePurchase.Repository>();

        services.AddScoped<
            Domain.Contexts.Libraries.Commands.IGamePurchase,
            Contexts.Libraries.UseCases.GamePurchase.Repository>();

        services.AddScoped<
            Domain.Contexts.Libraries.Queries.IGetLibrary,
            Contexts.Libraries.UseCases.GetLibrary.Repository>();

        AddMessaging(services, configuration);

        AddRedis(services, configuration);

        AddSearch(services, configuration);

        return services;
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.AddRabbitMq(configuration);

        services.AddSingleton<
            Application.Shared.Messaging.IIntegrationEventPublisher,
            RabbitMqIntegrationEventPublisher>();

        services.AddSingleton<
            Application.Shared.Messaging.IEventDispatcher,
            EventDispatcher>();

        services.AddRabbitMqConsumer<PaymentProcessedMessageProcessor>(
            new(
                PaymentsMessaging.Exchange,
                "catalog.payment-processed",
                PaymentsMessaging.RoutingKeys.Status));
    }
    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var cacheEnabled = configuration.GetValue("Cache:Enabled", true);

        if (!cacheEnabled)
        {
            services.AddSingleton<ICacheService, NoOpCacheService>();
            return;
        }

        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(
                configuration.GetConnectionString("Redis")));

        services.AddSingleton<ICacheService, RedisCacheService>();
    }

    // Busca de jogos no OpenSearch. Search:Enabled=false (padrão) usa o NoOpSearchService:
    // o catálogo funciona sem motor de busca e só o /search responde 503.
    private static void AddSearch(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(SearchOptions.Section).Get<SearchOptions>() ?? new SearchOptions();

        if (!options.Enabled)
        {
            services.AddSingleton<ISearchService, NoOpSearchService>();
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
            throw new InvalidOperationException("Search:Enabled=true exige Search:Endpoint.");

        services.AddSingleton<IOpenSearchClient>(_ =>
        {
            var pool = new SingleNodeConnectionPool(new Uri(options.Endpoint));

            // Amazon OpenSearch com acesso por IAM: sem usuário/senha, cada requisição é
            // assinada (SigV4) com a credencial da cadeia padrão do AWS SDK.
            var settings = options.UseAwsSigV4
                ? new ConnectionSettings(pool,
                    new AwsSigV4HttpConnection(RegionEndpoint.GetBySystemName(options.Region)))
                : new ConnectionSettings(pool);

            settings.DefaultMappingFor<GameIndexDocument>(m => m.IndexName(OpenSearchService.IndexAlias));

            return new OpenSearchClient(settings);
        });

        services.AddSingleton<ISearchService, OpenSearchService>();
        services.AddHostedService<SearchIndexInitializer>();
    }
}
