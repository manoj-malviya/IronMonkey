using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.ApiService.Features.CustomFields;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads;

public class UpdateLeadEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/leads/{id:guid}", Handle)
        .WithSummary("Update a lead's details and pipeline stage")
        .WithTags("Leads")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(
        string FirstName, string LastName, string Mobile, string Email,
        string Source, Guid PipelineStageId, Guid? AssignedToUserId,
        Dictionary<string, object?>? CustomFields = null);

    public record Response(Guid Id, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required.");
            RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("A valid email address is required.");
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError, NotFound>> Handle(
        Guid id,
        Request request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        IWorkflowTriggerDispatcher workflowTriggers,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<LeadSource>(request.Source, ignoreCase: true, out var source))
            return new ValidationError("Invalid source value. Valid: Manual, Import, Api, WebForm");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);

        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var lead = await db.Leads.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
            return TypedResults.NotFound();

        var stageExists = await db.PipelineStages
            .AnyAsync(p => p.Id == request.PipelineStageId, cancellationToken);
        if (!stageExists)
            return new ValidationError("Invalid pipeline stage.");

        var definitions = await db.CustomFieldDefinitions
            .Where(f => f.AppliesTo == CustomFieldEntity.Lead)
            .ToListAsync(cancellationToken);

        var bound = CustomFieldValueBinder.Bind(definitions, request.CustomFields);
        if (!bound.IsValid)
            return new ValidationError(string.Join(" ", bound.Errors));

        var stageChanged = lead.PipelineStageId != request.PipelineStageId;

        lead.UpdateLeadInfo(request.FirstName, request.LastName, request.Mobile, request.Email, source);
        lead.MoveToPipelineStage(request.PipelineStageId);
        lead.AssignTo(request.AssignedToUserId);

        // Replace the whole bag rather than merging: a cleared field must actually clear.
        lead.CustomFields.Values.Clear();
        foreach (var (key, value) in bound.Values)
            lead.CustomFields.Set(key, value);

        // The converter compares by reference, so mutating the object in place is invisible
        // to change tracking unless the property is explicitly flagged.
        db.Entry(lead).Property(l => l.CustomFields).IsModified = true;

        await db.SaveChangesAsync(cancellationToken);

        workflowTriggers.Dispatch(tenantId, lead.Id, WorkflowTrigger.FieldChange);

        // A stage change is a distinct trigger so rules can watch it specifically.
        if (stageChanged)
            workflowTriggers.Dispatch(tenantId, lead.Id, WorkflowTrigger.StatusChange);

        return TypedResults.Ok(new Response(lead.Id, "Lead updated successfully."));
    }
}
