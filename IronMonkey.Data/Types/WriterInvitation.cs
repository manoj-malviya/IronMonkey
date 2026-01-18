using System;
using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Types;

public class WriterInvitation : Entity
{
    private WriterInvitation() { } // For EF Core

    public static WriterInvitation Create(string email, Guid publisherId, string? message = null)
    {
        var invitation = new WriterInvitation
        {
            Id = Guid.NewGuid(),
            Email = email,
            PublisherId = publisherId,
            Message = message,
            Status = InvitationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        
        invitation.RaiseDomainEvent(new WriterInvitationCreatedEvent(invitation.Id, invitation.Email, invitation.PublisherId, invitation.Message));
        return invitation;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; }
    public Guid PublisherId { get; private set; }
    public User Publisher { get; private set; }
    public string? Message { get; private set; }
    public InvitationStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }

    public void Accept()
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException("Can only accept pending invitations");
            
        Status = InvitationStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        RaiseDomainEvent(new WriterInvitationAcceptedEvent(Id, PublisherId));
    }

    public void Reject()
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException("Can only reject pending invitations");
            
        Status = InvitationStatus.Rejected;
        RaiseDomainEvent(new WriterInvitationRejectedEvent(Id, PublisherId));
    }

    public void Expire()
    {
        if (Status != InvitationStatus.Pending)
            throw new InvalidOperationException("Can only expire pending invitations");
            
        Status = InvitationStatus.Expired;
        ExpiresAt = DateTime.UtcNow;
        RaiseDomainEvent(new WriterInvitationExpiredEvent(Id, PublisherId));
    }
}

public record WriterInvitationCreatedEvent(Guid InvitationId, string Email, Guid PublisherId, string? Message) : IDomainEvent;
public record WriterInvitationAcceptedEvent(Guid InvitationId, Guid PublisherId) : IDomainEvent;
public record WriterInvitationRejectedEvent(Guid InvitationId, Guid PublisherId) : IDomainEvent;
public record WriterInvitationExpiredEvent(Guid InvitationId, Guid PublisherId) : IDomainEvent;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Rejected,
    Expired
} 