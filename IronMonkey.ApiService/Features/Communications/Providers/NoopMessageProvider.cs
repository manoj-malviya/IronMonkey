using IronMonkey.Data.Communications;

namespace IronMonkey.ApiService.Features.Communications.Providers;

/// <summary>
/// Records a send without performing one, for local development and tests.
///
/// It reports <c>IsConfigured == true</c> and returns a real acceptance with a synthetic
/// message id, so the whole pipeline — queueing, status transitions, timeline rendering,
/// idempotency — exercises its real paths without a network call or a credential. That is
/// what lets the test suite cover delivery semantics while guaranteeing no customer is ever
/// actually messaged from a test run.
///
/// It logs at Information so a developer can see what would have gone out. It does not log
/// the body: a body is customer PII, and a local log file is not where it belongs.
/// </summary>
public sealed class NoopMessageProvider(MessageChannel channel, ILogger<NoopMessageProvider> logger)
    : IMessageProvider
{
    public MessageChannel Channel { get; } = channel;

    public string Name => "Noop";

    public bool IsConfigured => true;

    public string FromAddress => Channel == MessageChannel.Email
        ? "no-reply@localhost"
        : "+10000000000";

    public Task<SendResult> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Noop provider accepted a {Channel} message to {Recipient} (subject {Subject}, {Length} chars, idempotency {Key}) — nothing was sent",
            request.Channel,
            IronMonkey.Data.Workflow.WorkflowDiagnosticRedactor.MaskEmail(request.To) ?? "***",
            request.Subject,
            request.Body.Length,
            request.IdempotencyKey);

        // Derived from the idempotency key rather than random, so a replayed send in a test
        // produces a stable id and duplicate-detection assertions are meaningful.
        return Task.FromResult(SendResult.Success($"noop-{request.IdempotencyKey}"));
    }
}
