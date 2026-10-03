using IronMonkey.Data;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The <c>ProductsAndQuotes</c> migration moves every pre-line-item <c>Amount</c> onto a
/// line item. Deal value is now computed from lines, so a deal left without one would read
/// as zero the moment its totals were recomputed — silently, on every dashboard.
///
/// Migrates to the previous migration, seeds opportunities through raw SQL in the old
/// schema, applies the migration, and asserts on the resulting rows.
/// </summary>
[Collection("Integration")]
public class ProductsAndQuotesMigrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private const string PreviousMigration = "20261003101251_LeadPipelineSortIndexes";

    private readonly TenantDbContextFactory _factory = new();

    [Fact]
    public async Task Every_existing_amount_becomes_one_line_including_zero()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"pqmig_{Guid.NewGuid():N}"[..30]);

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var priced = Guid.NewGuid();
        var zero = Guid.NewGuid();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var stage = await ScalarAsync<Guid>(connection,
                "SELECT \"Id\" FROM pipeline_stages WHERE \"RecordType\" = 'Opportunity' LIMIT 1");
            if (stage == Guid.Empty) stage = await SeedOpportunityStageAsync(connection, tenantId);

            var pipeline = await ScalarAsync<Guid>(connection,
                $"SELECT \"PipelineId\" FROM pipeline_stages WHERE \"Id\" = '{stage}'");

            var contact = Guid.NewGuid();
            await ExecAsync(connection, $@"
                INSERT INTO contacts (""Id"", ""TenantId"", ""Name"", ""Mobile"", ""Email"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"", custom_field_values)
                VALUES ('{contact}', '{tenantId}', 'Legacy', '555', 'legacy@t.com', now(), now(), false, '{{}}'::jsonb);");

            foreach (var (id, amount) in new[] { (priced, "1234.56"), (zero, "0") })
            {
                await ExecAsync(connection, $@"
                    INSERT INTO opportunities (""Id"", ""TenantId"", ""Title"", ""ContactId"", ""ExpectedCloseDate"",
                        ""PipelineStageId"", ""PipelineId"", ""Amount"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                    VALUES ('{id}', '{tenantId}', 'Legacy deal', '{contact}', now(), '{stage}', '{pipeline}', {amount}, now(), now(), false);");
            }
        }

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
            await db.Database.MigrateAsync();

        await using var verify = _factory.CreateForTenant(connectionString, tenantId);
        var deals = await verify.Opportunities.Include(o => o.LineItems).ToListAsync();

        var pricedDeal = deals.Single(d => d.Id == priced);
        var line = Assert.Single(pricedDeal.LineItems);
        Assert.Equal(1234.56m, line.UnitPrice);
        Assert.Equal(1234.56m, line.TotalAmount);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(ChargeType.OneOff, line.ChargeType);
        Assert.True(line.IsMigrated);
        Assert.Equal(OpportunityLineItem.MigratedDescription, line.Description);
        Assert.Equal(1234.56m, pricedDeal.Amount);
        Assert.Equal(1234.56m, pricedDeal.OneOffAmount);
        Assert.Null(pricedDeal.CurrencyCode);

        // Zero (indistinguishable from "never entered" in the old schema) is carried over the
        // same way, as a zero-priced migrated line, so the rule has no exception.
        var zeroDeal = deals.Single(d => d.Id == zero);
        var zeroLine = Assert.Single(zeroDeal.LineItems);
        Assert.Equal(0m, zeroLine.TotalAmount);
        Assert.True(zeroLine.IsMigrated);
        Assert.Equal(0m, zeroDeal.Amount);

        // And the stored total agrees with what the calculator derives from the lines.
        Assert.Equal(pricedDeal.Amount, pricedDeal.RecalculateTotals().Total);
    }

    private static async Task<Guid> SeedOpportunityStageAsync(NpgsqlConnection connection, Guid tenantId)
    {
        var pipeline = Guid.NewGuid();
        var stage = Guid.NewGuid();
        await ExecAsync(connection, $@"
            INSERT INTO pipelines (""Id"", ""TenantId"", ""RecordType"", ""Name"", ""Order"", ""IsDefault"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
            VALUES ('{pipeline}', '{tenantId}', 'Opportunity', 'Sales', 1, true, true, now(), now(), false);
            INSERT INTO pipeline_stages (""Id"", ""TenantId"", ""Name"", ""Order"", ""IsActive"", ""StageType"", ""RecordType"", ""PipelineId"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
            VALUES ('{stage}', '{tenantId}', 'Prospecting', 1, true, 'Entry', 'Opportunity', '{pipeline}', now(), now(), false);");
        return stage;
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default! : (T)result;
    }
}
