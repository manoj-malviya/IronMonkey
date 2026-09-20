using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class ConfigurationWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pipeline_stages_TenantId_Order",
                table: "pipeline_stages");

            migrationBuilder.AddColumn<string>(
                name: "DefaultValue",
                table: "custom_field_definitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldKey",
                table: "custom_field_definitions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HelpText",
                table: "custom_field_definitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "custom_field_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_TenantId_Order",
                table: "pipeline_stages",
                columns: new[] { "TenantId", "Order" });

            // Every existing row gets FieldKey "" from the AddColumn default, which would
            // collide instantly under the unique index below. Derive a key from the label
            // the same way CustomFieldDefinition.DeriveKey does: lowercase, non-alphanumerics
            // collapsed to single underscores, trimmed.
            migrationBuilder.Sql(@"
                UPDATE custom_field_definitions
                SET ""FieldKey"" = NULLIF(trim(both '_' from regexp_replace(lower(""FieldName""), '[^a-z0-9]+', '_', 'g')), '')
                WHERE ""FieldKey"" = '' OR ""FieldKey"" IS NULL;
            ");

            // A label of only punctuation derives to nothing; DeriveKey falls back to 'field'.
            migrationBuilder.Sql(@"
                UPDATE custom_field_definitions
                SET ""FieldKey"" = 'field'
                WHERE ""FieldKey"" IS NULL;
            ");

            // Two pre-existing fields can legitimately derive the same key (""Region?"" and
            // ""Region""). Suffix the duplicates so the unique index can be created — the
            // Admin can rename them afterwards.
            migrationBuilder.Sql(@"
                UPDATE custom_field_definitions c
                SET ""FieldKey"" = c.""FieldKey"" || '_' || d.rn
                FROM (
                    SELECT ""Id"",
                           row_number() OVER (
                               PARTITION BY ""TenantId"", ""AppliesTo"", ""FieldKey""
                               ORDER BY ""CreatedAt"", ""Id""
                           ) - 1 AS rn
                    FROM custom_field_definitions
                ) d
                WHERE c.""Id"" = d.""Id"" AND d.rn > 0;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_scope_key_unique",
                table: "custom_field_definitions",
                columns: new[] { "TenantId", "AppliesTo", "FieldKey" },
                unique: true);

            // Case-insensitive uniqueness of stage names within a tenant. EF cannot express an
            // expression index, so it is declared here. Existing duplicates would block this,
            // so they are disambiguated first the same way field keys are.
            migrationBuilder.Sql(@"
                UPDATE pipeline_stages p
                SET ""Name"" = left(p.""Name"", 90) || ' (' || d.rn || ')'
                FROM (
                    SELECT ""Id"",
                           row_number() OVER (
                               PARTITION BY ""TenantId"", lower(""Name"")
                               ORDER BY ""Order"", ""Id""
                           ) - 1 AS rn
                    FROM pipeline_stages
                ) d
                WHERE p.""Id"" = d.""Id"" AND d.rn > 0;
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ix_pipeline_stages_tenant_name_unique
                ON pipeline_stages (""TenantId"", lower(""Name""));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_pipeline_stages_tenant_name_unique;");

            migrationBuilder.DropIndex(
                name: "IX_pipeline_stages_TenantId_Order",
                table: "pipeline_stages");

            migrationBuilder.DropIndex(
                name: "ix_custom_field_definitions_tenant_scope_key_unique",
                table: "custom_field_definitions");

            migrationBuilder.DropColumn(
                name: "DefaultValue",
                table: "custom_field_definitions");

            migrationBuilder.DropColumn(
                name: "FieldKey",
                table: "custom_field_definitions");

            migrationBuilder.DropColumn(
                name: "HelpText",
                table: "custom_field_definitions");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "custom_field_definitions");

            // Reinstating uniqueness on Order requires there to be no duplicates left behind
            // by a reorder performed while it was relaxed. Renumber each tenant's stages to a
            // dense 1..n sequence in their current order first.
            migrationBuilder.Sql(@"
                UPDATE pipeline_stages p
                SET ""Order"" = d.rn
                FROM (
                    SELECT ""Id"",
                           row_number() OVER (
                               PARTITION BY ""TenantId"" ORDER BY ""Order"", ""Id""
                           ) AS rn
                    FROM pipeline_stages
                ) d
                WHERE p.""Id"" = d.""Id"" AND p.""Order"" <> d.rn;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_TenantId_Order",
                table: "pipeline_stages",
                columns: new[] { "TenantId", "Order" },
                unique: true);
        }
    }
}
