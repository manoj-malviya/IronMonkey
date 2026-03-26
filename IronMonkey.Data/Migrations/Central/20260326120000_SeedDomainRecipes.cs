using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Central
{
    /// <inheritdoc />
    public partial class SeedDomainRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Automobile Dealership Recipe — GUID ...002
            var automobileRecipeId = new Guid("00000000-0000-0000-0000-000000000002");
            var automobileContentJson =
                "{\"PipelineStages\":[" +
                    "{\"Name\":\"Inquiry\",\"Order\":0,\"StageType\":\"Entry\"}," +
                    "{\"Name\":\"Test Drive\",\"Order\":1,\"StageType\":\"Active\"}," +
                    "{\"Name\":\"Negotiation\",\"Order\":2,\"StageType\":\"Active\"}," +
                    "{\"Name\":\"F&I\",\"Order\":3,\"StageType\":\"Active\"}," +
                    "{\"Name\":\"Sold\",\"Order\":4,\"StageType\":\"ClosedWon\"}," +
                    "{\"Name\":\"Lost\",\"Order\":5,\"StageType\":\"ClosedLost\"}" +
                "]," +
                "\"CustomFields\":[" +
                    "{\"FieldName\":\"Vehicle Make\",\"FieldType\":\"Dropdown\",\"IsRequired\":false,\"Options\":[\"Toyota\",\"Honda\",\"Ford\",\"Chevrolet\",\"BMW\",\"Mercedes\",\"Other\"]}," +
                    "{\"FieldName\":\"Vehicle Model\",\"FieldType\":\"Text\",\"IsRequired\":false,\"Options\":[]}," +
                    "{\"FieldName\":\"Vehicle Year\",\"FieldType\":\"Number\",\"IsRequired\":false,\"Options\":[]}," +
                    "{\"FieldName\":\"Budget Range\",\"FieldType\":\"Dropdown\",\"IsRequired\":false,\"Options\":[\"Under 20K\",\"20-35K\",\"35-50K\",\"50-75K\",\"75K+\"]}," +
                    "{\"FieldName\":\"Has Trade-In\",\"FieldType\":\"Boolean\",\"IsRequired\":false,\"Options\":[]}," +
                    "{\"FieldName\":\"Preferred Contact\",\"FieldType\":\"Dropdown\",\"IsRequired\":false,\"Options\":[\"Phone\",\"Email\",\"Text\",\"WhatsApp\"]}" +
                "]," +
                "\"WorkflowRules\":[" +
                    "{\"Name\":\"Notify on Negotiation\",\"Trigger\":\"StatusChange\",\"ConditionJson\":\"{\\\"stage\\\":\\\"Negotiation\\\"}\",\"ActionJson\":\"{\\\"notify\\\":\\\"Sales Manager\\\"}\"}," +
                    "{\"Name\":\"Flag Stale Inquiry\",\"Trigger\":\"TimeElapsed\",\"ConditionJson\":\"{\\\"stage\\\":\\\"Inquiry\\\",\\\"hours\\\":48}\",\"ActionJson\":\"{\\\"flag\\\":\\\"at-risk\\\"}\"}" +
                "]," +
                "\"Roles\":[" +
                    "{\"Name\":\"Sales Manager\",\"Description\":\"Oversees deals, approvals, and team performance\"}," +
                    "{\"Name\":\"Sales Executive\",\"Description\":\"Handles walk-ins, test drives, and closing deals\"}," +
                    "{\"Name\":\"BDC Agent\",\"Description\":\"Manages inbound inquiries and schedules appointments\"}" +
                "]," +
                "\"SampleLeads\":[" +
                    "{\"FirstName\":\"Marcus\",\"LastName\":\"Johnson\",\"Email\":\"marcus.johnson@example.com\",\"Mobile\":\"+1-555-0101\",\"Source\":\"WebForm\",\"StageName\":\"Inquiry\",\"CustomFieldValues\":{\"Vehicle Make\":\"Honda\",\"Vehicle Model\":\"Civic\",\"Budget Range\":\"20-35K\",\"Has Trade-In\":false}}," +
                    "{\"FirstName\":\"Priya\",\"LastName\":\"Sharma\",\"Email\":\"priya.sharma@example.com\",\"Mobile\":\"+1-555-0102\",\"Source\":\"WebForm\",\"StageName\":\"Inquiry\",\"CustomFieldValues\":{\"Vehicle Make\":\"Toyota\",\"Vehicle Model\":\"Camry\",\"Budget Range\":\"35-50K\",\"Has Trade-In\":true}}," +
                    "{\"FirstName\":\"David\",\"LastName\":\"Okafor\",\"Email\":\"david.okafor@example.com\",\"Mobile\":\"+1-555-0103\",\"Source\":\"WebForm\",\"StageName\":\"Test Drive\",\"CustomFieldValues\":{\"Vehicle Make\":\"Ford\",\"Vehicle Model\":\"F-150\",\"Budget Range\":\"50-75K\",\"Has Trade-In\":false}}," +
                    "{\"FirstName\":\"Sofia\",\"LastName\":\"Ramirez\",\"Email\":\"sofia.ramirez@example.com\",\"Mobile\":\"+1-555-0104\",\"Source\":\"WebForm\",\"StageName\":\"Negotiation\",\"CustomFieldValues\":{\"Vehicle Make\":\"BMW\",\"Vehicle Model\":\"3 Series\",\"Budget Range\":\"50-75K\",\"Has Trade-In\":false}}," +
                    "{\"FirstName\":\"James\",\"LastName\":\"Chen\",\"Email\":\"james.chen@example.com\",\"Mobile\":\"+1-555-0105\",\"Source\":\"WebForm\",\"StageName\":\"F&I\",\"CustomFieldValues\":{\"Vehicle Make\":\"Mercedes\",\"Vehicle Model\":\"C-Class\",\"Budget Range\":\"75K+\",\"Has Trade-In\":true}}" +
                "]}";

            migrationBuilder.InsertData(
                table: "industry_recipes",
                columns: new[] { "Id", "Name", "Description", "IndustrySlug", "IconIdentifier", "IsBlank", "IsActive", "Version", "ContentJson", "CreatedAt", "UpdatedAt", "IsDeleted" },
                values: new object[] { automobileRecipeId, "Automobile Dealership", "Pre-configured for automobile dealerships with Road-to-the-Sale pipeline stages, vehicle custom fields, and follow-up workflow rules.", "automobile", "icon-automobile", false, true, 1, automobileContentJson, now, now, false });

            // Education Institution Recipe — GUID ...003
            var educationRecipeId = new Guid("00000000-0000-0000-0000-000000000003");
            var educationContentJson =
                "{\"PipelineStages\":[" +
                    "{\"Name\":\"Inquiry\",\"Order\":0,\"StageType\":\"Entry\"}," +
                    "{\"Name\":\"Application\",\"Order\":1,\"StageType\":\"Active\"}," +
                    "{\"Name\":\"Under Review\",\"Order\":2,\"StageType\":\"Active\"}," +
                    "{\"Name\":\"Interview\",\"Order\":3,\"StageType\":\"Active\"}," +
                    "{\"Name\":\"Enrolled\",\"Order\":4,\"StageType\":\"ClosedWon\"}," +
                    "{\"Name\":\"Declined\",\"Order\":5,\"StageType\":\"ClosedLost\"}" +
                "]," +
                "\"CustomFields\":[" +
                    "{\"FieldName\":\"Program of Interest\",\"FieldType\":\"Dropdown\",\"IsRequired\":false,\"Options\":[\"Engineering\",\"Business\",\"Arts\",\"Science\",\"Medicine\",\"Law\",\"Education\",\"Other\"]}," +
                    "{\"FieldName\":\"Grade/Year Level\",\"FieldType\":\"Dropdown\",\"IsRequired\":false,\"Options\":[\"K-5\",\"6-8\",\"9-12\",\"Undergraduate\",\"Graduate\"]}," +
                    "{\"FieldName\":\"Previous School\",\"FieldType\":\"Text\",\"IsRequired\":false,\"Options\":[]}," +
                    "{\"FieldName\":\"Guardian Name\",\"FieldType\":\"Text\",\"IsRequired\":false,\"Options\":[]}," +
                    "{\"FieldName\":\"Guardian Phone\",\"FieldType\":\"Text\",\"IsRequired\":false,\"Options\":[]}," +
                    "{\"FieldName\":\"Scholarship Needed\",\"FieldType\":\"Boolean\",\"IsRequired\":false,\"Options\":[]}" +
                "]," +
                "\"WorkflowRules\":[" +
                    "{\"Name\":\"Notify on Review\",\"Trigger\":\"StatusChange\",\"ConditionJson\":\"{\\\"stage\\\":\\\"Under Review\\\"}\",\"ActionJson\":\"{\\\"notify\\\":\\\"Admissions Officer\\\"}\"}," +
                    "{\"Name\":\"Flag Stale Inquiry\",\"Trigger\":\"TimeElapsed\",\"ConditionJson\":\"{\\\"stage\\\":\\\"Inquiry\\\",\\\"hours\\\":168}\",\"ActionJson\":\"{\\\"flag\\\":\\\"at-risk\\\"}\"}" +
                "]," +
                "\"Roles\":[" +
                    "{\"Name\":\"Admissions Director\",\"Description\":\"Oversees admissions process and team performance\"}," +
                    "{\"Name\":\"Admissions Officer\",\"Description\":\"Reviews applications and conducts interviews\"}," +
                    "{\"Name\":\"Academic Counselor\",\"Description\":\"Guides prospective students through program selection\"}" +
                "]," +
                "\"SampleLeads\":[" +
                    "{\"FirstName\":\"Aisha\",\"LastName\":\"Williams\",\"Email\":\"aisha.williams@example.com\",\"Mobile\":\"+1-555-0201\",\"Source\":\"WebForm\",\"StageName\":\"Inquiry\",\"CustomFieldValues\":{\"Program of Interest\":\"Engineering\",\"Grade/Year Level\":\"Undergraduate\",\"Scholarship Needed\":true}}," +
                    "{\"FirstName\":\"Carlos\",\"LastName\":\"Mendez\",\"Email\":\"carlos.mendez@example.com\",\"Mobile\":\"+1-555-0202\",\"Source\":\"WebForm\",\"StageName\":\"Inquiry\",\"CustomFieldValues\":{\"Program of Interest\":\"Business\",\"Grade/Year Level\":\"Graduate\",\"Scholarship Needed\":false}}," +
                    "{\"FirstName\":\"Yuki\",\"LastName\":\"Tanaka\",\"Email\":\"yuki.tanaka@example.com\",\"Mobile\":\"+1-555-0203\",\"Source\":\"WebForm\",\"StageName\":\"Application\",\"CustomFieldValues\":{\"Program of Interest\":\"Medicine\",\"Grade/Year Level\":\"Graduate\",\"Previous School\":\"State University\",\"Scholarship Needed\":true}}," +
                    "{\"FirstName\":\"Omar\",\"LastName\":\"Hassan\",\"Email\":\"omar.hassan@example.com\",\"Mobile\":\"+1-555-0204\",\"Source\":\"WebForm\",\"StageName\":\"Under Review\",\"CustomFieldValues\":{\"Program of Interest\":\"Science\",\"Grade/Year Level\":\"Undergraduate\",\"Previous School\":\"City College\",\"Scholarship Needed\":false}}," +
                    "{\"FirstName\":\"Elena\",\"LastName\":\"Petrov\",\"Email\":\"elena.petrov@example.com\",\"Mobile\":\"+1-555-0205\",\"Source\":\"WebForm\",\"StageName\":\"Interview\",\"CustomFieldValues\":{\"Program of Interest\":\"Arts\",\"Grade/Year Level\":\"Undergraduate\",\"Previous School\":\"Arts Academy\",\"Scholarship Needed\":true}}" +
                "]}";

            migrationBuilder.InsertData(
                table: "industry_recipes",
                columns: new[] { "Id", "Name", "Description", "IndustrySlug", "IconIdentifier", "IsBlank", "IsActive", "Version", "ContentJson", "CreatedAt", "UpdatedAt", "IsDeleted" },
                values: new object[] { educationRecipeId, "Educational Institution", "Pre-configured for educational institutions with Admissions Funnel pipeline stages, student custom fields, and notification workflow rules.", "education", "icon-education", false, true, 1, educationContentJson, now, now, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "industry_recipes", keyColumn: "Id", keyValue: new Guid("00000000-0000-0000-0000-000000000002"));
            migrationBuilder.DeleteData(table: "industry_recipes", keyColumn: "Id", keyValue: new Guid("00000000-0000-0000-0000-000000000003"));
        }
    }
}
