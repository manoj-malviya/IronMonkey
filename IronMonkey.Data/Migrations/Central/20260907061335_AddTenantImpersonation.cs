using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Central
{
    /// <inheritdoc />
    public partial class AddTenantImpersonation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantImpersonations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImpersonatedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImpersonatedUserEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantImpersonations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantImpersonations_PlatformUserId",
                table: "TenantImpersonations",
                column: "PlatformUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantImpersonations_TenantId",
                table: "TenantImpersonations",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantImpersonations");
        }
    }
}
