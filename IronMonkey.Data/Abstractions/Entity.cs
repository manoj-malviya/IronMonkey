namespace IronMonkey.Data.Abstractions;

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected Entity(Guid id, Guid organizationId)
    {
        Id = id;
        OrganizationId = organizationId;
    }

    protected Entity()
    {
    }

    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; } // Added for multi-tenancy

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool IsDeleted { get; set; }

    public IReadOnlyList<IDomainEvent> GetDomainEvents()
    {
        return _domainEvents.ToList();
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }
}