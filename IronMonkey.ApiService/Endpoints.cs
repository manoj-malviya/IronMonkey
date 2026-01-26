using Microsoft.AspNetCore.Authentication.JwtBearer;
using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.ApiService.Common;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService;

public static class Endpoints
{
    public static void MapEndpoints(this WebApplication app)
    {
        var endpoints = app.MapGroup("")
            .AddOpenApiOperationTransformer((operation, context, ct) =>
            {
                // Customize OpenAPI operation here if needed
                return Task.CompletedTask;
            });

        endpoints.MapAuthenticationEndpoints();
        endpoints.MapUserEndpoints();
        endpoints.MapHealthCheckEndpoints();
        endpoints.MapTenantEndpoints();
    }
    
    extension(IEndpointRouteBuilder app)
    {
        private void MapHealthCheckEndpoints()
        {
            app.MapGet("/health", () => TypedResults.Ok())
                .WithName("HealthCheck")
                .WithTags("Health")
                .AddOpenApiOperationTransformer((operation, context, ct) =>
                {
                    // Customize OpenAPI operation here if needed
                    return Task.CompletedTask;
                });
        }

        private void MapAuthenticationEndpoints()
        {
            var endpoints = app.MapGroup("/auth")
                .WithTags("Authentication");

            endpoints.MapIdentityApi<User>();
        }

        private void MapUserEndpoints()
        {
            var endpoints = app.MapGroup("/user")
                .WithTags("User");

            // endpoints.MapAuthorizedGroup()
            //     .MapEndpoint<Forecast>();
        }

        private void MapTenantEndpoints()
        {
            var endpoints = app.MapGroup("/tenant")
                .WithTags("Tenant");

            endpoints.MapPublicGroup()
                .MapEndpoint<CreateTenant>();
        }

        private RouteGroupBuilder MapPublicGroup(string? prefix = null)
        {
            return app.MapGroup(prefix ?? string.Empty)
                .AllowAnonymous()
                .AddOpenApiOperationTransformer((operation, context, ct) =>
                {
                    // Customize OpenAPI operation here if needed
                    return Task.CompletedTask;
                });
        }

        private IEndpointRouteBuilder MapEndpoint<TEndpoint>() where TEndpoint : IEndpoint
        {
            TEndpoint.Map(app);
            return app;
        }

        private RouteGroupBuilder MapAuthorizedGroup(string? prefix = null)
        {
            return app.MapGroup(prefix ?? string.Empty)
                .RequireAuthorization()
                .AddOpenApiOperationTransformer((operation, context, ct) =>
                {
                    // Customize OpenAPI operation here if needed
                    return Task.CompletedTask;
                });
        }
    }
}