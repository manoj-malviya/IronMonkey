using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Reports.Dashboard;

/// <summary>
/// Tenant-wide recent activity plus recent conversions, for the dashboard's two feed
/// panels.
///
/// Unlike the per-subject timeline (<c>/api/activity/{subjectType}/{subjectId}</c>) this
/// is cross-subject and capped to a short window, so it answers "what has been happening
/// here lately" without the caller knowing which record to ask about.
/// </summary>
public class GetDashboardActivityEndpoint : IEndpoint
{
    /// <summary>Rows per feed. Both feeds are a glance, not a browsable log.</summary>
    private const int FeedLimit = 8;

    public static void Map(IEndpointRouteBuilder app) => app
        .MapGet("/api/dashboard/activity", Handle)
        .WithSummary("Recent tenant activity and recent lead conversions")
        .WithTags("Dashboard")
        .RequireAuthorization();

    public record ActivityFeedItem(
        Guid Id,
        string EventType,
        string SubjectType,
        Guid SubjectId,
        string? ActorName,
        DateTime OccurredAt);

    public record RecentConversionItem(
        Guid LeadId,
        string LeadName,
        Guid? OpportunityId,
        string? OpportunityTitle,
        decimal? Amount,
        DateTime ConvertedAt);

    public record DashboardActivityResponse(
        DateTime RangeFrom,
        DateTime RangeTo,
        string Preset,
        List<ActivityFeedItem> RecentActivity,
        List<RecentConversionItem> RecentConversions,
        int ConversionCount,
        DateTime GeneratedAt);

    internal static async Task<Ok<DashboardActivityResponse>> Handle(
        string? preset,
        DateTime? from,
        DateTime? to,
        ITenantService tenantService,
        ITenantDbContextFactory dbContextFactory,
        CancellationToken cancellationToken)
    {
        var range = DashboardDateRange.Resolve(preset, from, to);

        var tenantId = tenantService.GetCurrentTenantId();
        var connectionString = await tenantService.GetConnectionStringAsync(cancellationToken);
        await using var db = dbContextFactory.CreateForTenant(connectionString, tenantId);

        var recentActivity = await db.ActivityLogs
            .Where(a => a.CreatedAt >= range.From && a.CreatedAt < range.ToExclusive)
            .OrderByDescending(a => a.CreatedAt)
            .Take(FeedLimit)
            .Select(a => new ActivityFeedItem(
                a.Id,
                a.EventType,
                a.SubjectType,
                a.SubjectId,
                a.Actor != null ? a.Actor.Name : null,
                a.CreatedAt))
            .ToListAsync(cancellationToken);

        // Conversion time is approximated by UpdatedAt: Lead.Convert() mutates the row, so
        // the save that records the conversion is also the one that stamps UpdatedAt. A later
        // edit does move it, which is acceptable for a "recent" feed and avoids adding a
        // dedicated ConvertedAt column and migration for a display-only field.
        var convertedQuery = db.Leads
            .Where(l => l.IsConverted
                        && l.UpdatedAt >= range.From
                        && l.UpdatedAt < range.ToExclusive);

        var conversionCount = await convertedQuery.CountAsync(cancellationToken);

        // Left-joined against opportunities: a lead can be converted without one (the
        // contact-only path), and those conversions must still appear in the feed.
        var recentConversions = await convertedQuery
            .OrderByDescending(l => l.UpdatedAt)
            .Take(FeedLimit)
            .Select(l => new
            {
                l.Id,
                l.FirstName,
                l.LastName,
                l.ConvertedOpportunityId,
                l.UpdatedAt,
                Opportunity = db.Opportunities
                    .Where(o => o.Id == l.ConvertedOpportunityId)
                    .Select(o => new { o.Title, o.Amount })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new DashboardActivityResponse(
            range.From,
            range.ToInclusive,
            range.Preset,
            recentActivity,
            recentConversions.Select(c => new RecentConversionItem(
                c.Id,
                FormatName(c.FirstName, c.LastName),
                c.ConvertedOpportunityId,
                c.Opportunity?.Title,
                c.Opportunity?.Amount,
                c.UpdatedAt)).ToList(),
            conversionCount,
            DateTime.UtcNow));
    }

    private static string FormatName(string first, string last)
    {
        var name = $"{first} {last}".Trim();
        return string.IsNullOrEmpty(name) ? "(unnamed)" : name;
    }
}
