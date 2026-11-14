using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CatalogAPI.API.Extensions;

public static class BuilderExtension
{
    public static void AddBearerAuthentication(this WebApplicationBuilder builder)
    {
        // A SecretKey precisa ser a MESMA usada pela UsersAPI (emissora do token),
        // pois a assinatura HMAC (simetrica) e validada offline, sem request HTTP.
        // A chave nao fica no repositorio: vem da variavel JwtSettings__SecretKey
        // (Secret fcg-secrets no Kubernetes, .env no docker compose ou user-secrets no dotnet run).
        var secretKey = builder.Configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException(
                "JwtSettings:SecretKey nao configurada. Necessaria para validar o JWT emitido pela UsersAPI. " +
                "Defina a variavel de ambiente JwtSettings__SecretKey ou use " +
                "'dotnet user-secrets set \"JwtSettings:SecretKey\" <chave>' no projeto CatalogAPI.API.");

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                    RoleClaimType = ClaimTypes.Role
                };
            });

        builder.Services.AddAuthorization();
    }

    public static void AddConfigureJsonStringEnumConverter(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
    }
}