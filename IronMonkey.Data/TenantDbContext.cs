using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using IronMonkey.Common.Exceptions;
using IronMonkey.Data.Abstractions;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Outbox;

namespace IronMonkey.Data;

/// <summary>
/// DbContext for a specific tenant's isolated database.
/// All BaseTenantEntity types have global query filters enforcing TenantId.
/// Connection string is passed at construction — one instance per tenant per request.
/// </summary>
public class TenantDbContext : DbContext
{
    private readonly Guid _tenantId;

    public TenantDbContext(DbContextOptions<TenantDbContext> options, Guid tenantId)
        : base(options)
    {
        _tenantId = tenantId;
    }

    /// <summary>Exposes the tenant identity for interceptors that need to create tenant-scoped activity log entries.</summary>
    public Guid TenantId => _tenantId;

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<LeadMerge> LeadMerges => Set<LeadMerge>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<LeadTask> LeadTasks => Set<LeadTask>();
    public DbSet<WorkflowRule> WorkflowRules => Set<WorkflowRule>();
    public DbSet<StageTransition> StageTransitions => Set<StageTransition>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RoutingConfig> RoutingConfigs => Set<RoutingConfig>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<WorkflowExecutionLog> WorkflowExecutionLogs => Set<WorkflowExecutionLog>();
    public DbSet<WorkflowExecutionStep> WorkflowExecutionSteps => Set<WorkflowExecutionStep>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageConsent> MessageConsents => Set<MessageConsent>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<MessagingPolicy> MessagingPolicies => Set<MessagingPolicy>();
    public DbSet<TeamInvitation> TeamInvitations => Set<TeamInvitation>();
    public DbSet<UserAuditLog> UserAuditLogs => Set<UserAuditLog>();
    public DbSet<TenantOnboardingDismissal> TenantOnboardingDismissals => Set<TenantOnboardingDismissal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply all configurations from assembly (picks up IEntityTypeConfiguration<T> classes)
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenantDbContext).Assembly);

        // Global query filters — enforced on every query, cannot be accidentally skipped
        // These are the primary TNCY-02 enforcement mechanism
        modelBuilder.Entity<User>().HasQueryFilter(u => u.TenantId == _tenantId && !u.IsDeleted);
        modelBuilder.Entity<Lead>().HasQueryFilter(l => l.TenantId == _tenantId && !l.IsDeleted);
        modelBuilder.Entity<Contact>().HasQueryFilter(c => c.TenantId == _tenantId && !c.IsDeleted);
        modelBuilder.Entity<Opportunity>().HasQueryFilter(o => o.TenantId == _tenantId && !o.IsDeleted);
        modelBuilder.Entity<PipelineStage>().HasQueryFilter(p => p.TenantId == _tenantId && !p.IsDeleted);
        modelBuilder.Entity<CustomFieldDefinition>().HasQueryFilter(c => c.TenantId == _tenantId && !c.IsDeleted);
        modelBuilder.Entity<LeadMerge>().HasQueryFilter(m => m.TenantId == _tenantId);
        modelBuilder.Entity<ImportBatch>().HasQueryFilter(b => b.TenantId == _tenantId);
        modelBuilder.Entity<LeadTask>().HasQueryFilter(t => t.TenantId == _tenantId && !t.IsDeleted);
        modelBuilder.Entity<WorkflowRule>().HasQueryFilter(r => r.TenantId == _tenantId && !r.IsDeleted);
        modelBuilder.Entity<StageTransition>().HasQueryFilter(t => t.TenantId == _tenantId);
        modelBuilder.Entity<Notification>().HasQueryFilter(n => n.TenantId == _tenantId && !n.IsDeleted);
        modelBuilder.Entity<RoutingConfig>().HasQueryFilter(r => r.TenantId == _tenantId);
        modelBuilder.Entity<ActivityLog>().HasQueryFilter(a => a.TenantId == _tenantId);

        // Execution history is never soft-deleted — retention removes it outright — so the
        // filter is TenantId only. Steps carry their own filter rather than relying on the
        // parent's: a step loaded through Include inherits nothing, and the table is also
        // queried directly by the action-type filter.
        modelBuilder.Entity<WorkflowExecutionLog>().HasQueryFilter(l => l.TenantId == _tenantId);
        modelBuilder.Entity<WorkflowExecutionStep>().HasQueryFilter(s => s.TenantId == _tenantId);

        // Messages are conversation history and are never soft-deleted: a record of what was
        // said to a customer must survive the lead being removed, so the filter is TenantId
        // only. Consent is likewise permanent — an opt-out that could be soft-deleted would
        // silently become permission to message again.
        modelBuilder.Entity<Message>().HasQueryFilter(m => m.TenantId == _tenantId);
        modelBuilder.Entity<MessageConsent>().HasQueryFilter(c => c.TenantId == _tenantId);
        modelBuilder.Entity<MessageTemplate>().HasQueryFilter(t => t.TenantId == _tenantId && !t.IsDeleted);
        modelBuilder.Entity<MessagingPolicy>().HasQueryFilter(p => p.TenantId == _tenantId);

        // Invitations and the team audit trail are never soft-deleted: a revoked invitation
        // and a deactivation are both history that has to stay legible, so the filter is
        // TenantId only. This filter is also the tenant binding on the acceptance path —
        // a token issued by another tenant is simply not in scope, so it cannot be redeemed
        // here even if the plaintext were guessed.
        modelBuilder.Entity<TeamInvitation>().HasQueryFilter(i => i.TenantId == _tenantId);
        modelBuilder.Entity<UserAuditLog>().HasQueryFilter(a => a.TenantId == _tenantId);

        // A dismissal is a per-user UI preference and is never soft-deleted — reopening
        // clears DismissedAt on the same row — so the filter is TenantId only. Without it a
        // tenant could read (and toggle) another tenant's dismissal rows.
        modelBuilder.Entity<TenantOnboardingDismissal>().HasQueryFilter(d => d.TenantId == _tenantId);

        // ActivityLog JSONB columns and dashboard indexes
        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.Property(a => a.OldValues)
                .HasConversion(
                    v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => v == null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(v, (System.Text.Json.JsonSerializerOptions?)null))
                .HasColumnType("jsonb");

            entity.Property(a => a.NewValues)
                .HasConversion(
                    v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => v == null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(v, (System.Text.Json.JsonSerializerOptions?)null))
                .HasColumnType("jsonb");

            // Dashboard performance indexes per D-08
            entity.HasIndex(a => new { a.TenantId, a.LeadId }).HasDatabaseName("IX_ActivityLogs_TenantId_LeadId");
            entity.HasIndex(a => new { a.TenantId, a.EventType }).HasDatabaseName("IX_ActivityLogs_TenantId_EventType");
            entity.HasIndex(a => a.CreatedAt).HasDatabaseName("IX_ActivityLogs_CreatedAt");

            entity.Property(a => a.SubjectType).IsRequired().HasMaxLength(50).HasDefaultValue("Lead");

            // Actor is optional: system and background-job events carry Guid.Empty, which
            // matches no user. Convention made this a required FK, so EF built an INNER JOIN
            // against a User set that also has a soft-delete query filter — the required
            // navigation and the filtered principal are incompatible and every timeline read
            // threw. Optional makes it a LEFT JOIN and ActorName simply comes back null.
            entity.HasOne(a => a.Actor)
                .WithMany()
                .HasForeignKey(a => a.ActorId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            // The timeline query is exactly (tenant, subject) ordered by time.
            entity.HasIndex(a => new { a.TenantId, a.SubjectType, a.SubjectId, a.CreatedAt })
                .HasDatabaseName("IX_ActivityLogs_TenantId_Subject_CreatedAt");
        });

        // Dashboard performance indexes for Lead entity (per D-08)
        modelBuilder.Entity<Lead>(entity =>
        {
            entity.HasIndex(l => new { l.TenantId, l.PipelineStageId }).HasDatabaseName("IX_Leads_TenantId_PipelineStageId");
            entity.HasIndex(l => new { l.TenantId, l.Source }).HasDatabaseName("IX_Leads_TenantId_Source");
            entity.HasIndex(l => new { l.TenantId, l.AssignedToUserId }).HasDatabaseName("IX_Leads_TenantId_AssignedToUserId");
            entity.HasIndex(l => new { l.TenantId, l.CreatedAt }).HasDatabaseName("IX_Leads_TenantId_CreatedAt");
        });

        // Agent performance dashboard indexes for LeadTask
        modelBuilder.Entity<LeadTask>(entity =>
        {
            entity.HasIndex(t => new { t.TenantId, t.AssignedToUserId }).HasDatabaseName("IX_LeadTasks_TenantId_AssignedToUserId");
            entity.HasIndex(t => new { t.TenantId, t.Status }).HasDatabaseName("IX_LeadTasks_TenantId_Status");
        });

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            AddDomainEventsAsOutboxMessages();
            GenerateTimestamps();
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("Concurrency exception occurred.", ex);
        }
    }

    private void GenerateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is Entity && (
                e.State == EntityState.Added ||
                e.State == EntityState.Modified ||
                e.State == EntityState.Deleted));

        foreach (var entry in entries)
        {
            var entity = (Entity)entry.Entity;
            entity.UpdatedAt = DateTime.UtcNow;
            if (entry.State == EntityState.Added) entity.CreatedAt = DateTime.UtcNow;
            if (entry.State == EntityState.Deleted)
            {
                entity.DeletedAt = DateTime.UtcNow;
                entity.IsDeleted = true;
            }
        }
    }

    private static readonly JsonSerializerSettings JsonSerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.All
    };

    private void AddDomainEventsAsOutboxMessages()
    {
        var outboxMessages = ChangeTracker
            .Entries<Entity>()
            .Select(e => e.Entity)
            .SelectMany(entity =>
            {
                var events = entity.GetDomainEvents();
                entity.ClearDomainEvents();
                return events;
            })
            .Select(domainEvent => new OutboxMessage(
                Guid.NewGuid(),
                DateTime.UtcNow,
                domainEvent.GetType().Name,
                JsonConvert.SerializeObject(domainEvent, JsonSerializerSettings)))
            .ToList();

        AddRange(outboxMessages);
    }
}
