using Microsoft.AspNetCore.Authentication.JwtBearer;
using IronMonkey.ApiService.Authentication.Endpoints;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.ApiService.Features.Leads;
using IronMonkey.ApiService.Features.Leads.CustomFields;
using IronMonkey.ApiService.Features.Leads.PipelineStages;
using IronMonkey.ApiService.Features.Pipelines;
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
using IronMonkey.ApiService.Features.UserManagement.Invitations;
using IronMonkey.ApiService.Features.RoleManagement;
using IronMonkey.ApiService.Features.Contacts;
using IronMonkey.ApiService.Features.Opportunities;
using IronMonkey.ApiService.Features.Opportunities.Stages;
using IronMonkey.ApiService.Features.Communications.Endpoints;
using IronMonkey.ApiService.Features.Communications.Webhooks;
using IronMonkey.ApiService.Features.Presentation;
using IronMonkey.ApiService.Features.Onboarding;
using IronMonkey.ApiService.Features.Commerce;
using IronMonkey.ApiService.Features.Quotes;
using IronMonkey.ApiService.Features.Reports.Revenue;
using IronMonkey.ApiService.Features.Insights;

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
        endpoints.MapNamedPipelineEndpoints();
        endpoints.MapPresentationEndpoints();
        endpoints.MapOnboardingEndpoints();
        endpoints.MapCommunicationEndpoints();
        endpoints.MapCommerceEndpoints();
        endpoints.MapInsightEndpoints();
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
                .MapEndpoint<ReactivateUserEndpoint>()
                .MapEndpoint<ResetPasswordEndpoint>()
                .MapEndpoint<ListUserAuditEndpoint>()
                // Invitation lifecycle. Each carries its own permission requirement, so the
                // group's bare RequireAuthorization is a floor, not the whole gate.
                .MapEndpoint<InviteTeamMemberEndpoint>()
                .MapEndpoint<ListInvitationsEndpoint>()
                .MapEndpoint<ResendInvitationEndpoint>()
                .MapEndpoint<RevokeInvitationEndpoint>();

            // Acceptance is anonymous by necessity: the invitee has no account yet, so the
            // token IS the authentication. It sits outside the authenticated group above,
            // which would otherwise 401 the very person the link was sent to.
            endpoints.MapPublicGroup()
                .MapEndpoint<AcceptInvitationEndpoint>()
                .MapEndpoint<PreviewInvitationEndpoint>();
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
                .MapEndpoint<DeleteRoleEndpoint>()
                .MapEndpoint<GetRoleScopesEndpoint>()
                .MapEndpoint<SetRoleScopesEndpoint>()
                .MapEndpoint<TeamEndpoints>();

            // Assignment pickers: any tenant user may list who records can be assigned to,
            // without users:read — routing and assignment must reach users whose records the
            // caller cannot see.
            AssignableUsersEndpoint.Map(app);
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
            ConfigureRoutingEndpoint.Map(app);
            GetRoutingConfigEndpoint.Map(app);
            ConfigureTransitionsEndpoint.Map(app);
            ListTransitionsEndpoint.Map(app);

            // Lead records and everything that reads or changes them: leads:read for GETs,
            // leads:write for the rest. Record visibility is enforced separately, in the
            // TenantDbContext query filter, so these gates decide "may this user use leads at
            // all" and the filter decides "which leads". Delete is gated on leads:write rather
            // than leads:delete, which no tenant role holds — requiring it would remove a
            // capability every Admin has today.
            var leads = app.MapGroup(string.Empty)
                .RequireRecordPermissions(PermissionConstants.LeadsRead, PermissionConstants.LeadsWrite);
            CreateLeadEndpoint.Map(leads);
            CheckDuplicatesEndpoint.Map(leads);
            MergeLeadsEndpoint.Map(leads);
            CreateTaskEndpoint.Map(leads);
            ListTasksEndpoint.Map(leads);
            UpdateTaskEndpoint.Map(leads);
            GetKanbanBoardEndpoint.Map(leads);
            MoveLeadEndpoint.Map(leads);

            // Lead CRUD. CreateLeadEndpoint already existed; without these the leads the
            // API could create were unreachable — nothing could list, open or edit them.
            ListLeadsEndpoint.Map(leads);
            GetLeadEndpoint.Map(leads);
            UpdateLeadEndpoint.Map(leads);
            DeleteLeadEndpoint.Map(leads);
            ConvertLeadEndpoint.Map(leads);
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

        private void MapOnboardingEndpoints()
        {
            // First-run setup status for the tenant CRM dashboard. Both are plainly
            // authorized: every member of the tenant lands on /admin, and the payload holds
            // only counts of the caller's own tenant plus their own name and role. The
            // tenant and user both come from the claims, so neither endpoint can be pointed
            // at another tenant or another user.
            GetOnboardingStatusEndpoint.Map(app);
            SetOnboardingDismissalEndpoint.Map(app);
        }

        private void MapContactEndpoints()
        {
            var contacts = app.MapGroup(string.Empty)
                .RequireRecordPermissions(PermissionConstants.ContactsRead, PermissionConstants.ContactsWrite);
            ListContactsEndpoint.Map(contacts);
            GetContactEndpoint.Map(contacts);
            CreateContactEndpoint.Map(contacts);
            UpdateContactEndpoint.Map(contacts);
            DeleteContactEndpoint.Map(contacts);
            RecordOwnerEndpoints.MapContact(contacts);
        }

        private void MapOpportunityEndpoints()
        {
            var opportunities = app.MapGroup(string.Empty)
                .RequireRecordPermissions(PermissionConstants.OpportunitiesRead, PermissionConstants.OpportunitiesWrite);
            ListOpportunitiesEndpoint.Map(opportunities);
            GetOpportunityEndpoint.Map(opportunities);
            CreateOpportunityEndpoint.Map(opportunities);
            UpdateOpportunityEndpoint.Map(opportunities);
            DeleteOpportunityEndpoint.Map(opportunities);
            RecordOwnerEndpoints.MapOpportunity(opportunities);

            // Opportunity stages, mirroring the lead pipeline-stage endpoints rule for rule.
            ListOpportunityStagesEndpoint.Map(app);
            CreateOpportunityStageEndpoint.Map(app);
            UpdateOpportunityStageEndpoint.Map(app);
            DeleteOpportunityStageEndpoint.Map(app);
            ReorderOpportunityStagesEndpoint.Map(app);
            GetOpportunityStageImpactEndpoint.Map(app);
        }

        /// <summary>
        /// Named pipelines: a tenant may run more than one lead funnel and more than one deal
        /// funnel, each with its own stages, fields, rules and routing.
        ///
        /// These are ordinary tenant endpoints under /api, not /admin/* — a pipeline is the
        /// tenant's own configuration, like its stages and custom fields, and not platform
        /// catalog data.
        /// </summary>
        private void MapNamedPipelineEndpoints()
        {
            ListPipelinesEndpoint.Map(app);
            CreatePipelineEndpoint.Map(app);
            UpdatePipelineEndpoint.Map(app);
            DeletePipelineEndpoint.Map(app);
            GetPipelineImpactEndpoint.Map(app);

            // Moving one record between pipelines. Registers routes on both /api/leads and
            // /api/opportunities, since the operation is the same for either.
            MoveRecordPipelineEndpoint.Map(app);
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

        /// <summary>
        /// Product catalog, price lists, opportunity line items and quotes. Catalog, price and quote
        /// settings writes need catalog:write; quote approval needs quotes:approve. The /q/* customer
        /// page is anonymous by design and secured by its share token — see
        /// <see cref="PublicQuoteEndpoints"/>.
        /// </summary>
        private void MapCommerceEndpoints()
        {
            ListProductsEndpoint.Map(app);
            GetProductEndpoint.Map(app);
            CreateProductEndpoint.Map(app);
            UpdateProductEndpoint.Map(app);
            AddProductPriceEndpoint.Map(app);

            ListPriceListsEndpoint.Map(app);
            CreatePriceListEndpoint.Map(app);
            UpdatePriceListEndpoint.Map(app);

            // Deal lines and quotes are part of the opportunity record.
            var deals = app.MapGroup(string.Empty)
                .RequireRecordPermissions(PermissionConstants.OpportunitiesRead, PermissionConstants.OpportunitiesWrite);
            GetDealEconomicsEndpoint.Map(deals);
            AddLineItemEndpoint.Map(deals);
            UpdateLineItemEndpoint.Map(deals);
            RemoveLineItemEndpoint.Map(deals);
            SetDealEconomicsEndpoint.Map(deals);

            ListQuotesEndpoint.Map(deals);
            GetQuoteEndpoint.Map(deals);
            GetQuoteDocumentEndpoint.Map(deals);
            CreateQuoteEndpoint.Map(deals);
            UpdateQuoteEndpoint.Map(deals);
            QuoteActionEndpoints.Map(deals);
            SendQuoteEndpoint.Map(deals);
            RespondQuoteEndpoint.Map(deals);
            QuoteShareLinkEndpoints.Map(deals);
            QuoteSettingsEndpoints.Map(app);

            PublicQuoteEndpoints.Map(app);
        }

        /// <summary>
        /// Search, saved views, the report builder and exports. Each checks the record type's
        /// read permission itself (the type is a route/body value, so a group gate cannot), and
        /// all of them apply record visibility in their SQL. Export also needs data:export.
        /// </summary>
        private void MapInsightEndpoints()
        {
            SearchEndpoint.Map(app);
            InsightQueryEndpoints.Map(app);
            SavedViewEndpoints.Map(app);
            ReportBuilderEndpoints.Map(app);
            ExportEndpoints.Map(app);
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

            // Line-item revenue: by product, category, recurring vs one-off, discounts.
            GetRevenueReportEndpoint.Map(app);
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
