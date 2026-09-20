using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Features.Communications.Endpoints;

/// <summary>Times are local to the tenant's configured timezone, as "HH:mm".</summary>
public sealed record MessagingPolicyResponse(
    string? QuietHoursStart,
    string? QuietHoursEnd,
    List<string> QuietHoursChannels,
    int? MaxMessagesPerHour);

public sealed record SaveMessagingPolicyRequest(
    string? QuietHoursStart,
    string? QuietHoursEnd,
    List<string>? QuietHoursChannels,
    int? MaxMessagesPerHour);

public class GetMessagingPolicyEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/messaging-policy", Handle)
        .WithSummary("Get this tenant's sending hours and rate limit")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.MessagesRead);

    internal static async Task<Ok<MessagingPolicyResponse>> Handle(
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var policy = await db.MessagingPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        // No row means no restrictions, reported as empty rather than as a 404 — the UI needs
        // a shape to bind to either way.
        if (policy is null)
            return TypedResults.Ok(new MessagingPolicyResponse(null, null, [], null));

        var window = policy.ToWindow();

        return TypedResults.Ok(new MessagingPolicyResponse(
            policy.QuietHoursStart?.ToString("HH\\:mm"),
            policy.QuietHoursEnd?.ToString("HH\\:mm"),
            window?.Channels.Select(c => c.ToString()).ToList() ?? [],
            policy.MaxMessagesPerHour));
    }
}

public class UpdateMessagingPolicyEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/messaging-policy", Handle)
        .WithSummary("Set this tenant's sending hours and rate limit")
        .WithTags("Communications")
        .RequireAuthorization(PermissionConstants.SettingsWrite);

    internal static async Task<Results<Ok<MessagingPolicyResponse>, ValidationError>> Handle(
        SaveMessagingPolicyRequest request,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        TimeOnly? start = null;
        TimeOnly? end = null;

        // Both ends or neither. One alone would be an open-ended window that silences the
        // tenant indefinitely, so it is rejected here rather than quietly discarded.
        var hasStart = !string.IsNullOrWhiteSpace(request.QuietHoursStart);
        var hasEnd = !string.IsNullOrWhiteSpace(request.QuietHoursEnd);

        if (hasStart != hasEnd)
            return new ValidationError("Sending hours need both a start and an end time, or neither.");

        if (hasStart)
        {
            if (!TimeOnly.TryParse(request.QuietHoursStart, out var parsedStart))
                return new ValidationError($"\"{request.QuietHoursStart}\" is not a valid time.");

            if (!TimeOnly.TryParse(request.QuietHoursEnd, out var parsedEnd))
                return new ValidationError($"\"{request.QuietHoursEnd}\" is not a valid time.");

            if (parsedStart == parsedEnd)
                return new ValidationError("The start and end of the quiet window cannot be the same time.");

            start = parsedStart;
            end = parsedEnd;
        }

        var channels = new List<MessageChannel>();

        foreach (var name in request.QuietHoursChannels ?? [])
        {
            if (!MessageMapping.TryParseChannel(name, out var channel))
                return new ValidationError($"\"{name}\" is not a supported channel.");

            channels.Add(channel);
        }

        if (request.MaxMessagesPerHour is < 0)
            return new ValidationError("The hourly limit cannot be negative.");

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var policy = await db.MessagingPolicies.FirstOrDefaultAsync(cancellationToken);

        if (policy is null)
        {
            policy = MessagingPolicy.CreateDefault(tenantId);
            db.MessagingPolicies.Add(policy);
        }

        policy.SetQuietHours(start, end, channels);
        policy.SetRateLimit(request.MaxMessagesPerHour);

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new MessagingPolicyResponse(
            policy.QuietHoursStart?.ToString("HH\\:mm"),
            policy.QuietHoursEnd?.ToString("HH\\:mm"),
            policy.ToWindow()?.Channels.Select(c => c.ToString()).ToList() ?? [],
            policy.MaxMessagesPerHour));
    }
}
