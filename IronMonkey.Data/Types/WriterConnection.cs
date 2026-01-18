using System;
using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Types;

public class WriterConnection : Entity
{
    private WriterConnection() { } // For EF Core

    public static WriterConnection Create(Guid writerId, Guid publisherId)
    {
        var connection = new WriterConnection
        {
            Id = Guid.NewGuid(),
            WriterId = writerId,
            PublisherId = publisherId,
            Status = ConnectionStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        
        connection.RaiseDomainEvent(new WriterConnectionCreatedEvent(connection.Id, connection.WriterId, connection.PublisherId));
        return connection;
    }

    public Guid Id { get; private set; }
    public Guid WriterId { get; private set; }
    public Guid PublisherId { get; private set; }
    public ConnectionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? RejectedAt { get; private set; }

    public void Accept()
    {
        if (Status != ConnectionStatus.Pending)
            throw new InvalidOperationException("Can only accept pending connections");
            
        Status = ConnectionStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        RaiseDomainEvent(new WriterConnectionAcceptedEvent(Id, WriterId, PublisherId));
    }

    public void Reject()
    {
        if (Status != ConnectionStatus.Pending)
            throw new InvalidOperationException("Can only reject pending connections");
            
        Status = ConnectionStatus.Rejected;
        RejectedAt = DateTime.UtcNow;
        RaiseDomainEvent(new WriterConnectionRejectedEvent(Id, WriterId, PublisherId));
    }
}

public record WriterConnectionCreatedEvent(Guid ConnectionId, Guid WriterId, Guid PublisherId) : IDomainEvent;
public record WriterConnectionAcceptedEvent(Guid ConnectionId, Guid WriterId, Guid PublisherId) : IDomainEvent;
public record WriterConnectionRejectedEvent(Guid ConnectionId, Guid WriterId, Guid PublisherId) : IDomainEvent;

public enum ConnectionStatus
{
    Pending,
    Accepted,
    Rejected
} 