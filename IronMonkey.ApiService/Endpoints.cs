using Microsoft.AspNetCore.Authentication.JwtBearer;
using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.ApiService.Common;
using IronMonkey.Common.Auth;
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
using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.ApiService.Features.Reports.Pipeline;
using IronMonkey.ApiService.Features.Reports.Conversion;
using IronMonkey.ApiService.Features.Reports.Performance;
using IronMonkey.ApiService.Features.Reports.Dashboard;
using IronMonkey.ApiService.Features.Recipes;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using IronMonkey.Data.Entities;
using IronMonkey.ApiService.Features.UserManagement;
using IronMonkey.ApiService.Features.RoleManagement;
using IronMonkey.ApiService.Features.Contacts;
using IronMonkey.ApiService.Features.Opportunities;
using IronMonkey.ApiService.Features.Communications.Endpoints;
using IronMonkey.ApiService.Features.Communications.Webhooks;
using IronMonkey.ApiService.Features.Presentation;

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
        endpoints.MapRoleManagementEndpoints();
        endpoints.MapPlatformAdminEndpoints();
        endpoints.MapLeadsEndpoints();
        endpoints.MapIngestionEndpoints();
        endpoints.MapPipelineEndpoints();
        endpoints.MapReportEndpoints();
        endpoints.MapActivityEndpoints();
        endpoints.MapRecipeEndpoints();
        endpoints.MapContactEndpoints();
        endpoints.MapOpportunityEndpoints();
        endpoints.MapPresentationEndpoints();
        endpoints.MapCommunicationEndpoints();
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
            // Namespaced under /admin: each endpoint below declares a route relative
            // to it (e.g. "/signup/{id}/approve" -> "/admin/signup/{id}/approve"),
            // which keeps the admin surface distinct from the public POST /auth/signup.
            var endpoints = app.MapGroup("/admin")
                .WithTags("Platform Admin")
                .RequireAuthorization(PermissionConstants.AdminAccess);

            endpoints.MapEndpoint<ApproveTenantEndpoint>();
            endpoints.MapEndpoint<RejectTenantEndpoint>();
            endpoints.MapEndpoint<ListSignupRequestsEndpoint>();
            endpoints.MapEndpoint<GetSignupRequestEndpoint>();
            endpoints.MapEndpoint<ProvisionTenantEndpoint>();
            endpoints.MapEndpoint<MigrateAllTenantsEndpoint>();
            endpoints.MapEndpoint<ListTenantsEndpoint>();
            endpoints.MapEndpoint<PlatformStatsEndpoint>();
            endpoints.MapEndpoint<ImpersonateTenantEndpoint>();
        }

        private void MapUserEndpoints()
        {
            // CreateUser is superseded by CreateTenantUserEndpoint (see
            // MapUserManagementEndpoints). Both declare POST /users, so registering both
            // makes every request to that route fail with AmbiguousMatchException. The
            // legacy one also targets the obsolete AppDbContext, which points at the
            // central database where User/Role do not exist, so it could not succeed.
            // Left registered-but-unused code out rather than deleting the type.
        }

        private void MapTenantEndpoints()
        {
            var endpoints = app.MapGroup(string.Empty)
                .WithTags("Tenant");

            endpoints.MapPublicGroup()
                .MapEndpoint<CreateTenant>();
        }

        private void MapUserManagementEndpoints()
        {
            var endpoints = app.MapGroup(string.Empty)
                .WithTags("User Management");

            // Existing public endpoints (role/permission management).
            //
            // ListRoles (GET /roles) and ListRolePermissions are deliberately NOT registered:
            // both query the obsolete AppDbContext, which points at the central database where
            // the tenant-scoped Roles/Permissions tables do not exist, so both return 500.
            // Tenant roles are served by ListTenantRolesEndpoint (GET /users/roles) below.
            // Left registered-but-unused code out rather than deleting the types, matching
            // how CreateUser was handled in MapUserEndpoints.
            // CreateRole, CreatePermission and AttachPermissionsToRole are deliberately NOT
            // registered: all three target the obsolete AppDbContext, which points at the
            // central database where the tenant-scoped Roles/Permissions tables do not exist,
            // so every call returned 500. They are superseded by the tenant-scoped endpoints
            // in MapRoleManagementEndpoints below. Left registered-but-unused code out rather
            // than deleting the types, matching how CreateUser and ListRoles were handled.

            // Tenant-scoped user management endpoints (require auth)
            var userEndpoints = endpoints.MapGroup(string.Empty)
                .RequireAuthorization();
            userEndpoints
                .MapEndpoint<ListUsersEndpoint>()
                // Replaces the legacy ListRoles (GET /roles), which targets the obsolete
                // AppDbContext against the central DB and returns 500. Registered before
                // GetUserEndpoint for clarity; the {id:guid} constraint keeps them distinct.
                .MapEndpoint<ListTenantRolesEndpoint>()
                .MapEndpoint<GetUserEndpoint>()
                .MapEndpoint<CreateTenantUserEndpoint>()
                .MapEndpoint<UpdateUserEndpoint>()
                .MapEndpoint<DeactivateUserEndpoint>()
                .MapEndpoint<ResetPasswordEndpoint>();
        }

        private void MapRoleManagementEndpoints()
        {
            var endpoints = app.MapGroup(string.Empty)
                .WithTags("Role Management")
                .RequireAuthorization();

            endpoints
                .MapEndpoint<ListPermissionsEndpoint>()
                .MapEndpoint<CreateRoleEndpoint>()
                .MapEndpoint<UpdateRoleEndpoint>()
                .MapEndpoint<DeleteRoleEndpoint>();
        }

        private void MapLeadsEndpoints()
        {
            CreateCustomFieldEndpoint.Map(app);
            ListCustomFieldsEndpoint.Map(app);
            UpdateCustomFieldEndpoint.Map(app);
            DeleteCustomFieldEndpoint.Map(app);
            GetCustomFieldImpactEndpoint.Map(app);
            RestoreCustomFieldEndpoint.Map(app);
            CreatePipelineStageEndpoint.Map(app);
            ListPipelineStagesEndpoint.Map(app);
            UpdatePipelineStageEndpoint.Map(app);
            DeletePipelineStageEndpoint.Map(app);
            ReorderPipelineStagesEndpoint.Map(app);
            GetPipelineStageImpactEndpoint.Map(app);
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

            // Lead CRUD. CreateLeadEndpoint already existed; without these the leads the
            // API could create were unreachable — nothing could list, open or edit them.
            ListLeadsEndpoint.Map(app);
            GetLeadEndpoint.Map(app);
            UpdateLeadEndpoint.Map(app);
            DeleteLeadEndpoint.Map(app);
            ConvertLeadEndpoint.Map(app);
        }

        private void MapCommunicationEndpoints()
        {
            // Messaging. Sending is gated on messages:send and reading on messages:read, both
            // separate from leads:* — sending is outward-facing and a message body is the most
            // sensitive data in the CRM.
            SendMessageEndpoint.Map(app);
            ListMessagesEndpoint.Map(app);
            ListChannelsEndpoint.Map(app);
            AttachMessageEndpoint.Map(app);
            GetWebhookUrlsEndpoint.Map(app);

            GetMessagingPolicyEndpoint.Map(app);
            UpdateMessagingPolicyEndpoint.Map(app);

            ListMessageTemplatesEndpoint.Map(app);
            CreateMessageTemplateEndpoint.Map(app);
            UpdateMessageTemplateEndpoint.Map(app);

            ListConsentEndpoint.Map(app);
            UpdateConsentEndpoint.Map(app);

            // Provider callbacks. Anonymous by necessity — a provider has no session — so the
            // signature authenticates and the routing token in the path selects the tenant.
            // Neither reads a tenant id from the request body.
            TwilioWebhookEndpoint.Map(app);
            GenericInboundWebhookEndpoint.Map(app);
        }

        private void MapPresentationEndpoints()
        {
            // Tenant terminology, locale and branding. Read is available to any authenticated
            // tenant user because every page needs the labels; the write is gated on
            // settings:write, since it changes what the whole tenant sees.
            GetTenantPresentationEndpoint.Map(app);
            UpdateTenantPresentationEndpoint.Map(app);
        }

        private void MapContactEndpoints()
        {
            ListContactsEndpoint.Map(app);
            GetContactEndpoint.Map(app);
            CreateContactEndpoint.Map(app);
            UpdateContactEndpoint.Map(app);
            DeleteContactEndpoint.Map(app);
        }

        private void MapOpportunityEndpoints()
        {
            ListOpportunitiesEndpoint.Map(app);
            GetOpportunityEndpoint.Map(app);
            CreateOpportunityEndpoint.Map(app);
            UpdateOpportunityEndpoint.Map(app);
            DeleteOpportunityEndpoint.Map(app);
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
            DeleteWorkflowRuleEndpoint.Map(app);

            // Workflow execution history. Each declares RequireAuthorization(workflow:logs:read)
            // itself rather than inheriting a group, matching how the rest of the tenant
            // surface is registered here.
            //
            // The run-summary route is registered before the {id:guid} detail route for
            // clarity; the route constraint already keeps "run-summary" from matching it.
            GetWorkflowRuleRunSummaryEndpoint.Map(app);
            ListWorkflowExecutionsEndpoint.Map(app);
            GetWorkflowExecutionEndpoint.Map(app);
        }

        private void MapReportEndpoints()
        {
            // Dashboard reports (Phase 05)
            GetPipelineDashboardEndpoint.Map(app);         // REPT-01: pipeline overview
            GetConversionDashboardEndpoint.Map(app);       // REPT-02: conversion rates
            GetAgentPerformanceDashboardEndpoint.Map(app); // REPT-03: agent performance

            // Tenant CRM dashboard. One endpoint per widget rather than one combined
            // payload, so a widget that fails degrades on its own instead of blanking
            // the whole page.
            GetDashboardSummaryEndpoint.Map(app);
            GetDashboardOpportunitiesEndpoint.Map(app);
            GetDashboardAttentionEndpoint.Map(app);
            GetDashboardActivityEndpoint.Map(app);
        }

        private void MapActivityEndpoints()
        {
            // Activity timeline (ACTV-01)
            GetLeadActivityTimelineEndpoint.Map(app);
            AddLeadNoteEndpoint.Map(app);

            // Generic subject timelines (lead / contact / opportunity)
            GetActivityTimelineEndpoint.Map(app);
            AddActivityNoteEndpoint.Map(app);
        }

        private void MapRecipeEndpoints()
        {
            // Public read endpoints (anonymous — required for signup flow, D-10)
            var publicRecipes = app.MapGroup(string.Empty)
                .WithTags("Recipes")
                .AllowAnonymous();
            publicRecipes.MapEndpoint<RecipeListEndpoint>();
            publicRecipes.MapEndpoint<RecipePreviewEndpoint>();

            // Platform-admin write endpoints (D-11). Recipes are central-DB catalog data
            // shared by every tenant, so a bare .RequireAuthorization() is not enough: it
            // would let any authenticated tenant user rewrite the templates every other
            // tenant's signup reads from. admin:access is platform-only by construction —
            // a tenant Admin is deliberately denied it.
            var adminRecipes = app.MapGroup(string.Empty)
                .WithTags("Platform Admin")
                .RequireAuthorization(PermissionConstants.AdminAccess);
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
