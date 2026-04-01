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
using IronMonkey.ApiService.Features.Leads.Pipeline.Tasks;
using IronMonkey.ApiService.Features.Leads.Pipeline.Routing;
using IronMonkey.ApiService.Features.Leads.Pipeline.States;
using IronMonkey.ApiService.Features.Leads.Pipeline.Kanban;
using IronMonkey.ApiService.Features.Activity.Timeline;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.ApiService.Features.Reports.Pipeline;
using IronMonkey.ApiService.Features.Reports.Conversion;
using IronMonkey.ApiService.Features.Reports.Performance;
using IronMonkey.ApiService.Features.Recipes;
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
        endpoints.MapIngestionEndpoints();
        endpoints.MapPipelineEndpoints();
        endpoints.MapReportEndpoints();
        endpoints.MapActivityEndpoints();
        endpoints.MapRecipeEndpoints();
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
            endpoints.MapEndpoint<GetSignupRequestEndpoint>();
            endpoints.MapEndpoint<ProvisionTenantEndpoint>();
            endpoints.MapEndpoint<MigrateAllTenantsEndpoint>();
            endpoints.MapEndpoint<ListTenantsEndpoint>();
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
            CreateTaskEndpoint.Map(app);
            ListTasksEndpoint.Map(app);
            UpdateTaskEndpoint.Map(app);
            ConfigureRoutingEndpoint.Map(app);
            GetRoutingConfigEndpoint.Map(app);
            ConfigureTransitionsEndpoint.Map(app);
            ListTransitionsEndpoint.Map(app);
            GetKanbanBoardEndpoint.Map(app);
            MoveLeadEndpoint.Map(app);
        }

        private void MapIngestionEndpoints()
        {
            // API key management (authenticated, for tenant admins)
            GenerateApiKeyEndpoint.Map(app);
            ListApiKeysEndpoint.Map(app);
            DeleteApiKeyEndpoint.Map(app);

            // External REST API lead creation (X-Api-Key auth, rate limited)
            CreateLeadViaApiEndpoint.Map(app);

            // CSV import (authenticated)
            UploadLeadsFromCsvEndpoint.Map(app);
            GetImportStatusEndpoint.Map(app);
            GetImportErrorsEndpoint.Map(app);

            // Web forms (create/delete: authenticated; get/submit: anonymous)
            CreateWebFormEndpoint.Map(app);
            DeleteWebFormEndpoint.Map(app);
            GetWebFormPageEndpoint.Map(app);
            SubmitWebFormEndpoint.Map(app);
        }

        private void MapPipelineEndpoints()
        {
            // Workflow rules (PIPE-04)
            CreateWorkflowRuleEndpoint.Map(app);
            ListWorkflowRulesEndpoint.Map(app);
            UpdateWorkflowRuleEndpoint.Map(app);
        }

        private void MapReportEndpoints()
        {
            // Dashboard reports (Phase 05)
            GetPipelineDashboardEndpoint.Map(app);         // REPT-01: pipeline overview
            GetConversionDashboardEndpoint.Map(app);       // REPT-02: conversion rates
            GetAgentPerformanceDashboardEndpoint.Map(app); // REPT-03: agent performance
        }

        private void MapActivityEndpoints()
        {
            // Activity timeline (ACTV-01)
            GetLeadActivityTimelineEndpoint.Map(app);
            AddLeadNoteEndpoint.Map(app);
        }

        private void MapRecipeEndpoints()
        {
            // Public read endpoints (anonymous — required for signup flow, D-10)
            var publicRecipes = app.MapGroup("/api/recipes")
                .WithTags("Recipes")
                .AllowAnonymous();
            publicRecipes.MapEndpoint<RecipeListEndpoint>();
            publicRecipes.MapEndpoint<RecipePreviewEndpoint>();

            // Admin write endpoints (require authorization, D-11)
            var adminRecipes = app.MapGroup("/api/recipes")
                .WithTags("Platform Admin")
                .RequireAuthorization();
            adminRecipes.MapEndpoint<CreateRecipeEndpoint>();
            adminRecipes.MapEndpoint<UpdateRecipeEndpoint>();
            adminRecipes.MapEndpoint<DeactivateRecipeEndpoint>();
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
