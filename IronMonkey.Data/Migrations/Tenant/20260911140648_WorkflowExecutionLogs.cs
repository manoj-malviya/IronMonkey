using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class WorkflowExecutionLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_execution_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowRuleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    ErrorCategory = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(501)", maxLength: 501, nullable: true),
                    SkipReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    JobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    ConditionEvaluated = table.Column<bool>(type: "boolean", nullable: false),
                    ConditionMatched = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_execution_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_execution_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowExecutionLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    ErrorCategory = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "character varying(501)", maxLength: 501, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    TargetHost = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RecipientRedacted = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_execution_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workflow_execution_steps_workflow_execution_logs_WorkflowEx~",
                        column: x => x.WorkflowExecutionLogId,
                        principalTable: "workflow_execution_logs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "Id", "Name" },
                values: new object[] { 18, "workflow:logs:read" });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { 18, 1 },
                    { 18, 201 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_Status_StartedAt",
                table: "workflow_execution_logs",
                columns: new[] { "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_TenantId_LeadId_StartedAt",
                table: "workflow_execution_logs",
                columns: new[] { "TenantId", "LeadId", "StartedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_TenantId_RuleId_StartedAt",
                table: "workflow_execution_logs",
                columns: new[] { "TenantId", "WorkflowRuleId", "StartedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_TenantId_StartedAt",
                table: "workflow_execution_logs",
                columns: new[] { "TenantId", "StartedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_TenantId_Status_StartedAt",
                table: "workflow_execution_logs",
                columns: new[] { "TenantId", "Status", "StartedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "UX_WorkflowExecutionLogs_TenantId_Correlation_Rule_Attempt",
                table: "workflow_execution_logs",
                columns: new[] { "TenantId", "CorrelationId", "WorkflowRuleId", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionSteps_LogId_Sequence",
                table: "workflow_execution_steps",
                columns: new[] { "WorkflowExecutionLogId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionSteps_TenantId_ActionType",
                table: "workflow_execution_steps",
                columns: new[] { "TenantId", "ActionType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_execution_steps");

            migrationBuilder.DropTable(
                name: "workflow_execution_logs");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 18, 1 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 18, 201 });

            migrationBuilder.DeleteData(
                table: "permissions",
                keyColumn: "Id",
                keyValue: 18);
        }
    }
}
