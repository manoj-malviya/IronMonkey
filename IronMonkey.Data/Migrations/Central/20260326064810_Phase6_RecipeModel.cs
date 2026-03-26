using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Central
{
    /// <inheritdoc />
    public partial class Phase6_RecipeModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppliedRecipeId",
                table: "tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AppliedRecipeVersion",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "industry_recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IndustrySlug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IconIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsBlank = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_industry_recipes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_industry_recipes_IndustrySlug",
                table: "industry_recipes",
                column: "IndustrySlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_industry_recipes_IsActive",
                table: "industry_recipes",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "industry_recipes");

            migrationBuilder.DropColumn(
                name: "AppliedRecipeId",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "AppliedRecipeVersion",
                table: "tenants");
        }
    }
}
