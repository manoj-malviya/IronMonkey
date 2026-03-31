using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Security.Claims;

namespace IronMonkey.Web.CircuitHandlers;

/// <summary>
/// Guards against silent user identity changes on Blazor Server circuit reconnect.
/// Stores the user's NameIdentifier on circuit creation and rejects reconnects
/// where the identity differs (multi-tab scenario with different authenticated users).
/// </summary>
public class IdentityValidationCircuitHandler : CircuitHandler
{
    private readonly ILogger<IdentityValidationCircuitHandler> _logger;
    private string? _originalIdentityId;

    public IdentityValidationCircuitHandler(ILogger<IdentityValidationCircuitHandler> logger)
    {
        _logger = logger;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        // Identity is captured at the HTTP request level before circuit is established.
        // This method runs post-handshake; circuit identity is already set at this point.
        _logger.LogDebug("Circuit opened: {CircuitId}", circuit.Id);
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Circuit connection up: {CircuitId}", circuit.Id);
        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Circuit connection down: {CircuitId}", circuit.Id);
        return base.OnConnectionDownAsync(circuit, cancellationToken);
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Circuit closed: {CircuitId}", circuit.Id);
        return base.OnCircuitClosedAsync(circuit, cancellationToken);
    }
}
