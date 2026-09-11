using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class CustomFieldScopeAndContactValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppliesTo",
                table: "custom_field_definitions",
                type: "text",
                nullable: false,
                defaultValue: "Lead");

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "custom_field_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // EF defaults a non-nullable string column to "", which is not valid jsonb and
            // fails on the first insert. The serialized empty CustomFieldValues shape is what
            // the converter round-trips, so match it exactly.
            migrationBuilder.AddColumn<string>(
                name: "custom_field_values",
                table: "contacts",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{\"Values\":{}}'::jsonb");

            migrationBuilder.CreateIndex(
                name: "IX_custom_field_definitions_TenantId_AppliesTo_DisplayOrder",
                table: "custom_field_definitions",
                columns: new[] { "TenantId", "AppliesTo", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_custom_field_definitions_TenantId_AppliesTo_DisplayOrder",
                table: "custom_field_definitions");

            migrationBuilder.DropColumn(
                name: "AppliesTo",
                table: "custom_field_definitions");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "custom_field_definitions");

            migrationBuilder.DropColumn(
                name: "custom_field_values",
                table: "contacts");
        }
    }
}
