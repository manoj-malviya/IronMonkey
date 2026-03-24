using Microsoft.AspNetCore.Http.HttpResults;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;

public class SubmitWebFormEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/forms/{token}/submit", Handle)
        .WithSummary("Accept web form submission")
        .WithTags("Web Forms")
        .AllowAnonymous()
        .DisableAntiforgery()  // Public endpoint, form-data POST
        .RequireRateLimiting("form-token-limit");

    // Fields mapped from HTML form inputs
    public record SubmissionRequest(
        string? FirstName,
        string? LastName,
        string? Email,
        string? Mobile,
        string? Website = null  // Honeypot — must be null/empty for real humans
    );

    private static async Task<Results<ContentHttpResult, BadRequest>> Handle(
        string token,
        [Microsoft.AspNetCore.Mvc.FromForm] SubmissionRequest request,
        IWebFormService webFormService,
        ITenantDbContextFactory dbContextFactory,
        IDuplicateDetectionService duplicateDetection,
        CancellationToken cancellationToken)
    {
        // 1. Honeypot check — if "Website" field is filled, this is a bot. Silently reject per D-11.
        if (!string.IsNullOrWhiteSpace(request.Website))
            return TypedResults.BadRequest();

        // 2. Validate token
        var form = await webFormService.GetByTokenAsync(token, cancellationToken);
        if (form == null)
            return TypedResults.BadRequest();

        // 3. Basic field validation
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Email))
            return TypedResults.BadRequest();

        // 4. Get tenant DB
        var connectionString = await webFormService.GetTenantConnectionStringAsync(
            form.TenantId, cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, form.TenantId);

        // 5. Create lead with Source=WebForm — always create, no warning to visitor per D-15
        var lead = Lead.Create(
            form.TenantId,
            request.FirstName,
            request.LastName,
            request.Mobile ?? string.Empty,
            request.Email,
            LeadSource.WebForm,
            form.DefaultPipelineStageId);

        // 6. Check duplicates — flag internally, visitor never sees this per D-15
        var duplicates = await duplicateDetection.FindCandidatesAsync(
            form.TenantId, request.Email, request.Mobile,
            $"{request.FirstName} {request.LastName}", cancellationToken);

        if (duplicates.Any())
            lead.MarkAsPotentialDuplicate(duplicates.First().LeadId);

        db.Leads.Add(lead);
        await db.SaveChangesAsync(cancellationToken);

        // 7. Post-submission: redirect or thank-you per D-12
        if (!string.IsNullOrWhiteSpace(form.PostSubmissionRedirectUrl))
            return TypedResults.Content(
                $"""<html><head><meta http-equiv="refresh" content="0;url={System.Web.HttpUtility.HtmlAttributeEncode(form.PostSubmissionRedirectUrl)}"></head></html>""",
                "text/html");

        return TypedResults.Content(
            """
<!DOCTYPE html>
<html><head><meta charset="UTF-8"><title>Thank You</title>
<style>body{font-family:system-ui,sans-serif;max-width:480px;margin:40px auto;text-align:center;padding:0 16px}</style>
</head><body><h1>Thank you!</h1><p>Your information has been submitted.</p></body></html>
""", "text/html");
    }
}
