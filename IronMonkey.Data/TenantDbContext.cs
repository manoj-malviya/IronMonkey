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
    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<LeadMerge> LeadMerges => Set<LeadMerge>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<LeadTask> LeadTasks => Set<LeadTask>();
    public DbSet<WorkflowRule> WorkflowRules => Set<WorkflowRule>();
    public DbSet<StageTransition> StageTransitions => Set<StageTransition>();
    public DbSet<StageChange> StageChanges => Set<StageChange>();
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
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<OpportunityLineItem> OpportunityLineItems => Set<OpportunityLineItem>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();
    public DbSet<QuoteStatusChange> QuoteStatusChanges => Set<QuoteStatusChange>();
    public DbSet<QuoteShareLink> QuoteShareLinks => Set<QuoteShareLink>();
    public DbSet<QuoteSettings> QuoteSettings => Set<QuoteSettings>();

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
        modelBuilder.Entity<Pipeline>().HasQueryFilter(p => p.TenantId == _tenantId && !p.IsDeleted);
        modelBuilder.Entity<PipelineStage>().HasQueryFilter(p => p.TenantId == _tenantId && !p.IsDeleted);
        modelBuilder.Entity<CustomFieldDefinition>().HasQueryFilter(c => c.TenantId == _tenantId && !c.IsDeleted);
        modelBuilder.Entity<LeadMerge>().HasQueryFilter(m => m.TenantId == _tenantId);
        modelBuilder.Entity<ImportBatch>().HasQueryFilter(b => b.TenantId == _tenantId);
        modelBuilder.Entity<LeadTask>().HasQueryFilter(t => t.TenantId == _tenantId && !t.IsDeleted);
        modelBuilder.Entity<WorkflowRule>().HasQueryFilter(r => r.TenantId == _tenantId && !r.IsDeleted);
        modelBuilder.Entity<StageTransition>().HasQueryFilter(t => t.TenantId == _tenantId);

        // Stage history is never soft-deleted: how long a record sat in a stage is a fact
        // about the past that a later edit must not be able to remove, so the filter is
        // TenantId only.
        modelBuilder.Entity<StageChange>().HasQueryFilter(c => c.TenantId == _tenantId);
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

        // Catalog, line items and quotes. Lines, status changes and share links carry the
        // TenantId filter too: they are reachable by id from public and report queries, and
        // a filter on the parent alone does not protect a query that starts at the child.
        modelBuilder.Entity<Product>().HasQueryFilter(p => p.TenantId == _tenantId && !p.IsDeleted);
        modelBuilder.Entity<PriceList>().HasQueryFilter(p => p.TenantId == _tenantId && !p.IsDeleted);
        modelBuilder.Entity<ProductPrice>().HasQueryFilter(p => p.TenantId == _tenantId);
        modelBuilder.Entity<OpportunityLineItem>().HasQueryFilter(l => l.TenantId == _tenantId);
        modelBuilder.Entity<Quote>().HasQueryFilter(q => q.TenantId == _tenantId && !q.IsDeleted);
        modelBuilder.Entity<QuoteLine>().HasQueryFilter(l => l.TenantId == _tenantId);
        modelBuilder.Entity<QuoteStatusChange>().HasQueryFilter(c => c.TenantId == _tenantId);
        modelBuilder.Entity<QuoteShareLink>().HasQueryFilter(l => l.TenantId == _tenantId);
        modelBuilder.Entity<QuoteSettings>().HasQueryFilter(s => s.TenantId == _tenantId);

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
            DerivePipelineMembership();
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("Concurrency exception occurred.", ex);
        }
    }

    /// <summary>
    /// Fills in <c>PipelineId</c> on any lead, opportunity or stage saved without one, by
    /// reading it from the stage (or, for a stage, from the tenant's default pipeline of its
    /// record type).
    ///
    /// <para>
    /// This exists because <c>PipelineId</c> is non-nullable and there is no honest default
    /// for it that does not depend on data. Threading it through every <c>Create</c>
    /// signature would make the invariant a convention every one of dozens of call sites has
    /// to remember; deriving it here makes it a property of saving, which no call site can
    /// skip. A record's pipeline is, by definition, the pipeline of the stage it is in — so
    /// this derives the value rather than inventing one.
    /// </para>
    ///
    /// <para>
    /// It only ever fills an <i>empty</i> value. A caller that set the pipeline explicitly —
    /// every cross-pipeline move does — is never overridden, so this cannot silently undo a
    /// deliberate placement.
    /// </para>
    ///
    /// <para>
    /// Note it reads stages with <c>IgnoreQueryFilters</c> off: the stage must be one of this
    /// tenant's, and the global filter is what guarantees that. A stage id from another
    /// tenant resolves to nothing and the value stays empty, where the foreign key rejects
    /// it — loudly, which is correct.
    /// </para>
    /// </summary>
    private void DerivePipelineMembership()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        var pendingLeads = entries
            .Select(e => e.Entity).OfType<Lead>()
            .Where(l => l.PipelineId == Guid.Empty).ToList();

        var pendingOpportunities = entries
            .Select(e => e.Entity).OfType<Opportunity>()
            .Where(o => o.PipelineId == Guid.Empty).ToList();

        var pendingStages = entries
            .Select(e => e.Entity).OfType<PipelineStage>()
            .Where(s => s.PipelineId == Guid.Empty).ToList();

        if (pendingLeads.Count == 0 && pendingOpportunities.Count == 0 && pendingStages.Count == 0)
            return;

        // Stages are resolved FIRST. A lead and the stage it points at are routinely created
        // in the same SaveChanges, and the lead's pipeline is read off that stage — so if the
        // stage had not been given its own pipeline yet, the lead would find nothing and the
        // foreign key would reject the whole save.
        if (pendingStages.Count > 0)
        {
            // A stage created through the older CreateFor/Create factories, which name no
            // pipeline. It joins its tenant's default pipeline for its own record type —
            // the same pipeline a record created without one would land in, so the stage and
            // the records that point at it stay in agreement.
            var trackedPipelines = ChangeTracker.Entries<Pipeline>()
                .Select(e => e.Entity)
                .ToList();

            foreach (var group in pendingStages.GroupBy(s => s.RecordType))
            {
                var recordType = group.Key;

                var pipeline =
                    trackedPipelines.FirstOrDefault(p => p.RecordType == recordType && p.IsDefault)
                    ?? trackedPipelines.FirstOrDefault(p => p.RecordType == recordType)
                    ?? Pipelines.AsTracking()
                        .Where(p => p.RecordType == recordType)
                        .OrderByDescending(p => p.IsDefault)
                        .ThenBy(p => p.Order)
                        .FirstOrDefault();

                // No pipeline of this record type exists yet — the tenant predates pipelines
                // in-process, or a test is building a fixture stage-first. Create the default
                // one rather than refusing the save: a stage with no pipeline is precisely
                // the orphan state the model forbids, and the alternative to creating the
                // pipeline it belongs in is a foreign key violation nobody can act on.
                //
                // This is the same default pipeline the migration backfills into, by the same
                // name, so an in-process creation and a migrated database agree.
                pipeline ??= CreateDefaultPipeline(recordType);

                foreach (var stage in group) stage.AssignToPipeline(pipeline);
            }
        }

        // Stages being added in this same save are not in the database yet, so they are read
        // from the change tracker as well.
        var trackedStages = ChangeTracker.Entries<PipelineStage>()
            .Select(e => e.Entity)
            .Where(s => s.PipelineId != Guid.Empty)
            .ToDictionary(s => s.Id, s => s.PipelineId);

        Guid PipelineOfStage(Guid stageId)
        {
            if (trackedStages.TryGetValue(stageId, out var tracked)) return tracked;

            var stage = PipelineStages.AsTracking()
                .FirstOrDefault(s => s.Id == stageId);

            return stage?.PipelineId ?? Guid.Empty;
        }

        foreach (var lead in pendingLeads)
        {
            var pipelineId = PipelineOfStage(lead.PipelineStageId);
            if (pipelineId != Guid.Empty) lead.SetPipeline(pipelineId);
        }

        foreach (var opportunity in pendingOpportunities)
        {
            var pipelineId = PipelineOfStage(opportunity.PipelineStageId);
            if (pipelineId != Guid.Empty) opportunity.SetPipeline(pipelineId);
        }

    }

    /// <summary>
    /// The name the default pipeline is created under, in-process and by the migration
    /// alike. Kept in one place so the two cannot drift — a tenant migrated by SQL and a
    /// tenant provisioned in code must end up with the same thing.
    /// </summary>
    public const string DefaultLeadPipelineName = "Default";

    /// <summary>The default opportunity pipeline's name. Distinct from the lead one only for
    /// legibility in a picker that shows both.</summary>
    public const string DefaultOpportunityPipelineName = "Default";

    private Pipeline CreateDefaultPipeline(PipelineRecordType recordType)
    {
        var name = recordType == PipelineRecordType.Opportunity
            ? DefaultOpportunityPipelineName
            : DefaultLeadPipelineName;

        var pipeline = Pipeline.Create(_tenantId, recordType, name, order: 1, isDefault: true);
        Pipelines.Add(pipeline);

        // Timestamped here because GenerateTimestamps has already run for this save — a row
        // added during derivation would otherwise reach the database with default dates.
        pipeline.CreatedAt = DateTime.UtcNow;
        pipeline.UpdatedAt = DateTime.UtcNow;

        return pipeline;
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
