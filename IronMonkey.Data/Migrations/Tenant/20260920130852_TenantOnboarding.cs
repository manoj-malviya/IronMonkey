using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <summary>
    /// Adds the per-tenant, per-user record of a dismissed first-run setup checklist.
    ///
    /// Scoped deliberately to this one table. EF scaffolded it alongside the team-invitation
    /// tables, which belong to separate work in flight and carry their own migration — two
    /// migrations both creating them would make whichever ran second fail. Those statements
    /// were removed here by hand so this migration owns only what this feature added.
    /// </summary>
    public partial class TenantOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_onboarding_dismissals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DismissedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_onboarding_dismissals", x => x.Id);
                });

            // One row per user per tenant. Two rows could disagree, and whichever the read
            // happened to pick would make a dismissal look intermittent.
            migrationBuilder.CreateIndex(
                name: "IX_TenantOnboardingDismissals_TenantId_UserId",
                table: "tenant_onboarding_dismissals",
                columns: new[] { "TenantId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_onboarding_dismissals");
        }
    }
}
