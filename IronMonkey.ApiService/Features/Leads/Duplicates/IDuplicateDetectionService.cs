namespace IronMonkey.ApiService.Features.Leads.Duplicates;

public record DuplicateCandidate(Guid LeadId, string FullName, string Email, string Reason, int ConfidenceScore);

public interface IDuplicateDetectionService
{
    Task<List<DuplicateCandidate>> FindCandidatesAsync(
        Guid tenantId, string? email, string? phone, string? name, CancellationToken ct = default);
}
