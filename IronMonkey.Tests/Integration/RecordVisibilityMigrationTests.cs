using IronMonkey.Data;
using IronMonkey.Data.Visibility;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The <c>RecordVisibility</c> migration starts enforcing leads/contacts/opportunities
/// permissions, so it must first grant every existing role the CRM access it already had in
/// effect — including a tenant's own custom roles — and give converted contacts and deals an
/// owner. Seeds the pre-migration schema with raw SQL, migrates, and asserts on the rows.
/// </summary>
[Collection("Integration")]
public class RecordVisibilityMigrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private const string Previous = "20261003104043_ProductsAndQuotes";

    [Fact]
    public async Task Existing_roles_keep_crm_access_and_converted_records_get_their_leads_owner()
    {
        var tenantId = Guid.NewGuid();
        var cs = fixture.ConnectionString.Replace("ironmonkey_test", $"rvmig_{Guid.NewGuid():N}");
        var unrestricted = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(cs).Options;

        await using (var db = new TenantDbContext(unrestricted, tenantId, RecordVisibility.Unrestricted))
            await db.GetService<IMigrator>().MigrateAsync(Previous);

        var owner = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var dealId = Guid.NewGuid();
        var orphanDeal = Guid.NewGuid();

        await using (var c = new NpgsqlConnection(cs))
        {
            await c.OpenAsync();
            async Task Exec(string sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }

            // A custom role the tenant created before enforcement, with no CRM grants at all.
            await Exec(@"INSERT INTO roles (""Id"", ""Name"") VALUES (1001, 'Field Agent');");
            // The Owner role already granted leads:read by an Admin — must not break the migration.
            await Exec(@"INSERT INTO role_permissions (""RoleId"", ""PermissionId"") VALUES (301, 4) ON CONFLICT DO NOTHING;");

            await Exec($@"INSERT INTO ""Users"" (""Id"", ""TenantId"", ""Name"", ""Email"", ""Password"", ""IdentityId"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                          VALUES ('{owner}', '{tenantId}', 'Agent', 'agent@t.test', 'x', '', now(), now(), false);");

            var pipeline = Guid.NewGuid(); var oppStage = Guid.NewGuid(); var leadPipeline = Guid.NewGuid(); var leadStage = Guid.NewGuid();
            await Exec($@"
                INSERT INTO pipelines (""Id"", ""TenantId"", ""RecordType"", ""Name"", ""Order"", ""IsDefault"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES ('{pipeline}', '{tenantId}', 'Opportunity', 'Sales', 1, true, true, now(), now(), false),
                       ('{leadPipeline}', '{tenantId}', 'Lead', 'Leads', 1, true, true, now(), now(), false);
                INSERT INTO pipeline_stages (""Id"", ""TenantId"", ""Name"", ""Order"", ""IsActive"", ""StageType"", ""RecordType"", ""PipelineId"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES ('{oppStage}', '{tenantId}', 'Open', 1, true, 'Entry', 'Opportunity', '{pipeline}', now(), now(), false),
                       ('{leadStage}', '{tenantId}', 'New', 1, true, 'Entry', 'Lead', '{leadPipeline}', now(), now(), false);
                INSERT INTO contacts (""Id"", ""TenantId"", ""Name"", ""Mobile"", ""Email"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"", custom_field_values)
                VALUES ('{contactId}', '{tenantId}', 'Buyer', '1', 'b@t.test', now(), now(), false, '{{}}'::jsonb);
                INSERT INTO opportunities (""Id"", ""TenantId"", ""Title"", ""ContactId"", ""ExpectedCloseDate"", ""PipelineStageId"", ""PipelineId"", ""Amount"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"",
                                           ""OneOffAmount"", ""RecurringAmount"", ""DiscountAmount"", ""TaxAmount"")
                VALUES ('{dealId}', '{tenantId}', 'Converted', '{contactId}', now(), '{oppStage}', '{pipeline}', 0, now(), now(), false, 0, 0, 0, 0),
                       ('{orphanDeal}', '{tenantId}', 'Direct', '{contactId}', now(), '{oppStage}', '{pipeline}', 0, now(), now(), false, 0, 0, 0, 0);
                INSERT INTO leads (""Id"", ""TenantId"", ""FirstName"", ""LastName"", ""Mobile"", ""Email"", ""Source"", ""PipelineStageId"", ""PipelineId"",
                                   ""IsConverted"", ""IsPotentialDuplicate"", ""AssignedToUserId"", ""ConvertedContactId"", ""ConvertedOpportunityId"",
                                   ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"", custom_field_values)
                VALUES ('{Guid.NewGuid()}', '{tenantId}', 'B', 'L', '1', 'b@t.test', 'Manual', '{leadStage}', '{leadPipeline}',
                        true, false, '{owner}', '{contactId}', '{dealId}', now(), now(), false, '{{}}'::jsonb);");
        }

        await using (var db = new TenantDbContext(unrestricted, tenantId, RecordVisibility.Unrestricted))
            await db.Database.MigrateAsync();

        await using var verify = new TenantDbContext(unrestricted, tenantId, RecordVisibility.Unrestricted);

        var custom = await verify.Roles.Where(r => r.Id == 1001).SelectMany(r => r.Permissions).Select(p => p.Name).ToListAsync();
        Assert.Equivalent(new[] { "leads:read", "leads:write", "contacts:read", "contacts:write", "opportunities:read", "opportunities:write" }, custom);

        // SuperAdmin's seeded grants are untouched (no duplicate row, no failure).
        Assert.Equal(1, await verify.RolePermissions.CountAsync(rp => rp.RoleId == 1 && rp.PermissionId == 4));

        Assert.Equal(owner, (await verify.Opportunities.SingleAsync(o => o.Id == dealId)).OwnerUserId);
        Assert.Equal(owner, (await verify.Contacts.SingleAsync(x => x.Id == contactId)).OwnerUserId);
        Assert.Null((await verify.Opportunities.SingleAsync(o => o.Id == orphanDeal)).OwnerUserId);
    }
}
