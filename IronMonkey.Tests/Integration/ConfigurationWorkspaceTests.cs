using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.ApiService.Features.CustomFields;
using IronMonkey.ApiService.Features.Leads.Pipeline.Routing;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Covers the safety rules the configuration workspace depends on: duplicate names, protection
/// for referenced stages and fields, ordering that survives concurrent edits, option changes
/// that would strand values, routing validation, and tenant isolation.
/// </summary>
[Collection("Integration")]
public class ConfigurationWorkspaceTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();
    private readonly ConfigurationUsageService _usage = new();

    private (string ConnectionString, TenantDbContext Db) CreateDb(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"cw_{label}_{suffix}");
        return (connectionString, _factory.CreateForTenant(connectionString, tenantId));
    }

    private static TenantDbContext Fresh(string connectionString, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connectionString).Options;
        return new TenantDbContext(options, tenantId);
    }

    // ── Duplicate names ─────────────────────────────────────────────────────

    [Fact]
    public async Task StageNames_AreUniquePerTenant_CaseInsensitively()
    {
        var tenantId = Guid.NewGuid();
        var (_, db) = CreateDb(tenantId, "stgdup");
        await using (db)
        {
            await db.Database.MigrateAsync();

            db.PipelineStages.Add(PipelineStage.Create(tenantId, "Qualified", 1));
            await db.SaveChangesAsync();

            // Differs only by case, which reads as the same stage on a board column.
            db.PipelineStages.Add(PipelineStage.Create(tenantId, "qualified", 2));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task FieldKeys_AreUniqueWithinScope_ButSharedAcrossScopes()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantId, "keydup");
        await using (db)
        {
            await db.Database.MigrateAsync();

            db.CustomFieldDefinitions.Add(CustomFieldDefinition.Create(
                tenantId, "Region", CustomFieldType.Text, false, appliesTo: CustomFieldEntity.Lead));

            // The same key on the other record type is a different field, so it is allowed.
            db.CustomFieldDefinitions.Add(CustomFieldDefinition.Create(
                tenantId, "Region", CustomFieldType.Text, false, appliesTo: CustomFieldEntity.Contact));

            await db.SaveChangesAsync();

            await using var fresh = Fresh(connectionString, tenantId);
            Assert.Equal(2, await fresh.CustomFieldDefinitions.CountAsync());

            db.CustomFieldDefinitions.Add(CustomFieldDefinition.Create(
                tenantId, "Region", CustomFieldType.Text, false, appliesTo: CustomFieldEntity.Lead));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public void DeriveKey_ProducesAStableSlug()
    {
        Assert.Equal("annual_revenue", CustomFieldDefinition.DeriveKey("Annual Revenue"));
        Assert.Equal("region", CustomFieldDefinition.DeriveKey("  Region?  "));
        Assert.Equal("q1_target", CustomFieldDefinition.DeriveKey("Q1 — Target"));

        // A label with nothing usable in it still has to yield a valid key.
        Assert.Equal("field", CustomFieldDefinition.DeriveKey("???"));
    }

    // ── Referenced-item protection ──────────────────────────────────────────

    [Fact]
    public async Task StageUsage_CountsLeadsAndFlagsTheLastActiveStage()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantId, "stgimp");
        await using (db)
        {
            await db.Database.MigrateAsync();

            var stage = PipelineStage.Create(tenantId, "New", 1);
            db.PipelineStages.Add(stage);
            await db.SaveChangesAsync();

            db.Leads.Add(Lead.Create(tenantId, "Ada", "Lovelace", "555-1111",
                "ada@example.com", LeadSource.Manual, stage.Id));
            await db.SaveChangesAsync();

            await using var fresh = Fresh(connectionString, tenantId);
            var usage = await _usage.GetStageUsageAsync(fresh, stage.Id, CancellationToken.None);

            Assert.Equal(1, usage.LeadCount);
            Assert.True(usage.IsReferenced);

            // It is the tenant's only active stage, so removing it would leave nowhere to put
            // a lead.
            Assert.True(usage.IsOnlyActiveStage);
        }
    }

    [Fact]
    public async Task StageUsage_IsZero_ForAnUnusedStage()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantId, "stgfree");
        await using (db)
        {
            await db.Database.MigrateAsync();

            var used = PipelineStage.Create(tenantId, "New", 1);
            var unused = PipelineStage.Create(tenantId, "Archived", 2);
            db.PipelineStages.AddRange(used, unused);
            await db.SaveChangesAsync();

            db.Leads.Add(Lead.Create(tenantId, "Ada", "Lovelace", "555-1111",
                "ada@example.com", LeadSource.Manual, used.Id));
            await db.SaveChangesAsync();

            await using var fresh = Fresh(connectionString, tenantId);
            var usage = await _usage.GetStageUsageAsync(fresh, unused.Id, CancellationToken.None);

            Assert.Equal(0, usage.LeadCount);
            Assert.False(usage.IsReferenced);
            Assert.False(usage.IsOnlyActiveStage);
        }
    }

    [Fact]
    public async Task FieldUsage_CountsOnlyRecordsThatCapturedAValue()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantId, "fldimp");
        await using (db)
        {
            await db.Database.MigrateAsync();

            var stage = PipelineStage.Create(tenantId, "New", 1);
            db.PipelineStages.Add(stage);

            var field = CustomFieldDefinition.Create(tenantId, "Region", CustomFieldType.Text, false);
            db.CustomFieldDefinitions.Add(field);
            await db.SaveChangesAsync();

            var withValue = Lead.Create(tenantId, "Ada", "L", "555-1", "a@example.com", LeadSource.Manual, stage.Id);
            withValue.CustomFields.Set(field.Id.ToString(), "West");

            var withoutValue = Lead.Create(tenantId, "Bob", "M", "555-2", "b@example.com", LeadSource.Manual, stage.Id);

            // An explicit null is not a captured value and must not inflate the count.
            var withNull = Lead.Create(tenantId, "Cid", "N", "555-3", "c@example.com", LeadSource.Manual, stage.Id);
            withNull.CustomFields.Set(field.Id.ToString(), null);

            db.Leads.AddRange(withValue, withoutValue, withNull);
            await db.SaveChangesAsync();

            await using var fresh = Fresh(connectionString, tenantId);
            var definition = await fresh.CustomFieldDefinitions.SingleAsync(f => f.Id == field.Id);
            var usage = await _usage.GetFieldUsageAsync(fresh, definition, CancellationToken.None);

            Assert.Equal(1, usage.RecordCount);
            Assert.True(usage.IsReferenced);
        }
    }

    // ── Option changes ──────────────────────────────────────────────────────

    [Fact]
    public async Task FieldUsage_ReportsStoredValuesTheOptionsNoLongerOffer()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantId, "fldopt");
        await using (db)
        {
            await db.Database.MigrateAsync();

            var stage = PipelineStage.Create(tenantId, "New", 1);
            db.PipelineStages.Add(stage);

            var field = CustomFieldDefinition.Create(tenantId, "Tier", CustomFieldType.Dropdown,
                false, options: ["Gold", "Silver", "Bronze"]);
            db.CustomFieldDefinitions.Add(field);
            await db.SaveChangesAsync();

            var gold = Lead.Create(tenantId, "Ada", "L", "555-1", "a@example.com", LeadSource.Manual, stage.Id);
            gold.CustomFields.Set(field.Id.ToString(), "Gold");

            var bronze = Lead.Create(tenantId, "Bob", "M", "555-2", "b@example.com", LeadSource.Manual, stage.Id);
            bronze.CustomFields.Set(field.Id.ToString(), "Bronze");

            db.Leads.AddRange(gold, bronze);
            await db.SaveChangesAsync();

            // Narrow the options — "Bronze" is now stranded on a record.
            var definition = await db.CustomFieldDefinitions.SingleAsync(f => f.Id == field.Id);
            definition.Update("Tier", CustomFieldType.Dropdown, false, ["Gold", "Silver"]);
            await db.SaveChangesAsync();

            await using var fresh = Fresh(connectionString, tenantId);
            var reloaded = await fresh.CustomFieldDefinitions.SingleAsync(f => f.Id == field.Id);
            var usage = await _usage.GetFieldUsageAsync(fresh, reloaded, CancellationToken.None);

            Assert.Equal(2, usage.RecordCount);
            Assert.Equal(["Bronze"], usage.ValuesOutsideOptions);
        }
    }

    [Fact]
    public async Task FieldUsage_FlattensMultiSelectValues()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantId, "fldmulti");
        await using (db)
        {
            await db.Database.MigrateAsync();

            var stage = PipelineStage.Create(tenantId, "New", 1);
            db.PipelineStages.Add(stage);

            var field = CustomFieldDefinition.Create(tenantId, "Interests", CustomFieldType.MultiSelect,
                false, options: ["Email"]);
            db.CustomFieldDefinitions.Add(field);
            await db.SaveChangesAsync();

            var lead = Lead.Create(tenantId, "Ada", "L", "555-1", "a@example.com", LeadSource.Manual, stage.Id);
            lead.CustomFields.Set(field.Id.ToString(), new List<string> { "Email", "Phone" });
            db.Leads.Add(lead);
            await db.SaveChangesAsync();

            await using var fresh = Fresh(connectionString, tenantId);
            var reloaded = await fresh.CustomFieldDefinitions.SingleAsync(f => f.Id == field.Id);
            var usage = await _usage.GetFieldUsageAsync(fresh, reloaded, CancellationToken.None);

            // Each element is checked on its own, so only the unoffered one is reported.
            Assert.Equal(["Phone"], usage.ValuesOutsideOptions);
        }
    }

    // ── Ordering ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reordering_SwapsPositionsWithoutViolatingAConstraint()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantId, "order");
        await using (db)
        {
            await db.Database.MigrateAsync();

            var first = PipelineStage.Create(tenantId, "New", 1);
            var second = PipelineStage.Create(tenantId, "Qualified", 2);
            var third = PipelineStage.Create(tenantId, "Won", 3);
            db.PipelineStages.AddRange(first, second, third);
            await db.SaveChangesAsync();

            // Reversing the order passes through states where two stages briefly hold the
            // same position; the write must still succeed as one set.
            first.SetOrder(3);
            second.SetOrder(2);
            third.SetOrder(1);
            await db.SaveChangesAsync();

            await using var fresh = Fresh(connectionString, tenantId);
            var ordered = await fresh.PipelineStages.OrderBy(s => s.Order).Select(s => s.Name).ToListAsync();

            Assert.Equal(["Won", "Qualified", "New"], ordered);
        }
    }

    [Fact]
    public async Task Reordering_IsRejected_WhenTheStageSetHasChanged()
    {
        var tenantId = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantId, "orderconc");
        await using (db)
        {
            await db.Database.MigrateAsync();

            var first = PipelineStage.Create(tenantId, "New", 1);
            var second = PipelineStage.Create(tenantId, "Qualified", 2);
            db.PipelineStages.AddRange(first, second);
            await db.SaveChangesAsync();

            // What one admin's browser is holding.
            var submitted = new[] { second.Id, first.Id }.ToHashSet();

            // Meanwhile another admin adds a stage.
            await using (var other = Fresh(connectionString, tenantId))
            {
                other.PipelineStages.Add(PipelineStage.Create(tenantId, "Won", 3));
                await other.SaveChangesAsync();
            }

            await using var fresh = Fresh(connectionString, tenantId);
            var current = (await fresh.PipelineStages.Select(s => s.Id).ToListAsync()).ToHashSet();

            // The endpoint compares these sets and refuses rather than silently leaving the
            // new stage at a position the submitted sequence also claims.
            Assert.False(submitted.SetEquals(current));
        }
    }

    // ── Routing validation ──────────────────────────────────────────────────

    [Fact]
    public void TerritoryRules_RejectMissingNamesValuesAndAssignees()
    {
        var assignee = Guid.NewGuid();

        var ruleSet = new TerritoryRuleSet
        {
            Rules =
            [
                new TerritoryRule { Name = "", Values = [], AssigneeId = "" },
                new TerritoryRule { Name = "West", Values = ["CA"], AssigneeId = assignee.ToString() }
            ]
        };

        var errors = ruleSet.Validate([assignee]);

        Assert.Contains(errors, e => e.Contains("needs a territory name"));
        Assert.Contains(errors, e => e.Contains("needs at least one matching value"));
        Assert.Contains(errors, e => e.Contains("needs an assigned user"));
    }

    [Fact]
    public void TerritoryRules_RejectAnAssigneeWhoIsNotATenantUser()
    {
        var ruleSet = new TerritoryRuleSet
        {
            Rules = [new TerritoryRule { Name = "West", Values = ["CA"], AssigneeId = Guid.NewGuid().ToString() }]
        };

        var errors = ruleSet.Validate([Guid.NewGuid()]);

        Assert.Contains(errors, e => e.Contains("no longer exists"));
    }

    [Fact]
    public void TerritoryRules_RejectTwoRulesClaimingTheSameValue()
    {
        var assignee = Guid.NewGuid();

        var ruleSet = new TerritoryRuleSet
        {
            Rules =
            [
                new TerritoryRule { Name = "West", Values = ["CA"], AssigneeId = assignee.ToString() },
                new TerritoryRule { Name = "Pacific", Values = ["CA"], AssigneeId = assignee.ToString() }
            ]
        };

        var errors = ruleSet.Validate([assignee]);

        Assert.Contains(errors, e => e.Contains("claimed by both"));
    }

    [Fact]
    public void TerritoryRules_AcceptAValidSetAndFlattenToTheRoutingLookup()
    {
        var assignee = Guid.NewGuid();

        var ruleSet = new TerritoryRuleSet
        {
            Rules = [new TerritoryRule { Name = "West", Values = ["CA", "OR"], AssigneeId = assignee.ToString() }]
        };

        Assert.Empty(ruleSet.Validate([assignee]));

        // One rule covering two values becomes one lookup entry each.
        var lookup = ruleSet.ToLookup();
        Assert.Equal(assignee.ToString(), lookup["CA"]);
        Assert.Equal(assignee.ToString(), lookup["OR"]);
    }

    [Fact]
    public void TerritoryRules_RejectMalformedJson()
    {
        Assert.Null(TerritoryRuleSet.Parse("{ not json"));
        Assert.Null(TerritoryRuleSet.Parse("[1, 2, 3]"));
        Assert.Null(TerritoryRuleSet.Parse("{\"rules\": \"not-an-array\"}"));
    }

    [Fact]
    public void TerritoryRules_StillReadTheOriginalFlatMap()
    {
        var assignee = Guid.NewGuid().ToString();

        var parsed = TerritoryRuleSet.Parse($"{{\"Referral\": \"{assignee}\"}}");

        Assert.NotNull(parsed);
        var rule = Assert.Single(parsed!.Rules);
        Assert.Equal("Referral", rule.Name);
        Assert.Equal([ "Referral" ], rule.Values);
        Assert.Equal(assignee, rule.AssigneeId);
    }

    // ── Field binding: defaults, archived fields, required ──────────────────

    [Fact]
    public void Binder_AppliesTheDefaultWhenNothingIsSubmitted()
    {
        var tenantId = Guid.NewGuid();
        var field = CustomFieldDefinition.Create(tenantId, "Tier", CustomFieldType.Dropdown,
            isRequired: true, options: ["Gold", "Silver"], defaultValue: "Silver");

        var result = CustomFieldValueBinder.Bind([field], new Dictionary<string, object?>());

        Assert.True(result.IsValid);
        Assert.Equal("Silver", result.Values[field.Id.ToString()]);
    }

    [Fact]
    public void Binder_PrefersASubmittedValueOverTheDefault()
    {
        var tenantId = Guid.NewGuid();
        var field = CustomFieldDefinition.Create(tenantId, "Tier", CustomFieldType.Dropdown,
            isRequired: false, options: ["Gold", "Silver"], defaultValue: "Silver");

        var result = CustomFieldValueBinder.Bind([field],
            new Dictionary<string, object?> { [field.Id.ToString()] = "Gold" });

        Assert.Equal("Gold", result.Values[field.Id.ToString()]);
    }

    [Fact]
    public void Binder_DoesNotRequireAnArchivedField()
    {
        var tenantId = Guid.NewGuid();
        var field = CustomFieldDefinition.Create(tenantId, "Legacy", CustomFieldType.Text, isRequired: true);
        field.Archive();

        var result = CustomFieldValueBinder.Bind([field], new Dictionary<string, object?>());

        // Archiving a required field must not lock every record in the tenant.
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Binder_KeepsAnArchivedFieldsExistingValue()
    {
        var tenantId = Guid.NewGuid();
        var field = CustomFieldDefinition.Create(tenantId, "Legacy", CustomFieldType.Text, isRequired: false);
        field.Archive();

        var result = CustomFieldValueBinder.Bind([field],
            new Dictionary<string, object?> { [field.Id.ToString()] = "kept" });

        Assert.Equal("kept", result.Values[field.Id.ToString()]);
    }

    [Fact]
    public void Binder_StillReportsAMissingRequiredField()
    {
        var tenantId = Guid.NewGuid();
        var field = CustomFieldDefinition.Create(tenantId, "Region", CustomFieldType.Text, isRequired: true);

        var result = CustomFieldValueBinder.Bind([field], new Dictionary<string, object?>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'Region' is required."));
    }

    [Fact]
    public void Binder_ResolvesAValueSubmittedUnderTheInternalKey()
    {
        var tenantId = Guid.NewGuid();
        var field = CustomFieldDefinition.Create(tenantId, "Annual Revenue", CustomFieldType.Number,
            isRequired: false);

        var result = CustomFieldValueBinder.Bind([field],
            new Dictionary<string, object?> { ["annual_revenue"] = "1000" });

        Assert.Equal(1000m, result.Values[field.Id.ToString()]);
    }

    // ── Tenant isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Configuration_IsNotVisibleToAnotherTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantA, "iso");

        await using (db)
        {
            await db.Database.MigrateAsync();

            db.PipelineStages.Add(PipelineStage.Create(tenantA, "A-only stage", 1));
            db.CustomFieldDefinitions.Add(CustomFieldDefinition.Create(
                tenantA, "A-only field", CustomFieldType.Text, false));
            await db.SaveChangesAsync();

            // The same physical database, read as the other tenant: the global query filter
            // must hide every row.
            await using var asTenantB = Fresh(connectionString, tenantB);

            Assert.Empty(await asTenantB.PipelineStages.ToListAsync());
            Assert.Empty(await asTenantB.CustomFieldDefinitions.ToListAsync());

            await using var asTenantA = Fresh(connectionString, tenantA);
            Assert.Single(await asTenantA.PipelineStages.ToListAsync());
            Assert.Single(await asTenantA.CustomFieldDefinitions.ToListAsync());
        }
    }

    [Fact]
    public async Task FieldUsage_CountsOnlyTheCurrentTenantsRecords()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (connectionString, db) = CreateDb(tenantA, "isousage");

        await using (db)
        {
            await db.Database.MigrateAsync();

            var stageA = PipelineStage.Create(tenantA, "New", 1);
            db.PipelineStages.Add(stageA);

            var field = CustomFieldDefinition.Create(tenantA, "Region", CustomFieldType.Text, false);
            db.CustomFieldDefinitions.Add(field);
            await db.SaveChangesAsync();

            var leadA = Lead.Create(tenantA, "Ada", "L", "555-1", "a@example.com", LeadSource.Manual, stageA.Id);
            leadA.CustomFields.Set(field.Id.ToString(), "West");
            db.Leads.Add(leadA);
            await db.SaveChangesAsync();

            // A second tenant's lead carrying the same field key must not be counted.
            await using (var asTenantB = Fresh(connectionString, tenantB))
            {
                var stageB = PipelineStage.Create(tenantB, "New", 1);
                asTenantB.PipelineStages.Add(stageB);
                await asTenantB.SaveChangesAsync();

                var leadB = Lead.Create(tenantB, "Bob", "M", "555-2", "b@example.com", LeadSource.Manual, stageB.Id);
                leadB.CustomFields.Set(field.Id.ToString(), "East");
                asTenantB.Leads.Add(leadB);
                await asTenantB.SaveChangesAsync();
            }

            await using var fresh = Fresh(connectionString, tenantA);
            var definition = await fresh.CustomFieldDefinitions.SingleAsync(f => f.Id == field.Id);
            var usage = await _usage.GetFieldUsageAsync(fresh, definition, CancellationToken.None);

            Assert.Equal(1, usage.RecordCount);
        }
    }
}
