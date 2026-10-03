using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <summary>
    /// Restores an index seek for the default lead and opportunity list queries.
    ///
    /// Those lists filter on TenantId, PipelineId and NOT IsDeleted, then order by
    /// CreatedAt DESC with an Id tiebreak. Before multiple pipelines existed the query was
    /// a prefix match for IX_Leads_TenantId_CreatedAt and read exactly one page. Adding the
    /// PipelineId predicate broke that prefix: no existing index carried both the filter
    /// columns and the sort column, so Postgres had to sort the whole filtered set before
    /// applying LIMIT. These indexes put the two equality columns first and the sort key
    /// after them, with Id trailing so the tiebreak is satisfied from the index too.
    ///
    /// Partial on NOT "IsDeleted" because the global query filter always applies that
    /// predicate, so the partial index is always usable and stays smaller.
    ///
    /// TenantId is the LEADING index column. That is the structural form of this repo's
    /// rule that raw SQL must scope by tenant: every lookup served by these indexes is
    /// confined to one tenant's rows.
    ///
    /// Written by hand: EF scaffolded an empty Up(), because a partial index with a
    /// per-column DESC cannot be expressed through the model, so the model has no diff.
    /// CREATE INDEX CONCURRENTLY is deliberately NOT used — it cannot run inside a
    /// transaction and EF migrations are transactional.
    /// </summary>
    public partial class LeadPipelineSortIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_leads_tenant_pipeline_created
                    ON leads (""TenantId"", ""PipelineId"", ""CreatedAt"" DESC, ""Id"")
                    WHERE NOT ""IsDeleted"";
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_opportunities_tenant_pipeline_created
                    ON opportunities (""TenantId"", ""PipelineId"", ""CreatedAt"" DESC, ""Id"")
                    WHERE NOT ""IsDeleted"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_leads_tenant_pipeline_created;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_opportunities_tenant_pipeline_created;");
        }
    }
}
