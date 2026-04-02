using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class Phase8_SignupRequestRecipeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndustryType",
                table: "SignupRequests");

            migrationBuilder.AddColumn<Guid>(
                name: "RecipeId",
                table: "SignupRequests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipeId",
                table: "SignupRequests");

            migrationBuilder.AddColumn<string>(
                name: "IndustryType",
                table: "SignupRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
