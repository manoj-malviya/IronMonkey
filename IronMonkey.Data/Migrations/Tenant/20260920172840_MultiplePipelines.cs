using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <summary>
    /// Introduces named pipelines: a tenant may run more than one lead funnel and more than
    /// one deal funnel, each owning its own ordered stages, and each record belongs to
    /// exactly one.
    ///
    /// <para><b>HAND-EDITED after scaffolding, deliberately</b> — for the same class of reason
    /// Part A's <c>OpportunityStages</c> migration was. The generated version described the
    /// right schema and would have destroyed the database applying it:</para>
    ///
    /// <list type="number">
    /// <item>It gave <c>pipeline_stages.PipelineId</c>, <c>leads.PipelineId</c> and
    ///   <c>opportunities.PipelineId</c> a default of the empty GUID, which no <c>pipelines</c>
    ///   row has, and then added the foreign keys immediately. Every pre-existing stage, lead
    ///   and opportunity would have been rejected by its own new constraint. All three are now
    ///   backfilled to real pipeline rows <b>before</b> any foreign key is created.</item>
    /// <item>It created no pipelines at all, so there was nothing to backfill <i>to</i>. The
    ///   default pipelines are now created first, one pair per tenant.</item>
    /// <item>It left <c>stage_changes.ToPipelineId</c> at the empty GUID for every historical
    ///   row, which would have made every past move look like it happened in a pipeline that
    ///   does not exist and silently excluded it from every per-pipeline velocity report.</item>
    /// </list>
    ///
    /// <para>
    /// <b>The no-orphaned-stage guarantee.</b> Requirement: no tenant may be left with stages
    /// belonging to no pipeline. Three things enforce it together, and the third is what makes
    /// it a guarantee rather than a hope:
    /// </para>
    /// <list type="number">
    /// <item>A default pipeline is created for <b>every</b> (tenant, record type) pair present
    ///   in <c>pipeline_stages</c>, derived from the stage rows themselves rather than assumed —
    ///   several tenants can share this database.</item>
    /// <item>Every stage is then backfilled to its tenant's default pipeline of its own record
    ///   type, matching on both columns.</item>
    /// <item>A <c>DO $$ ... RAISE EXCEPTION</c> guard aborts the whole migration if a single
    ///   stage, lead or opportunity is still holding the placeholder. Failing loudly here is far
    ///   better than creating a constraint that rejects the row later with no context — or, far
    ///   worse, succeeding and leaving an orphan behind.</item>
    /// </list>
    ///
    /// <para>
    /// Every raw-SQL statement below carries an explicit <c>TenantId</c> predicate or derives
    /// its tenant from the rows it is reading. Raw SQL bypasses the global query filters, and
    /// this database holds several tenants.
    /// </para>
    ///
    /// <para>
    /// Verified after scaffolding: the generated <c>Up()</c> touched only <c>pipelines</c> and
    /// the <c>PipelineId</c>/<c>ToPipelineId</c>/<c>FromPipelineId</c> columns and their
    /// indexes. It did <b>not</b> fold in Part A's uncommitted <c>OpportunityStages</c> tables,
    /// and it was not empty. The schema operations below are exactly the generated ones,
    /// reordered around the backfill and with the two expression indexes added by hand.
    /// </para>
    /// </summary>
    public partial class MultiplePipelines : Migration
    {
        /// <summary>
        /// The placeholder every new non-nullable GUID column is added with, and which nothing
        /// may still be holding by the time the foreign keys are created.
        /// </summary>
        private const string Placeholder = "00000000-0000-0000-0000-000000000000";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. The pipelines table itself ────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "pipelines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordType = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipelines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pipelines_TenantId_RecordType_Order",
                table: "pipelines",
                columns: new[] { "TenantId", "RecordType", "Order" });

            // ── 2. One default pipeline per (tenant, record type) that has stages ────
            //
            // The tenant/record-type pairs come from pipeline_stages itself rather than from
            // any assumption about which tenants live here. A tenant with lead stages but no
            // opportunity stages gets one pipeline, not two — seeding a funnel a tenant has
            // never had is not this migration's business.
            //
            // gen_random_uuid() is a PG13+ builtin; this database is postgres:15, as Part A's
            // migration already relies on.
            //
            // IsDefault is true for each, which is exactly one per (tenant, record type) here
            // because the source is a DISTINCT over those two columns. The filtered unique
            // index created in step 7 then pins that invariant for everything afterwards.
            migrationBuilder.Sql($@"
                INSERT INTO pipelines
                    (""Id"", ""TenantId"", ""Name"", ""RecordType"", ""IsDefault"", ""IsActive"",
                     ""Order"", ""Description"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                SELECT gen_random_uuid(), s.""TenantId"", 'Default', s.""RecordType"",
                       true, true, 1,
                       'Created automatically when multiple pipelines were introduced.',
                       now(), now(), false
                FROM (
                    SELECT DISTINCT ""TenantId"", ""RecordType"" FROM pipeline_stages
                ) s
                WHERE NOT EXISTS (
                    SELECT 1 FROM pipelines p
                    WHERE p.""TenantId"" = s.""TenantId""
                      AND p.""RecordType"" = s.""RecordType""
                );
            ");

            // A tenant can hold leads or deals while every one of its stages is soft-deleted,
            // and the DISTINCT above reads pipeline_stages including those rows — but a tenant
            // whose stage table is genuinely empty would get no pipeline and then fail the
            // guard in step 5 with records to place. Cover both record types from the record
            // tables as well, so the set of pipelines created is the union of what the stages
            // need and what the records need.
            migrationBuilder.Sql($@"
                INSERT INTO pipelines
                    (""Id"", ""TenantId"", ""Name"", ""RecordType"", ""IsDefault"", ""IsActive"",
                     ""Order"", ""Description"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                SELECT gen_random_uuid(), t.""TenantId"", 'Default', t.rt,
                       true, true, 1,
                       'Created automatically when multiple pipelines were introduced.',
                       now(), now(), false
                FROM (
                    SELECT DISTINCT ""TenantId"", 'Lead'::text AS rt FROM leads
                    UNION
                    SELECT DISTINCT ""TenantId"", 'Opportunity'::text AS rt FROM opportunities
                ) t
                WHERE NOT EXISTS (
                    SELECT 1 FROM pipelines p
                    WHERE p.""TenantId"" = t.""TenantId""
                      AND p.""RecordType"" = t.rt
                );
            ");

            // ── 3. The membership columns, still unconstrained ───────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "pipeline_stages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid(Placeholder));

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "leads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid(Placeholder));

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "opportunities",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid(Placeholder));

            // ── 4. Backfill, stages first ────────────────────────────────────────────
            //
            // Matched on BOTH TenantId and RecordType. Matching on tenant alone would file a
            // tenant's opportunity stages into its lead pipeline, which is precisely the
            // cross-pipeline corruption this whole feature exists to prevent.
            migrationBuilder.Sql($@"
                UPDATE pipeline_stages s
                SET ""PipelineId"" = p.""Id""
                FROM pipelines p
                WHERE p.""TenantId"" = s.""TenantId""
                  AND p.""RecordType"" = s.""RecordType""
                  AND p.""IsDefault""
                  AND s.""PipelineId"" = '{Placeholder}';
            ");

            // Records take their pipeline from the stage they are already in — that IS the
            // definition of a record's pipeline, so this derives the value rather than
            // guessing it. Soft-deleted stages are joined too (no IsDeleted predicate): a
            // record sitting in one still has to land somewhere valid.
            migrationBuilder.Sql($@"
                UPDATE leads l
                SET ""PipelineId"" = s.""PipelineId""
                FROM pipeline_stages s
                WHERE s.""Id"" = l.""PipelineStageId""
                  AND s.""TenantId"" = l.""TenantId""
                  AND l.""PipelineId"" = '{Placeholder}';
            ");

            migrationBuilder.Sql($@"
                UPDATE opportunities o
                SET ""PipelineId"" = s.""PipelineId""
                FROM pipeline_stages s
                WHERE s.""Id"" = o.""PipelineStageId""
                  AND s.""TenantId"" = o.""TenantId""
                  AND o.""PipelineId"" = '{Placeholder}';
            ");

            // A record whose stage row has vanished entirely (a hard delete predating the
            // restrict constraints) has no stage to derive from. It still must not be left
            // orphaned, so it falls back to its tenant's default pipeline of the right record
            // type. Its stage id is left alone — repointing it would silently move the record
            // to a stage nobody chose, and the row is visibly broken either way.
            migrationBuilder.Sql($@"
                UPDATE leads l
                SET ""PipelineId"" = p.""Id""
                FROM pipelines p
                WHERE p.""TenantId"" = l.""TenantId""
                  AND p.""RecordType"" = 'Lead'
                  AND p.""IsDefault""
                  AND l.""PipelineId"" = '{Placeholder}';
            ");

            migrationBuilder.Sql($@"
                UPDATE opportunities o
                SET ""PipelineId"" = p.""Id""
                FROM pipelines p
                WHERE p.""TenantId"" = o.""TenantId""
                  AND p.""RecordType"" = 'Opportunity'
                  AND p.""IsDefault""
                  AND o.""PipelineId"" = '{Placeholder}';
            ");

            // ── 5. Fail loudly rather than constrain over bad data ───────────────────
            //
            // This is the guarantee that no tenant is left with an orphaned stage — or an
            // orphaned record. If any row still holds the placeholder, the mapping above has a
            // hole, and aborting here leaves the database exactly as it was found rather than
            // half-migrated with a constraint nobody can satisfy.
            //
            // Soft-deleted rows are counted too: they are still rows in the table, and the
            // foreign key does not care about IsDeleted.
            migrationBuilder.Sql($@"
                DO $$
                DECLARE
                    orphan_stages bigint;
                    orphan_leads bigint;
                    orphan_opportunities bigint;
                BEGIN
                    SELECT count(*) INTO orphan_stages FROM pipeline_stages
                    WHERE ""PipelineId"" = '{Placeholder}';

                    SELECT count(*) INTO orphan_leads FROM leads
                    WHERE ""PipelineId"" = '{Placeholder}';

                    SELECT count(*) INTO orphan_opportunities FROM opportunities
                    WHERE ""PipelineId"" = '{Placeholder}';

                    IF orphan_stages > 0 OR orphan_leads > 0 OR orphan_opportunities > 0 THEN
                        RAISE EXCEPTION
                            'MultiplePipelines migration: % stage(s), % lead(s) and % opportunity row(s) could not be attached to a pipeline.',
                            orphan_stages, orphan_leads, orphan_opportunities;
                    END IF;
                END $$;
            ");

            // ── 6. History gains its pipeline context ────────────────────────────────
            migrationBuilder.AddColumn<Guid>(
                name: "FromPipelineId",
                table: "stage_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ToPipelineId",
                table: "stage_changes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid(Placeholder));

            // Every historical row happened inside whatever pipeline its destination stage now
            // belongs to — which, before this migration, was the tenant's only one. Left at the
            // placeholder these rows would be silently excluded from every per-pipeline
            // velocity report, so the history would appear to start today.
            //
            // FromPipelineId is set to the SAME pipeline wherever there is a FromStageId,
            // because no cross-pipeline move can have happened before pipelines existed. It
            // stays null for a first placement, which is what null means on that column.
            migrationBuilder.Sql($@"
                UPDATE stage_changes c
                SET ""ToPipelineId"" = s.""PipelineId""
                FROM pipeline_stages s
                WHERE s.""Id"" = c.""ToStageId""
                  AND s.""TenantId"" = c.""TenantId""
                  AND c.""ToPipelineId"" = '{Placeholder}';
            ");

            migrationBuilder.Sql($@"
                UPDATE stage_changes c
                SET ""FromPipelineId"" = s.""PipelineId""
                FROM pipeline_stages s
                WHERE s.""Id"" = c.""FromStageId""
                  AND s.""TenantId"" = c.""TenantId""
                  AND c.""FromStageId"" IS NOT NULL
                  AND c.""FromPipelineId"" IS NULL;
            ");

            // A history row whose destination stage has been hard-deleted cannot be resolved
            // that way, so it falls back to the tenant's default pipeline for its own record
            // type. This column carries no foreign key precisely so history survives
            // configuration being removed, and it is better for such a row to be attributed to
            // the tenant's main funnel than to a pipeline id that never existed.
            migrationBuilder.Sql($@"
                UPDATE stage_changes c
                SET ""ToPipelineId"" = p.""Id""
                FROM pipelines p
                WHERE p.""TenantId"" = c.""TenantId""
                  AND p.""RecordType"" = c.""RecordType""
                  AND p.""IsDefault""
                  AND c.""ToPipelineId"" = '{Placeholder}';
            ");

            // ── 7. Targeting columns — nullable, so no backfill ──────────────────────
            //
            // NULL is the correct value for every pre-existing row and means "tenant-wide",
            // which is exactly what these rows meant before pipelines existed. Defaulting them
            // to the default pipeline would have silently narrowed every existing rule, field
            // and routing config the day a tenant created a second pipeline.
            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "workflow_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "custom_field_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineId",
                table: "routing_configs",
                type: "uuid",
                nullable: true);

            // ── 8. Indexes and constraints, over data that already satisfies them ────
            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_PipelineId",
                table: "pipeline_stages",
                column: "PipelineId");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_TenantId_PipelineId_Order",
                table: "pipeline_stages",
                columns: new[] { "TenantId", "PipelineId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_leads_PipelineId",
                table: "leads",
                column: "PipelineId");

            migrationBuilder.CreateIndex(
                name: "IX_leads_TenantId_PipelineId_PipelineStageId",
                table: "leads",
                columns: new[] { "TenantId", "PipelineId", "PipelineStageId" });

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_PipelineId",
                table: "opportunities",
                column: "PipelineId");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_TenantId_PipelineId_PipelineStageId",
                table: "opportunities",
                columns: new[] { "TenantId", "PipelineId", "PipelineStageId" });

            migrationBuilder.CreateIndex(
                name: "IX_stage_changes_TenantId_ToPipelineId_OccurredAt",
                table: "stage_changes",
                columns: new[] { "TenantId", "ToPipelineId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_rules_TenantId_PipelineId",
                table: "workflow_rules",
                columns: new[] { "TenantId", "PipelineId" });

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_definitions_TenantId_PipelineId",
                table: "custom_field_definitions",
                columns: new[] { "TenantId", "PipelineId" });

            migrationBuilder.CreateIndex(
                name: "IX_routing_configs_TenantId_PipelineId",
                table: "routing_configs",
                columns: new[] { "TenantId", "PipelineId" });

            // Stage name uniqueness moves from per-(tenant, record type) to per-PIPELINE. Two
            // pipelines are different funnels and both may legitimately have a "Qualified"
            // stage; keeping the old scope would have made a second pipeline unable to reuse
            // any name the first had taken. Raw SQL because EF cannot express an index over
            // lower(Name) — the same reason Part A and ConfigurationWorkspace declared theirs
            // this way.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_pipeline_stages_tenant_record_name_unique;");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_pipeline_stages_pipeline_name_unique
                ON pipeline_stages (""TenantId"", ""PipelineId"", lower(""Name""));
            ");

            // Pipeline names are unique per tenant and record type, case-insensitively: two
            // pipelines a user cannot tell apart in a picker are a configuration mistake.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_pipelines_tenant_record_name_unique
                ON pipelines (""TenantId"", ""RecordType"", lower(""Name""))
                WHERE NOT ""IsDeleted"";
            ");

            // EXACTLY ONE default per (tenant, record type). A partial unique index, which EF
            // also cannot express.
            //
            // This one carries real weight: it is what makes "no pipeline specified" resolve
            // to exactly one answer, and that single-answer property is the entire basis for
            // leaving a single-pipeline tenant's UX untouched. Zero defaults would leave every
            // existing caller with no pipeline; two would make the choice arbitrary.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_pipelines_tenant_record_single_default
                ON pipelines (""TenantId"", ""RecordType"")
                WHERE ""IsDefault"" AND NOT ""IsDeleted"";
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_pipeline_stages_pipelines_PipelineId",
                table: "pipeline_stages",
                column: "PipelineId",
                principalTable: "pipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_leads_pipelines_PipelineId",
                table: "leads",
                column: "PipelineId",
                principalTable: "pipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_opportunities_pipelines_PipelineId",
                table: "opportunities",
                column: "PipelineId",
                principalTable: "pipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_leads_pipelines_PipelineId",
                table: "leads");

            migrationBuilder.DropForeignKey(
                name: "FK_opportunities_pipelines_PipelineId",
                table: "opportunities");

            migrationBuilder.DropForeignKey(
                name: "FK_pipeline_stages_pipelines_PipelineId",
                table: "pipeline_stages");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_pipelines_tenant_record_single_default;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_pipelines_tenant_record_name_unique;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_pipeline_stages_pipeline_name_unique;");

            // Restore the per-record-type stage name index this migration replaced. A rollback
            // that left no uniqueness at all would let duplicates accumulate and then make
            // re-applying this migration fail.
            //
            // Duplicates can legitimately exist at this point — two pipelines may each have a
            // "Qualified" — so the index is created only if the data allows it, and the
            // rollback is not failed over configuration the tenant was entitled to create.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    BEGIN
                        CREATE UNIQUE INDEX ix_pipeline_stages_tenant_record_name_unique
                        ON pipeline_stages (""TenantId"", ""RecordType"", lower(""Name""));
                    EXCEPTION WHEN unique_violation THEN
                        RAISE NOTICE
                            'Stage names are no longer unique per record type (multiple pipelines share names); leaving the index off.';
                    END;
                END $$;
            ");

            migrationBuilder.DropIndex(
                name: "IX_workflow_rules_TenantId_PipelineId",
                table: "workflow_rules");

            migrationBuilder.DropIndex(
                name: "IX_stage_changes_TenantId_ToPipelineId_OccurredAt",
                table: "stage_changes");

            migrationBuilder.DropIndex(
                name: "IX_routing_configs_TenantId_PipelineId",
                table: "routing_configs");

            migrationBuilder.DropIndex(
                name: "IX_pipeline_stages_PipelineId",
                table: "pipeline_stages");

            migrationBuilder.DropIndex(
                name: "IX_pipeline_stages_TenantId_PipelineId_Order",
                table: "pipeline_stages");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_PipelineId",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_TenantId_PipelineId_PipelineStageId",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_leads_PipelineId",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_leads_TenantId_PipelineId_PipelineStageId",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_custom_field_definitions_TenantId_PipelineId",
                table: "custom_field_definitions");

            migrationBuilder.DropColumn(
                name: "PipelineId",
                table: "workflow_rules");

            migrationBuilder.DropColumn(
                name: "FromPipelineId",
                table: "stage_changes");

            migrationBuilder.DropColumn(
                name: "ToPipelineId",
                table: "stage_changes");

            migrationBuilder.DropColumn(
                name: "PipelineId",
                table: "routing_configs");

            migrationBuilder.DropColumn(
                name: "PipelineId",
                table: "pipeline_stages");

            migrationBuilder.DropColumn(
                name: "PipelineId",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "PipelineId",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "PipelineId",
                table: "custom_field_definitions");

            // Dropped last: the records and stages that referenced these rows no longer do.
            migrationBuilder.DropTable(
                name: "pipelines");
        }
    }
}
