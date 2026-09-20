using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <summary>
    /// Adds the invitation lifecycle and the team-administration audit trail.
    ///
    /// Written by hand rather than scaffolded: a concurrently-developed migration
    /// (TenantOnboarding) regenerated the model snapshot at a moment when these entities
    /// already existed on disk, so the snapshot absorbed them and `migrations add` then saw
    /// no diff and produced an empty Up(). The body below matches the snapshot exactly — the
    /// alternative was two tables present in the model and created by no migration, which
    /// fails at runtime on every tenant database.
    /// </summary>
    public partial class TeamInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "team_invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    // The BCrypt hash of the token, never the token. Cleared outright on
                    // revoke and on accept, which is what makes a spent token unusable at
                    // the data level rather than only by a status check.
                    TokenHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TokenPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SendCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_invitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    // Deliberately no FK to users: the trail has to survive the row it
                    // describes, and an invitation event has no user at all until accepted.
                    table.PrimaryKey("PK_user_audit_logs", x => x.Id);
                });

            // The acceptance lookup: narrow by prefix, then verify the full token against the
            // hash. Without it every acceptance scans the table.
            migrationBuilder.CreateIndex(
                name: "IX_TeamInvitations_TenantId_TokenPrefix",
                table: "team_invitations",
                columns: new[] { "TenantId", "TokenPrefix" });

            // Drives the duplicate-invite check and the list page's filter.
            migrationBuilder.CreateIndex(
                name: "IX_TeamInvitations_TenantId_Email",
                table: "team_invitations",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAuditLogs_TenantId_TargetUserId_OccurredAt",
                table: "user_audit_logs",
                columns: new[] { "TenantId", "TargetUserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "team_invitations");
            migrationBuilder.DropTable(name: "user_audit_logs");
        }
    }
}
