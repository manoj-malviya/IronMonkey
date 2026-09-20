using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Central
{
    /// <inheritdoc />
    public partial class UniqueUserTenantIndexEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserTenantIndex_Email",
                table: "UserTenantIndex");

            // Emails are now stored normalised, so fold existing rows before adding the
            // unique index — otherwise "A@b.com" and "a@b.com" would survive as duplicates.
            migrationBuilder.Sql(@"UPDATE ""UserTenantIndex"" SET ""Email"" = lower(trim(""Email""));");

            // The old index allowed duplicates, so any that accumulated would fail the
            // unique index below. Keep the oldest row per email — it is the one whose
            // tenant the user has been logging into.
            migrationBuilder.Sql(@"
                DELETE FROM ""UserTenantIndex"" a
                USING ""UserTenantIndex"" b
                WHERE a.""Email"" = b.""Email"" AND a.""Id"" > b.""Id"";");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenantIndex_Email",
                table: "UserTenantIndex",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserTenantIndex_Email",
                table: "UserTenantIndex");

            // Emails are now stored normalised, so fold existing rows before adding the
            // unique index — otherwise "A@b.com" and "a@b.com" would survive as duplicates.
            migrationBuilder.Sql(@"UPDATE ""UserTenantIndex"" SET ""Email"" = lower(trim(""Email""));");

            // The old index allowed duplicates, so any that accumulated would fail the
            // unique index below. Keep the oldest row per email — it is the one whose
            // tenant the user has been logging into.
            migrationBuilder.Sql(@"
                DELETE FROM ""UserTenantIndex"" a
                USING ""UserTenantIndex"" b
                WHERE a.""Email"" = b.""Email"" AND a.""Id"" > b.""Id"";");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenantIndex_Email",
                table: "UserTenantIndex",
                column: "Email");
        }
    }
}
