using Microsoft.AspNetCore.Authentication.JwtBearer;
using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Features.Leads;
using IronMonkey.ApiService.Features.Leads.CustomFields;
using IronMonkey.ApiService.Features.Leads.PipelineStages;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.ApiService.Features.Leads.Ingestion.Api;
using IronMonkey.ApiService.Features.Leads.Ingestion.Csv;
using IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;
using IronMonkey.ApiService.Features.Leads.Merge;
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
        endpoints.MapUserManagementEndpoints();
        endpoints.MapPlatformAdminEndpoints();
        endpoints.MapLeadsEndpoints();
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
            app.MapPublicGroup()
                .MapEndpoint<LoginEndpoint>()
                .MapEndpoint<SignupRequestEndpoint>();
        }

        private void MapPlatformAdminEndpoints()
        {
            var endpoints = app.MapGroup("/admin")
                .WithTags("Platform Admin")
                .RequireAuthorization();

            endpoints.MapEndpoint<ApproveTenantEndpoint>();
            endpoints.MapEndpoint<RejectTenantEndpoint>();
            endpoints.MapEndpoint<ListSignupRequestsEndpoint>();
            endpoints.MapEndpoint<ProvisionTenantEndpoint>();
            endpoints.MapEndpoint<MigrateAllTenantsEndpoint>();
        }

        private void MapUserEndpoints()
        {
            var endpoints = app.MapGroup("/users")
                .WithTags("Users");

            endpoints.MapPublicGroup()
                .MapEndpoint<CreateUser>();
        }

        private void MapTenantEndpoints()
        {
            var endpoints = app.MapGroup("/tenant")
                .WithTags("Tenant");

            endpoints.MapPublicGroup()
                .MapEndpoint<CreateTenant>();
        }

        private void MapUserManagementEndpoints()
        {
            var endpoints = app.MapGroup("/user-management")
                .WithTags("User Management");

            endpoints.MapPublicGroup()
                .MapEndpoint<CreateRole>()
                .MapEndpoint<ListRoles>()
                .MapEndpoint<CreatePermission>()
                .MapEndpoint<ListRolePermissions>()
                .MapEndpoint<AttachPermissionsToRole>();
        }

        private void MapLeadsEndpoints()
        {
            CreateCustomFieldEndpoint.Map(app);
            ListCustomFieldsEndpoint.Map(app);
            CreatePipelineStageEndpoint.Map(app);
            ListPipelineStagesEndpoint.Map(app);
            UpdatePipelineStageEndpoint.Map(app);
            CreateLeadEndpoint.Map(app);
            CheckDuplicatesEndpoint.Map(app);
            MergeLeadsEndpoint.Map(app);
            // API key management endpoints (require JWT auth)
            GenerateApiKeyEndpoint.Map(app);
            ListApiKeysEndpoint.Map(app);
            DeleteApiKeyEndpoint.Map(app);
            // External lead ingestion (X-Api-Key auth, no JWT)
            CreateLeadViaApiEndpoint.Map(app);
            // Web form endpoints
            CreateWebFormEndpoint.Map(app);
            GetWebFormPageEndpoint.Map(app);
            SubmitWebFormEndpoint.Map(app);
            DeleteWebFormEndpoint.Map(app);
            // CSV bulk import endpoints
            UploadLeadsFromCsvEndpoint.Map(app);
            GetImportStatusEndpoint.Map(app);
            GetImportErrorsEndpoint.Map(app);
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