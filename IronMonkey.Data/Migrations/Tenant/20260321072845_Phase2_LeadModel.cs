using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class Phase2_LeadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeadSource",
                table: "leads");

            migrationBuilder.AddColumn<Guid>(
                name: "PipelineStageId",
                table: "leads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "leads",
                type: "text",
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "custom_field_values",
                table: "leads",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "custom_field_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FieldType = table.Column<string>(type: "text", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Options = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_field_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lead_merges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetLeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    MergedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MergedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSnapshot = table.Column<string>(type: "text", nullable: false),
                    TargetSnapshot = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lead_merges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lead_merges_leads_SourceLeadId",
                        column: x => x.SourceLeadId,
                        principalTable: "leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lead_merges_leads_TargetLeadId",
                        column: x => x.TargetLeadId,
                        principalTable: "leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_stages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_leads_PipelineStageId",
                table: "leads",
                column: "PipelineStageId");

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_definitions_TenantId",
                table: "custom_field_definitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_lead_merges_SourceLeadId",
                table: "lead_merges",
                column: "SourceLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_lead_merges_TargetLeadId",
                table: "lead_merges",
                column: "TargetLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_lead_merges_TenantId",
                table: "lead_merges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_stages_TenantId_Order",
                table: "pipeline_stages",
                columns: new[] { "TenantId", "Order" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_leads_pipeline_stages_PipelineStageId",
                table: "leads",
                column: "PipelineStageId",
                principalTable: "pipeline_stages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_leads_pipeline_stages_PipelineStageId",
                table: "leads");

            migrationBuilder.DropTable(
                name: "custom_field_definitions");

            migrationBuilder.DropTable(
                name: "lead_merges");

            migrationBuilder.DropTable(
                name: "pipeline_stages");

            migrationBuilder.DropIndex(
                name: "IX_leads_PipelineStageId",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "PipelineStageId",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "custom_field_values",
                table: "leads");

            migrationBuilder.AddColumn<string>(
                name: "LeadSource",
                table: "leads",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
