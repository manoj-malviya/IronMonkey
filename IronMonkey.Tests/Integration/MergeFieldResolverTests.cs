using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Features.Communications;
using IronMonkey.Data;
using IronMonkey.Data.Communications;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// Merge-field resolution against real custom field definitions.
///
/// The scope test is here because the filter was initially missing: the resolver took a scope
/// argument and ignored it, so a lead template silently offered contact-only fields that could
/// never resolve. Values are keyed by definition id, so a field from the wrong scope is not
/// merely irrelevant — it always renders empty.
/// </summary>
[Collection("Integration")]
public class MergeFieldResolverTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<(TenantDbContext Db, Guid TenantId)> NewTenantAsync()
    {
        var tenantId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var db = _factory.CreateForTenant(
            fixture.ConnectionString.Replace("ironmonkey_test", $"merge_{suffix}"), tenantId);

        await db.Database.MigrateAsync();
        return (db, tenantId);
    }

    [Fact]
    public async Task Built_in_lead_fields_resolve()
    {
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        var lead = Lead.Create(tenantId, "Ada", "Lovelace", "555-0000", "ada@example.com",
            LeadSource.Api, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var loaded = await db.Leads.Include(l => l.Stage).FirstAsync();
        var values = await new MergeFieldResolver().ForLeadAsync(db, loaded, default);

        Assert.Equal("Ada", values["FirstName"]);
        Assert.Equal("Lovelace", values["LastName"]);
        Assert.Equal("Ada Lovelace", values["FullName"]);
        Assert.Equal("ada@example.com", values["Email"]);
        Assert.Equal("New", values["StageName"]);
    }

    [Fact]
    public async Task A_lead_custom_field_resolves_by_definition_id_not_by_name()
    {
        // Values are keyed by the definition Id throughout the codebase, so renaming a field
        // must not orphan the stored value.
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var definition = CustomFieldDefinition.Create(tenantId, "Budget", CustomFieldType.Text, false, []);
        db.CustomFieldDefinitions.Add(definition);

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);

        var lead = Lead.Create(tenantId, "Ada", "L", "555", "ada@example.com", LeadSource.Api, stage.Id);
        lead.CustomFields.Set(definition.Id.ToString(), "50000");
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var loaded = await db.Leads.Include(l => l.Stage).FirstAsync();
        var values = await new MergeFieldResolver().ForLeadAsync(db, loaded, default);

        Assert.Equal("50000", values["Budget"]);
    }

    [Fact]
    public async Task A_contact_scoped_field_is_not_offered_when_rendering_a_lead()
    {
        // The bug this pins: without the scope filter the resolver returned every definition,
        // so a lead render exposed a contact-only field that could only ever be blank.
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var contactOnly = CustomFieldDefinition.Create(tenantId, "PreferredPronouns",
            CustomFieldType.Text, false, [], CustomFieldEntity.Contact);
        db.CustomFieldDefinitions.Add(contactOnly);

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);
        var lead = Lead.Create(tenantId, "Ada", "L", "555", "ada@example.com", LeadSource.Api, stage.Id);
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var loaded = await db.Leads.Include(l => l.Stage).FirstAsync();
        var values = await new MergeFieldResolver().ForLeadAsync(db, loaded, default);

        Assert.False(values.ContainsKey("PreferredPronouns"));
    }

    [Fact]
    public async Task A_lead_scoped_field_is_not_offered_when_rendering_a_contact()
    {
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        db.CustomFieldDefinitions.Add(CustomFieldDefinition.Create(
            tenantId, "LeadOnlyBudget", CustomFieldType.Text, false, [], CustomFieldEntity.Lead));

        var contact = Contact.Create(tenantId, "Ada", "555", "ada@example.com");
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var loaded = await db.Contacts.FirstAsync();
        var values = await new MergeFieldResolver().ForContactAsync(db, loaded, default);

        Assert.False(values.ContainsKey("LeadOnlyBudget"));
        Assert.Equal("Ada", values["Name"]);
    }

    [Fact]
    public async Task A_custom_field_cannot_shadow_a_built_in()
    {
        // Otherwise a tenant could create a field named "Email" and silently redirect what
        // every template renders.
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        var shadow = CustomFieldDefinition.Create(tenantId, "Email", CustomFieldType.Text, false, []);
        db.CustomFieldDefinitions.Add(shadow);

        var stage = PipelineStage.Create(tenantId, "New", 1);
        db.PipelineStages.Add(stage);

        var lead = Lead.Create(tenantId, "Ada", "L", "555", "real@example.com", LeadSource.Api, stage.Id);
        lead.CustomFields.Set(shadow.Id.ToString(), "attacker@evil.example");
        db.Leads.Add(lead);
        await db.SaveChangesAsync();

        var loaded = await db.Leads.Include(l => l.Stage).FirstAsync();
        var values = await new MergeFieldResolver().ForLeadAsync(db, loaded, default);

        Assert.Equal("real@example.com", values["Email"]);
    }

    [Fact]
    public async Task Available_fields_include_built_ins_and_active_custom_fields()
    {
        var (db, tenantId) = await NewTenantAsync();
        await using var _db = db;

        db.CustomFieldDefinitions.Add(CustomFieldDefinition.Create(
            tenantId, "Budget", CustomFieldType.Text, false, []));
        await db.SaveChangesAsync();

        var available = await new MergeFieldResolver().AvailableFieldsAsync(db, default);

        Assert.Contains("FirstName", available);
        Assert.Contains("Budget", available);
    }
}
