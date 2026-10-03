using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class SearchViewsReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSearchable",
                table: "custom_field_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MobileDigits",
                table: "leads",
                type: "text",
                nullable: false,
                computedColumnSql: "regexp_replace(\"Mobile\", '[^0-9]', '', 'g')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileDigits",
                table: "contacts",
                type: "text",
                nullable: false,
                computedColumnSql: "regexp_replace(\"Mobile\", '[^0-9]', '', 'g')",
                stored: true);

            migrationBuilder.CreateTable(
                name: "export_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: true),
                    WasTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content = table.Column<byte[]>(type: "bytea", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "report_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    Schedule = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "saved_views",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saved_views", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "report_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RanAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_report_runs_report_definitions_ReportDefinitionId",
                        column: x => x.ReportDefinitionId,
                        principalTable: "report_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_default_views",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SavedViewId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_default_views", x => new { x.UserId, x.RecordType });
                    table.ForeignKey(
                        name: "FK_user_default_views_saved_views_SavedViewId",
                        column: x => x.SavedViewId,
                        principalTable: "saved_views",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "Id", "Name" },
                values: new object[] { 23, "data:export" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { 23, 1 },
                    { 23, 201 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_export_jobs_TenantId_RequestedByUserId_CreatedAt",
                table: "export_jobs",
                columns: new[] { "TenantId", "RequestedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_report_definitions_TenantId_OwnerUserId",
                table: "report_definitions",
                columns: new[] { "TenantId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_report_runs_ReportDefinitionId",
                table: "report_runs",
                column: "ReportDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_report_runs_TenantId_ReportDefinitionId_RanAt",
                table: "report_runs",
                columns: new[] { "TenantId", "ReportDefinitionId", "RanAt" });

            migrationBuilder.CreateIndex(
                name: "IX_saved_views_TenantId_RecordType_OwnerUserId",
                table: "saved_views",
                columns: new[] { "TenantId", "RecordType", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_user_default_views_SavedViewId",
                table: "user_default_views",
                column: "SavedViewId");

            // ── Global search indexes ───────────────────────────────────────────────────
            //
            // Trigram GIN indexes make ILIKE '%term%' an index scan instead of a sequential
            // scan of every row. PostgreSQL rather than an external search engine: tenants are
            // database-per-tenant, and an external index would be one more thing to provision
            // and reindex per tenant. pg_trgm ships with PostgreSQL.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_leads_trgm_FirstName"" ON leads USING gin (""FirstName"" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_leads_trgm_LastName"" ON leads USING gin (""LastName"" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_leads_trgm_Email"" ON leads USING gin (""Email"" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_leads_trgm_MobileDigits"" ON leads USING gin (""MobileDigits"" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_leads_trgm_custom"" ON leads USING gin ((custom_field_values::text) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_contacts_trgm_Name"" ON contacts USING gin (""Name"" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_contacts_trgm_Email"" ON contacts USING gin (""Email"" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_contacts_trgm_MobileDigits"" ON contacts USING gin (""MobileDigits"" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_contacts_trgm_custom"" ON contacts USING gin ((custom_field_values::text) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_opportunities_trgm_Title"" ON opportunities USING gin (""Title"" gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ""IX_lead_tasks_trgm_Title"" ON lead_tasks USING gin (""Title"" gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_leads_trgm_FirstName""; DROP INDEX IF EXISTS ""IX_leads_trgm_LastName"";
                DROP INDEX IF EXISTS ""IX_leads_trgm_Email""; DROP INDEX IF EXISTS ""IX_leads_trgm_MobileDigits"";
                DROP INDEX IF EXISTS ""IX_leads_trgm_custom""; DROP INDEX IF EXISTS ""IX_contacts_trgm_Name"";
                DROP INDEX IF EXISTS ""IX_contacts_trgm_Email""; DROP INDEX IF EXISTS ""IX_contacts_trgm_MobileDigits"";
                DROP INDEX IF EXISTS ""IX_contacts_trgm_custom""; DROP INDEX IF EXISTS ""IX_opportunities_trgm_Title"";
                DROP INDEX IF EXISTS ""IX_lead_tasks_trgm_Title"";");
            migrationBuilder.DropTable(
                name: "export_jobs");

            migrationBuilder.DropTable(
                name: "report_runs");

            migrationBuilder.DropTable(
                name: "user_default_views");

            migrationBuilder.DropTable(
                name: "report_definitions");

            migrationBuilder.DropTable(
                name: "saved_views");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 23, 1 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 23, 201 });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DropColumn(
                name: "MobileDigits",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "MobileDigits",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "IsSearchable",
                table: "custom_field_definitions");
        }
    }
}
