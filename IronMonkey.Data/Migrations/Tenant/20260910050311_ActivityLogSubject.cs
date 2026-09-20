using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class ActivityLogSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActivityLogs_leads_LeadId",
                table: "ActivityLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "LeadId",
                table: "ActivityLogs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "SubjectId",
                table: "ActivityLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "SubjectType",
                table: "ActivityLogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Lead");

            // Every pre-existing row was lead-scoped, so point its subject at that lead.
            // SubjectType already defaults to 'Lead'.
            migrationBuilder.Sql(@"
                UPDATE ""ActivityLogs""
                SET ""SubjectId"" = ""LeadId""
                WHERE ""SubjectId"" = '00000000-0000-0000-0000-000000000000'
                  AND ""LeadId"" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_TenantId_Subject_CreatedAt",
                table: "ActivityLogs",
                columns: new[] { "TenantId", "SubjectType", "SubjectId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ActivityLogs_leads_LeadId",
                table: "ActivityLogs",
                column: "LeadId",
                principalTable: "leads",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActivityLogs_leads_LeadId",
                table: "ActivityLogs");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogs_TenantId_Subject_CreatedAt",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "ActivityLogs");

            migrationBuilder.DropColumn(
                name: "SubjectType",
                table: "ActivityLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "LeadId",
                table: "ActivityLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ActivityLogs_leads_LeadId",
                table: "ActivityLogs",
                column: "LeadId",
                principalTable: "leads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
