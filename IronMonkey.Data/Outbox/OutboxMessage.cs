namespace IronMonkey.Data.Outbox;

public sealed class OutboxMessage
{
    public OutboxMessage(Guid id, DateTime occurredOnUtc, string type, string content)
    {
        Id = id;
        OccurredOnUtc = occurredOnUtc;
        Content = content;
        Type = type;
    }

    public Guid Id { get; init; }

    public DateTime OccurredOnUtc { get; init; }

    public string Type { get; init; }

    public string Content { get; init; }

    public DateTime? ProcessedOnUtc { get; private set; }

    public string? Error { get; private set; }

    /// <summary>
    /// Whether this message has been published to its domain event handlers.
    /// </summary>
    public bool Published { get; private set; }

    /// <summary>
    /// Marks the message as published. Sets ProcessedOnUtc and Published = true.
    /// </summary>
    public void MarkAsPublished()
    {
        Published = true;
        ProcessedOnUtc = DateTime.UtcNow;
    }
}
