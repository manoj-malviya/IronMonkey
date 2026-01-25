using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using IronMonkey.Common.Exceptions;
using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Outbox;
using IronMonkey.Data.Types;

namespace IronMonkey.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<WriterInvitation> WriterInvitations { get; set; }
    public DbSet<WriterConnection> WriterConnections { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            AddDomainEventsAsOutboxMessages();
            GenerateTimestamps();
            int result = await base.SaveChangesAsync(cancellationToken);

            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("Concurrency exception occurred.", ex);
        }
    }

    private void GenerateTimestamps()
    {
        var entries = ChangeTracker
            .Entries()
            .Where(e => e.Entity is Entity && (
                e.State == EntityState.Added
                || e.State == EntityState.Modified
                || e.State == EntityState.Deleted));

        foreach (var entityEntry in entries)
        {
            ((Entity)entityEntry.Entity).UpdatedAt = DateTime.Now;

            if (entityEntry.State == EntityState.Added)
            {
                ((Entity)entityEntry.Entity).CreatedAt = DateTime.Now;
            }

            if (entityEntry.State == EntityState.Deleted)
            {
                ((Entity)entityEntry.Entity).DeletedAt = DateTime.Now;
                ((Entity)entityEntry.Entity).IsDeleted = true;
            }
        }
    }
    
    private void AddDomainEventsAsOutboxMessages()
    {
        var outboxMessages = ChangeTracker
            .Entries<Entity>()
            .Select(entry => entry.Entity)
            .SelectMany(entity =>
            {
                IReadOnlyList<IDomainEvent> domainEvents = entity.GetDomainEvents();

                entity.ClearDomainEvents();

                return domainEvents;
            })
            .Select(domainEvent => new OutboxMessage(
                Guid.NewGuid(),
                DateTime.UtcNow,
                domainEvent.GetType().Name,
                JsonConvert.SerializeObject(domainEvent, JsonSerializerSettings)))
            .ToList();

        AddRange(outboxMessages);
    }
    
    private static readonly JsonSerializerSettings JsonSerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.All
    };
}