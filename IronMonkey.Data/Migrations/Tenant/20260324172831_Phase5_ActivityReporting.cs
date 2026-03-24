using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class Phase5_ActivityReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_lead_tasks_TenantId_AssignedToUserId",
                table: "lead_tasks",
                newName: "IX_LeadTasks_TenantId_AssignedToUserId");

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "opportunities",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    OldValues = table.Column<string>(type: "jsonb", nullable: true),
                    NewValues = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityLogs_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityLogs_leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId_AssignedToUserId",
                table: "leads",
                columns: new[] { "TenantId", "AssignedToUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId_CreatedAt",
                table: "leads",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId_PipelineStageId",
                table: "leads",
                columns: new[] { "TenantId", "PipelineStageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId_Source",
                table: "leads",
                columns: new[] { "TenantId", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadTasks_TenantId_Status",
                table: "lead_tasks",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_ActorId",
                table: "ActivityLogs",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_CreatedAt",
                table: "ActivityLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_LeadId",
                table: "ActivityLogs",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_TenantId_EventType",
                table: "ActivityLogs",
                columns: new[] { "TenantId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_TenantId_LeadId",
                table: "ActivityLogs",
                columns: new[] { "TenantId", "LeadId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogs");

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId_AssignedToUserId",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId_CreatedAt",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId_PipelineStageId",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId_Source",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "IX_LeadTasks_TenantId_Status",
                table: "lead_tasks");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "opportunities");

            migrationBuilder.RenameIndex(
                name: "IX_LeadTasks_TenantId_AssignedToUserId",
                table: "lead_tasks",
                newName: "IX_lead_tasks_TenantId_AssignedToUserId");
        }
    }
}
