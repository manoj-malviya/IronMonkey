namespace IronMonkey.ApiService.Features.Activity;

public interface IActivityTrackingService
{
    /// <summary>
    /// Adds a manually written note to a lead's activity timeline.
    /// All other events are captured automatically by ActivityChangeInterceptor.
    /// </summary>
    Task AddNoteAsync(Guid tenantId, string connectionString, Guid leadId, Guid actorId, string noteContent, CancellationToken cancellationToken = default);
}
