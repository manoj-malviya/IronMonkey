using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Central
{
    /// <inheritdoc />
    public partial class SeedBlankRecipe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var blankRecipeId = new Guid("00000000-0000-0000-0000-000000000001");
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var contentJson = "{\"PipelineStages\":[{\"Name\":\"New\",\"Order\":0,\"StageType\":\"Entry\"}],\"CustomFields\":[],\"WorkflowRules\":[],\"Roles\":[]}";

            migrationBuilder.InsertData(
                table: "industry_recipes",
                columns: new[] { "Id", "Name", "Description", "IndustrySlug", "IconIdentifier", "IsBlank", "IsActive", "Version", "ContentJson", "CreatedAt", "UpdatedAt", "IsDeleted" },
                values: new object[] { blankRecipeId, "Blank/Custom", "Start with a clean workspace. One default pipeline stage included.", "blank", "icon-blank", true, true, 1, contentJson, now, now, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "industry_recipes",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"));
        }
    }
}
