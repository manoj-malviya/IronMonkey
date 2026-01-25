using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Users.Endpoints;

namespace IronMonkey.ApiService;

public static class Endpoints
{
    public static void MapEndpoints(this WebApplication app)
    {
        var endpoints = app.MapGroup("")
            // .AddEndpointFilter<RequestLoggingFilter>()
            .WithOpenApi();

        endpoints.MapAuthenticationEndpoints();
        endpoints.MapUserEndpoints();
        // endpoints.MapPublisherEndpoints();
        endpoints.MapHealthCheckEndpoints();
        // endpoints.MapInvitationEndpoints();
        // endpoints.MapPostEndpoints();
        // endpoints.MapCommentEndpoints();
        endpoints.MapUserEndpoints();
    }
    
    private static void MapHealthCheckEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => TypedResults.Ok())
            .WithName("HealthCheck")
            .WithTags("Health")
            .WithOpenApi();
    }
    
    private static void MapAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("/auth")
            .WithTags("Authentication");
            
        endpoints.MapPublicGroup()
            .MapEndpoint<Signup>()
            .MapEndpoint<Login>()
            .MapEndpoint<ConfirmEmail>();
    }

    private static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var endpoints = app.MapGroup("/user")
            .WithTags("User");

        endpoints.MapAuthorizedGroup()
            .MapEndpoint<Forecast>();
    }
    
    // private static void MapPublisherEndpoints(this IEndpointRouteBuilder app)
    // {
    //     var endpoints = app.MapGroup("/publisher")
    //         .WithTags("Publisher");

    //     endpoints.MapAuthorizedGroup()
    //         .MapEndpoint<CreateContentTemplate>()
    //         .MapEndpoint<ListContentTemplates>()
    //         .MapEndpoint<SendWriterInvitation>()
    //         .MapEndpoint<ListWriterInvitations>()
    //         .MapEndpoint<AcceptWriterInvitation>()
    //         .MapEndpoint<RejectWriterInvitation>();
    // }                                

    private static RouteGroupBuilder MapPublicGroup(this IEndpointRouteBuilder app, string? prefix = null)
    {
        return app.MapGroup(prefix ?? string.Empty)
            .AllowAnonymous()
            .WithOpenApi();
    }
    private static IEndpointRouteBuilder MapEndpoint<TEndpoint>(this IEndpointRouteBuilder app) where TEndpoint : IEndpoint
    {
        TEndpoint.Map(app);
        return app;
    }
    
    private static RouteGroupBuilder MapAuthorizedGroup(this IEndpointRouteBuilder app, string? prefix = null)
    {
        return app.MapGroup(prefix ?? string.Empty)
            .RequireAuthorization()
            .WithOpenApi();
    }
    
    private static readonly OpenApiSecurityScheme securityScheme = new()
    {
        Type = SecuritySchemeType.Http,
        Name = JwtBearerDefaults.AuthenticationScheme,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        Reference = new()
        {
            Type = ReferenceType.SecurityScheme,
            Id = JwtBearerDefaults.AuthenticationScheme
        }
    };
    
    // private static void MapInvitationEndpoints(this IEndpointRouteBuilder app)
    // {
    //     var endpoints = app.MapGroup("/invitations")
    //         .WithTags("Invitations");

    //     endpoints.MapAuthorizedGroup()
    //         .MapEndpoint<SendWriterInvitation>()
    //         .MapEndpoint<ListWriterInvitations>()
    //         .MapEndpoint<AcceptWriterInvitation>()
    // }
}