using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class RecordVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "opportunities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "role_record_scopes",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Scope = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_record_scopes", x => new { x.RoleId, x.RecordType });
                    table.ForeignKey(
                        name: "FK_role_record_scopes_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_teams_teams_ParentTeamId",
                        column: x => x.ParentTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team_memberships",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_memberships", x => new { x.TeamId, x.UserId });
                    table.ForeignKey(
                        name: "FK_team_memberships_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // CRM record access for every existing tenant role (Owner, TeleCaller and any
            // custom role a tenant created). Before leads:*/contacts:*/opportunities:* were
            // enforced every authenticated user held them in effect, so this grants exactly the
            // access each role already had — nothing changes until an Admin narrows a role.
            // SQL rather than InsertData: an Admin may already have granted some of these by
            // hand, and a plain INSERT would fail the whole migration on the duplicate key.
            // SuperAdmin (1) already holds them; it is excluded so the platform role's grants
            // stay exactly as seeded.
            migrationBuilder.Sql(@"
                INSERT INTO role_permissions (""RoleId"", ""PermissionId"")
                SELECT r.""Id"", p.""Id""
                FROM roles r CROSS JOIN permissions p
                WHERE r.""Id"" <> 1
                  AND p.""Name"" IN ('leads:read', 'leads:write', 'contacts:read', 'contacts:write',
                                     'opportunities:read', 'opportunities:write')
                ON CONFLICT DO NOTHING;");

            // Owners for contacts and opportunities, from the lead each was converted from.
            // Anything with no converting lead stays unowned — visible under an All scope, which
            // is every role's scope until one is narrowed.
            migrationBuilder.Sql(@"
                UPDATE opportunities o SET ""OwnerUserId"" = l.""AssignedToUserId""
                FROM leads l
                WHERE l.""ConvertedOpportunityId"" = o.""Id"" AND l.""AssignedToUserId"" IS NOT NULL;

                UPDATE contacts c SET ""OwnerUserId"" = l.""AssignedToUserId""
                FROM leads l
                WHERE l.""ConvertedContactId"" = c.""Id"" AND l.""AssignedToUserId"" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_opportunities_TenantId_OwnerUserId",
                table: "opportunities",
                columns: new[] { "TenantId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_contacts_TenantId_OwnerUserId",
                table: "contacts",
                columns: new[] { "TenantId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_TenantId_UserId",
                table: "team_memberships",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_teams_ParentTeamId",
                table: "teams",
                column: "ParentTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_teams_TenantId_ManagerUserId",
                table: "teams",
                columns: new[] { "TenantId", "ManagerUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_record_scopes");

            migrationBuilder.DropTable(
                name: "team_memberships");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropIndex(
                name: "IX_opportunities_TenantId_OwnerUserId",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "IX_contacts_TenantId_OwnerUserId",
                table: "contacts");

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 4, 301 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 5, 301 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 7, 301 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 8, 301 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 10, 301 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 11, 301 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 4, 302 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 5, 302 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 7, 302 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 8, 302 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 10, 302 });

            migrationBuilder.DeleteData(
                table: "role_permissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { 11, 302 });

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "contacts");
        }
    }
}
