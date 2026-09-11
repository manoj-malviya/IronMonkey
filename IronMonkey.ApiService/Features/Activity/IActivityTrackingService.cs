namespace IronMonkey.ApiService.Features.Activity;

public interface IActivityTrackingService
{
    /// <summary>
    /// Adds a manually written note to a lead's activity timeline.
    /// All other events are captured automatically by ActivityChangeInterceptor.
    /// </summary>
    Task<Guid> AddNoteAsync(Guid tenantId, string connectionString, Guid leadId, Guid actorId, string noteContent, CancellationToken cancellationToken = default);

    /// <summary>Adds a note to any timeline subject — Lead, Contact or Opportunity.</summary>
    Task<Guid> AddNoteForAsync(Guid tenantId, string connectionString, string subjectType, Guid subjectId, Guid actorId, string noteContent, CancellationToken cancellationToken = default);
}
