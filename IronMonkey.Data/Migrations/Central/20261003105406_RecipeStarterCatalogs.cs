using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IronMonkey.Data.Migrations.Central
{
    /// <inheritdoc />
    public partial class RecipeStarterCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Starter catalogs for the stored Automobile and Education recipes, so a tenant
            // provisioned from them arrives with a plausible product shape and quote template.
            //
            // Applied only where the recipe has no Catalog yet: a platform admin may already
            // have edited the recipe, and this must never overwrite that. The JSON is a
            // literal, not serialised from RecipeCatalogDefinition — a migration must keep
            // producing the same rows however that class changes later.
            //
            // jsonb_exists() rather than the ? operator, which Npgsql can read as a parameter.
            migrationBuilder.Sql(@"
                UPDATE industry_recipes
                SET ""ContentJson"" = jsonb_set(""ContentJson""::jsonb, '{Catalog}', '{""Products"":[{""Code"":""VEH-NEW"",""Name"":""New vehicle"",""Description"":""New vehicle, on-the-road price"",""Category"":""Vehicles"",""ChargeType"":""OneOff"",""BillingFrequency"":""None"",""DefaultPeriods"":1,""UnitOfMeasure"":""vehicle"",""DefaultTaxRatePercent"":0,""ListPrice"":28000},{""Code"":""VEH-USED"",""Name"":""Approved used vehicle"",""Category"":""Vehicles"",""ChargeType"":""OneOff"",""BillingFrequency"":""None"",""DefaultPeriods"":1,""UnitOfMeasure"":""vehicle"",""DefaultTaxRatePercent"":0,""ListPrice"":16500},{""Code"":""FIN-PCP"",""Name"":""Finance plan (PCP)"",""Description"":""Monthly finance payment"",""Category"":""Finance"",""ChargeType"":""Recurring"",""BillingFrequency"":""Monthly"",""DefaultPeriods"":48,""UnitOfMeasure"":""month"",""DefaultTaxRatePercent"":0,""ListPrice"":349},{""Code"":""SVC-PLAN"",""Name"":""Service plan"",""Description"":""Scheduled servicing, paid monthly"",""Category"":""Aftersales"",""ChargeType"":""Recurring"",""BillingFrequency"":""Monthly"",""DefaultPeriods"":36,""UnitOfMeasure"":""month"",""DefaultTaxRatePercent"":0,""ListPrice"":25},{""Code"":""WAR-EXT"",""Name"":""Extended warranty"",""Description"":""Additional cover after the manufacturer warranty"",""Category"":""Add-ons"",""ChargeType"":""Recurring"",""BillingFrequency"":""Annually"",""DefaultPeriods"":2,""UnitOfMeasure"":""year"",""DefaultTaxRatePercent"":0,""ListPrice"":450},{""Code"":""ACC-PROT"",""Name"":""Paint and fabric protection"",""Category"":""Add-ons"",""ChargeType"":""OneOff"",""BillingFrequency"":""None"",""DefaultPeriods"":1,""UnitOfMeasure"":""unit"",""DefaultTaxRatePercent"":0,""ListPrice"":399}],""QuoteTemplate"":{""NumberPrefix"":""VQ-"",""ValidityDays"":14,""Terms"":""Prices include delivery and first registration. Finance is subject to status and credit approval. This quote is valid for 14 days."",""ApprovalDiscountThresholdPercent"":10}}'::jsonb)
                WHERE ""IndustrySlug"" = 'automobile' AND NOT jsonb_exists(""ContentJson""::jsonb, 'Catalog');");

            migrationBuilder.Sql(@"
                UPDATE industry_recipes
                SET ""ContentJson"" = jsonb_set(""ContentJson""::jsonb, '{Catalog}', '{""Products"":[{""Code"":""TUI-UG"",""Name"":""Undergraduate tuition"",""Description"":""Tuition fee per term"",""Category"":""Tuition"",""ChargeType"":""Recurring"",""BillingFrequency"":""PerTerm"",""DefaultPeriods"":6,""UnitOfMeasure"":""term"",""DefaultTaxRatePercent"":0,""ListPrice"":4625},{""Code"":""TUI-PG"",""Name"":""Postgraduate tuition"",""Description"":""Tuition fee per term"",""Category"":""Tuition"",""ChargeType"":""Recurring"",""BillingFrequency"":""PerTerm"",""DefaultPeriods"":3,""UnitOfMeasure"":""term"",""DefaultTaxRatePercent"":0,""ListPrice"":5800},{""Code"":""ACC-HALL"",""Name"":""Halls accommodation"",""Description"":""Accommodation per term"",""Category"":""Accommodation"",""ChargeType"":""Recurring"",""BillingFrequency"":""PerTerm"",""DefaultPeriods"":3,""UnitOfMeasure"":""term"",""DefaultTaxRatePercent"":0,""ListPrice"":2100},{""Code"":""FEE-APP"",""Name"":""Application fee"",""Category"":""Fees"",""ChargeType"":""OneOff"",""BillingFrequency"":""None"",""DefaultPeriods"":1,""UnitOfMeasure"":""application"",""DefaultTaxRatePercent"":0,""ListPrice"":75},{""Code"":""FEE-DEP"",""Name"":""Enrolment deposit"",""Description"":""Deducted from the first term''s tuition"",""Category"":""Fees"",""ChargeType"":""OneOff"",""BillingFrequency"":""None"",""DefaultPeriods"":1,""UnitOfMeasure"":""deposit"",""DefaultTaxRatePercent"":0,""ListPrice"":500}],""QuoteTemplate"":{""NumberPrefix"":""OFF-"",""ValidityDays"":30,""Terms"":""This offer of fees is valid for 30 days. Tuition is invoiced at the start of each term."",""ApprovalDiscountThresholdPercent"":15}}'::jsonb)
                WHERE ""IndustrySlug"" = 'education' AND NOT jsonb_exists(""ContentJson""::jsonb, 'Catalog');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE industry_recipes
                SET ""ContentJson"" = (""ContentJson""::jsonb - 'Catalog')
                WHERE ""IndustrySlug"" IN ('automobile', 'education');");
        }
    }
}
