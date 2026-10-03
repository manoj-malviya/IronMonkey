using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <summary>
    /// Replaces the free-text <c>opportunities.Stage</c> column with a foreign key to a
    /// tenant-configured stage row, and adds the <c>stage_changes</c> history table.
    ///
    /// HAND-EDITED after scaffolding, deliberately. The generated version was correct about
    /// the schema and fatally wrong about the order of operations:
    ///
    ///   1. It dropped the "Stage" column near the top, before anything had read it — every
    ///      existing opportunity's stage would have been destroyed with nothing to map from.
    ///      The drop is moved to the very end, after the backfill.
    ///   2. It gave "RecordType" a default of '' (not a valid PipelineRecordType) and
    ///      "PipelineStageId" a default of the empty GUID, which no pipeline_stages row has —
    ///      the new foreign key would have been rejected for every pre-existing opportunity.
    ///      Both are now backfilled to real values before any constraint is added.
    ///
    /// The scaffolded Up() was otherwise verified to contain only this change: it touches
    /// pipeline_stages, opportunities and the new stage_changes table and nothing else.
    /// </summary>
    public partial class OpportunityStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Discriminate the existing stages ──────────────────────────────────
            // Every pipeline_stages row that exists today is a lead stage, so the column is
            // added with that as its default rather than ''. The enum is stored by name
            // (HasConversion<string>), so 'Lead' is the literal the model reads back.
            migrationBuilder.AddColumn<string>(
                name: "RecordType",
                table: "pipeline_stages",
                type: "text",
                nullable: false,
                defaultValue: "Lead");

            migrationBuilder.DropIndex(
                name: "IX_pipeline_stages_TenantId_Order",
                table: "pipeline_stages");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_TenantId_RecordType_Order",
                table: "pipeline_stages",
                columns: new[] { "TenantId", "RecordType", "Order" });

            // The case-insensitive name uniqueness index is scoped per record type now: a
            // tenant may legitimately have a lead stage and an opportunity stage both named
            // "Qualified". EF cannot express an expression index, so it is raw SQL — the same
            // reason the lead-only version was declared this way by ConfigurationWorkspace.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_pipeline_stages_tenant_name_unique;");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_pipeline_stages_tenant_record_name_unique
                ON pipeline_stages (""TenantId"", ""RecordType"", lower(""Name""));
            ");

            // ── 2. Seed the default opportunity stage set, per tenant ────────────────
            // One set per tenant that actually has opportunities. A tenant with no deals gets
            // its stages from provisioning or from the recipe instead, so seeding it here
            // would hand it a stage set it never asked for.
            //
            // gen_random_uuid() is pgcrypto/PG13+ builtin; this database is postgres:15.
            // TenantId is taken from the opportunity rows themselves rather than assumed,
            // because several tenants can share this database.
            migrationBuilder.Sql(@"
                INSERT INTO pipeline_stages
                    (""Id"", ""TenantId"", ""Name"", ""Order"", ""IsActive"", ""StageType"",
                     ""RecordType"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                SELECT gen_random_uuid(), t.""TenantId"", d.name, d.ord, true, d.stage_type,
                       'Opportunity', now(), now(), false
                FROM (SELECT DISTINCT ""TenantId"" FROM opportunities) t
                CROSS JOIN (VALUES
                    ('Qualification', 1, 'Entry'),
                    ('Proposal',      2, 'Active'),
                    ('Negotiation',   3, 'Active'),
                    ('Won',           4, 'ClosedWon'),
                    ('Lost',          5, 'ClosedLost')
                ) AS d(name, ord, stage_type)
                WHERE NOT EXISTS (
                    SELECT 1 FROM pipeline_stages p
                    WHERE p.""TenantId"" = t.""TenantId""
                      AND p.""RecordType"" = 'Opportunity'
                );
            ");

            // ── 3. Add the FK column, still unconstrained ────────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "PipelineStageId",
                table: "opportunities",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // ── 4. Backfill: map each legacy string onto a seeded stage ──────────────
            // Matched case-insensitively and whitespace-trimmed, within the row's own tenant.
            // This covers every value the product itself ever wrote, including the literal
            // 'Lost' that MarkAsLost() assigned.
            migrationBuilder.Sql(@"
                UPDATE opportunities o
                SET ""PipelineStageId"" = p.""Id""
                FROM pipeline_stages p
                WHERE p.""TenantId"" = o.""TenantId""
                  AND p.""RecordType"" = 'Opportunity'
                  AND lower(btrim(o.""Stage"")) = lower(p.""Name"")
                  AND o.""PipelineStageId"" = '00000000-0000-0000-0000-000000000000';
            ");

            // The column was free text, so it can hold anything: a value from a build that
            // predates the current stage list, something typed straight into the database, an
            // empty string, or NULL. Those rows must not be dropped and must not be left
            // pointing at a stage that does not exist, so each tenant with any unmatched row
            // gets one extra stage to hold them.
            //
            // It is Active, not terminal: whatever these deals are, they are not known to be
            // closed, and classifying them as won or lost would corrupt revenue reporting.
            // It is named distinctively so an Admin can find, re-file and then deactivate it.
            migrationBuilder.Sql(@"
                INSERT INTO pipeline_stages
                    (""Id"", ""TenantId"", ""Name"", ""Order"", ""IsActive"", ""StageType"",
                     ""RecordType"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                SELECT gen_random_uuid(), u.""TenantId"", 'Unsorted (migrated)',
                       COALESCE(
                           (SELECT max(p.""Order"") + 1 FROM pipeline_stages p
                            WHERE p.""TenantId"" = u.""TenantId""
                              AND p.""RecordType"" = 'Opportunity'), 1),
                       true, 'Active', 'Opportunity', now(), now(), false
                FROM (
                    SELECT DISTINCT ""TenantId"" FROM opportunities
                    WHERE ""PipelineStageId"" = '00000000-0000-0000-0000-000000000000'
                ) u
                WHERE NOT EXISTS (
                    SELECT 1 FROM pipeline_stages p
                    WHERE p.""TenantId"" = u.""TenantId""
                      AND p.""RecordType"" = 'Opportunity'
                      AND lower(p.""Name"") = lower('Unsorted (migrated)')
                );
            ");

            migrationBuilder.Sql(@"
                UPDATE opportunities o
                SET ""PipelineStageId"" = p.""Id""
                FROM pipeline_stages p
                WHERE p.""TenantId"" = o.""TenantId""
                  AND p.""RecordType"" = 'Opportunity'
                  AND lower(p.""Name"") = lower('Unsorted (migrated)')
                  AND o.""PipelineStageId"" = '00000000-0000-0000-0000-000000000000';
            ");

            // Nothing may reach the foreign key still holding the placeholder. If any row
            // does, the mapping above has a hole and failing loudly here is far better than
            // creating a constraint that silently rejects it later, or worse, succeeding with
            // an orphan. Soft-deleted rows count too — they are still rows in the table.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE unmapped bigint;
                BEGIN
                    SELECT count(*) INTO unmapped FROM opportunities
                    WHERE ""PipelineStageId"" = '00000000-0000-0000-0000-000000000000';

                    IF unmapped > 0 THEN
                        RAISE EXCEPTION
                            'OpportunityStages migration: % opportunity row(s) could not be mapped to a stage.',
                            unmapped;
                    END IF;
                END $$;
            ");

            // ── 5. The history table, then its opening balance ───────────────────────
            migrationBuilder.CreateTable(
                name: "stage_changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "text", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToStageId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stage_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stage_changes_pipeline_stages_FromStageId",
                        column: x => x.FromStageId,
                        principalTable: "pipeline_stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stage_changes_pipeline_stages_ToStageId",
                        column: x => x.ToStageId,
                        principalTable: "pipeline_stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stage_changes_FromStageId",
                table: "stage_changes",
                column: "FromStageId");

            migrationBuilder.CreateIndex(
                name: "IX_stage_changes_ToStageId",
                table: "stage_changes",
                column: "ToStageId");

            migrationBuilder.CreateIndex(
                name: "IX_stage_changes_TenantId_Record_OccurredAt",
                table: "stage_changes",
                columns: new[] { "TenantId", "RecordType", "RecordId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stage_changes_TenantId_ToStageId_OccurredAt",
                table: "stage_changes",
                columns: new[] { "TenantId", "ToStageId", "OccurredAt" });

            // Every surviving opportunity gets one backfilled stage_changes row recording the
            // stage it is in now. This is marked as a system action (ChangedByUserId NULL)
            // and timed at the row's own CreatedAt rather than at migration time: stamping
            // them all with now() would report every deal in the tenant as having moved
            // simultaneously, which is a worse lie than admitting the real path is unknown.
            //
            // The pre-migration path genuinely cannot be reconstructed — the old schema
            // recorded no per-record history at all, which is the defect being fixed — so this
            // is an opening balance, not invented history.
            migrationBuilder.Sql(@"
                INSERT INTO stage_changes
                    (""Id"", ""TenantId"", ""RecordType"", ""RecordId"", ""FromStageId"", ""ToStageId"",
                     ""OccurredAt"", ""ChangedByUserId"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                SELECT gen_random_uuid(), o.""TenantId"", 'Opportunity', o.""Id"", NULL,
                       o.""PipelineStageId"", o.""CreatedAt"", NULL, now(), now(), false
                FROM opportunities o;
            ");

            // Leads get the same opening balance, so the one history model covers both record
            // types from the same instant. Their own transition data does not exist either:
            // stage_transitions is the tenant's allowed-edges graph, not a log of moves, and
            // carries no record reference to derive one from.
            migrationBuilder.Sql(@"
                INSERT INTO stage_changes
                    (""Id"", ""TenantId"", ""RecordType"", ""RecordId"", ""FromStageId"", ""ToStageId"",
                     ""OccurredAt"", ""ChangedByUserId"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                SELECT gen_random_uuid(), l.""TenantId"", 'Lead', l.""Id"", NULL,
                       l.""PipelineStageId"", l.""CreatedAt"", NULL, now(), now(), false
                FROM leads l
                WHERE EXISTS (
                    SELECT 1 FROM pipeline_stages p WHERE p.""Id"" = l.""PipelineStageId""
                );
            ");

            // ── 6. Only now is the legacy column safe to remove ──────────────────────
            migrationBuilder.DropIndex(
                name: "IX_opportunities_Stage",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "opportunities");

            // ── 7. Constraints and indexes, over data that already satisfies them ────
            migrationBuilder.CreateIndex(
                name: "IX_opportunities_PipelineStageId",
                table: "opportunities",
                column: "PipelineStageId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_TenantId_PipelineStageId",
                table: "opportunities",
                columns: new[] { "TenantId", "PipelineStageId" });

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_pipeline_stages_PipelineStageId",
                table: "opportunities",
                column: "PipelineStageId",
                principalTable: "pipeline_stages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_pipeline_stages_PipelineStageId",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_PipelineStageId",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_TenantId_PipelineStageId",
                table: "opportunities");

            // Restore the text column and write the stage names back into it before the
            // stage rows are gone, so a rollback is not itself a data-loss event.
            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "opportunities",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE opportunities o
                SET ""Stage"" = left(p.""Name"", 50)
                FROM pipeline_stages p
                WHERE p.""Id"" = o.""PipelineStageId"";
            ");

            migrationBuilder.DropColumn(
                name: "PipelineStageId",
                table: "opportunities");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_Stage",
                table: "opportunities",
                column: "Stage");

            migrationBuilder.DropTable(
                name: "stage_changes");

            // The opportunity stages themselves only exist because of this migration, so they
            // go with it. Lead stages are left untouched.
            migrationBuilder.Sql(@"DELETE FROM pipeline_stages WHERE ""RecordType"" = 'Opportunity';");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_pipeline_stages_tenant_record_name_unique;");

            migrationBuilder.DropIndex(
                name: "IX_pipeline_stages_TenantId_RecordType_Order",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "RecordType",
                table: "pipeline_stages");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_TenantId_Order",
                table: "pipeline_stages",
                columns: new[] { "TenantId", "Order" });

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_pipeline_stages_tenant_name_unique
                ON pipeline_stages (""TenantId"", lower(""Name""));
            ");
        }
    }
}
