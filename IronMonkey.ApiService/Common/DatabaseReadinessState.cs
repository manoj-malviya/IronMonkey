using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IronMonkey.ApiService.Common;

/// <summary>
/// Tracks whether central-database preparation (creation + migrations) has finished.
/// Database init runs after the server starts listening so that Aspire's health probe
/// is answerable while it works; this flag keeps /health reporting unhealthy until the
/// schema is actually usable, so dependent resources still wait for a real ready state.
/// </summary>
public sealed class DatabaseReadinessState
{
    private volatile bool _isReady;

    public bool IsReady => _isReady;

    public void MarkReady() => _isReady = true;
}

public sealed class DatabaseReadinessHealthCheck(DatabaseReadinessState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(state.IsReady
            ? HealthCheckResult.Healthy("Central database ready.")
            : HealthCheckResult.Unhealthy("Central database preparation in progress."));
}
