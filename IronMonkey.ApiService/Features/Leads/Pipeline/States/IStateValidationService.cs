namespace IronMonkey.ApiService.Features.Leads.Pipeline.States;

public interface IStateValidationService
{
    /// <summary>
    /// Returns null if transition is allowed, or an error string listing allowed stages if denied.
    /// </summary>
    Task<string?> ValidateTransitionAsync(
        Guid tenantId, string connectionString, Guid leadId, Guid targetStageId,
        CancellationToken cancellationToken);
}
