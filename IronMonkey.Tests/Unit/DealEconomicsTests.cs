using IronMonkey.ApiService.Features.Quotes;
using IronMonkey.Data.Commerce;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Presentation;
using IronMonkey.Data.RecipeContent;
using IronMonkey.ApiService.Features.Recipes;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// The money rules every surface shares: one rounding rule, one aggregation rule, one
/// formatter. These pin the rules themselves; the integration tests pin that every surface
/// actually goes through them.
/// </summary>
public class DealEconomicsTests
{
    // ── Rounding ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2.345, "GBP", 2.35)]   // midpoint rounds away from zero, not to even
    [InlineData(2.355, "GBP", 2.36)]
    [InlineData(-2.345, "GBP", -2.35)]
    [InlineData(1234.5, "JPY", 1235)]  // zero-decimal currency
    [InlineData(1.2345, "KWD", 1.235)] // three-decimal currency
    [InlineData(2.345, null, 2.35)]    // unconfigured currency defaults to 2 places
    public void Rounding_is_half_away_from_zero_to_the_currency_minor_unit(double input, string? currency, double expected)
    {
        Assert.Equal((decimal)expected, MoneyMath.Round((decimal)input, currency));
    }

    [Fact]
    public void Line_rule_rounds_gross_then_discount_then_tax_on_the_discounted_amount()
    {
        // 3 × 33.335 = 100.005 → gross 100.01; 12.5% discount = 12.50125 → 12.50;
        // net 87.51; 20% tax = 17.502 → 17.50; total 105.01.
        var amounts = LineCalculator.Compute(new LineInput(3m, 33.335m, 1, 12.5m, 20m, ChargeType.OneOff), "GBP");

        Assert.Equal(100.01m, amounts.Gross);
        Assert.Equal(12.50m, amounts.Discount);
        Assert.Equal(87.51m, amounts.Net);
        Assert.Equal(17.50m, amounts.Tax);
        Assert.Equal(105.01m, amounts.Total);
    }

    [Fact]
    public void Recurring_lines_multiply_by_periods_and_one_off_lines_ignore_them()
    {
        var recurring = LineCalculator.Compute(new LineInput(1m, 349m, 48, 0m, 0m, ChargeType.Recurring), "GBP");
        var oneOff = LineCalculator.Compute(new LineInput(1m, 349m, 48, 0m, 0m, ChargeType.OneOff), "GBP");

        Assert.Equal(16752m, recurring.Total);
        Assert.Equal(349m, oneOff.Total);
    }

    [Fact]
    public void Deal_total_is_the_sum_of_rounded_lines_and_is_never_rounded_again()
    {
        // Three lines of 0.333… each round to 0.33. Rounding the grand total from unrounded
        // parts would give 1.00; the customer adding the printed lines gets 0.99. The rule
        // is that the total equals what the customer can add up.
        var line = LineCalculator.Compute(new LineInput(1m, 1m / 3m, 1, 0m, 0m, ChargeType.OneOff), "GBP");
        var totals = LineCalculator.Sum(Enumerable.Repeat((line, ChargeType.OneOff), 3));

        Assert.Equal(0.33m, line.Total);
        Assert.Equal(0.99m, totals.Total);
    }

    [Fact]
    public void Totals_separate_one_off_from_recurring()
    {
        var vehicle = LineCalculator.Compute(new LineInput(1m, 28000m, 1, 0m, 0m, ChargeType.OneOff), null);
        var finance = LineCalculator.Compute(new LineInput(1m, 349m, 48, 0m, 0m, ChargeType.Recurring), null);

        var totals = LineCalculator.Sum([(vehicle, ChargeType.OneOff), (finance, ChargeType.Recurring)]);

        Assert.Equal(28000m, totals.OneOffTotal);
        Assert.Equal(16752m, totals.RecurringTotal);
        Assert.Equal(44752m, totals.Total);
    }

    [Theory]
    [InlineData(0, 10, 1, 0, 0)]
    [InlineData(1, -1, 1, 0, 0)]
    [InlineData(1, 10, 0, 0, 0)]
    [InlineData(1, 10, 1, 101, 0)]
    [InlineData(1, 10, 1, 0, -5)]
    public void Invalid_line_inputs_are_refused(double qty, double price, int periods, double discount, double tax)
    {
        Assert.NotNull(LineCalculator.Validate((decimal)qty, (decimal)price, periods, (decimal)discount, (decimal)tax));
    }

    // ── Multi-currency aggregation ───────────────────────────────────────────────────

    [Fact]
    public void Aggregation_never_adds_a_foreign_currency_at_face_value()
    {
        var total = MoneyAggregator.Sum(
        [
            new MoneyValue(1000m, null, null),      // tenant currency
            new MoneyValue(500m, "GBP", null),      // explicitly the base currency
            new MoneyValue(2000m, "EUR", null)      // foreign, no rate
        ], "GBP");

        Assert.Equal(1500m, total.Total);
        Assert.False(total.IsComplete);
        Assert.Equal(1, total.ExcludedCount);
        Assert.Equal(2000m, total.ExcludedAmounts["EUR"]);
    }

    [Fact]
    public void Aggregation_converts_through_the_rate_recorded_on_the_record()
    {
        var total = MoneyAggregator.Sum(
        [
            new MoneyValue(1000m, null, null),
            new MoneyValue(100.005m, "EUR", 0.85m)  // 85.00425 → 85.00
        ], "GBP");

        Assert.Equal(1085.00m, total.Total);
        Assert.True(total.IsComplete);
        Assert.Equal(1, total.ConvertedCount);
    }

    [Fact]
    public void A_tenant_with_no_base_currency_still_refuses_an_explicit_foreign_currency()
    {
        var total = MoneyAggregator.Sum([new MoneyValue(10m, null, null), new MoneyValue(10m, "USD", null)], null);

        Assert.Equal(10m, total.Total);
        Assert.Equal(1, total.ExcludedCount);
    }

    // ── Formatting ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Exact_money_uses_the_tenant_symbol_only_for_the_tenant_currency()
    {
        var formatting = TenantFormatting.From(new TenantLocale { CurrencyCode = "GBP", CurrencySymbol = "£", Culture = "en-GB" });

        Assert.Equal("£1,234.50", formatting.Money(1234.5m, null));
        Assert.Equal("£1,234.50", formatting.Money(1234.5m, "GBP"));
        Assert.Equal("EUR 1,234.50", formatting.Money(1234.5m, "EUR"));
        Assert.Equal("JPY 1,235", formatting.Money(1234.5m, "JPY"));
    }

    [Fact]
    public void Exact_money_never_renders_the_invariant_currency_placeholder()
    {
        var formatting = TenantFormatting.From(null);
        var text = formatting.Money(308000m, null);

        Assert.DoesNotContain("¤", text);
        Assert.Equal("308,000.00", text);
    }

    // ── Quote document ───────────────────────────────────────────────────────────────

    [Fact]
    public void Quote_document_shows_exactly_the_totals_the_API_computed_and_encodes_every_value()
    {
        var tenantId = Guid.NewGuid();
        var opportunity = Opportunity.Create(tenantId, "Fleet", Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid());
        opportunity.AddLine(new LineDetails(null, null, null, "<script>alert(1)</script> Widget", "W-1", null,
            3m, 33.335m, 10m, ChargeType.OneOff, BillingFrequency.None, 1, 12.5m, 20m));

        var quote = Quote.CreateDraft(opportunity, 7, "Q-", "Fleet", "Mallory <b>", "m@x.test",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), "Net 30", null, Guid.NewGuid(), DateTime.UtcNow);

        var formatting = TenantFormatting.From(new TenantLocale { CurrencyCode = "GBP", CurrencySymbol = "£", Culture = "en-GB" });
        var html = QuoteDocumentRenderer.Render(quote, new TenantBranding { PrimaryColor = "red;}</style><script>" }, "Acme", formatting);

        // The document total is the formatted API total — the same string the UI renders
        // (HTML-encoded, so "£" arrives as an entity the browser renders identically).
        Assert.Equal(105.01m, quote.Total);
        Assert.Equal(opportunity.Amount, quote.Total);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(formatting.Money(quote.Total, quote.CurrencyCode)), html);

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("Mallory <b>", html);
        Assert.Contains("Mallory &lt;b&gt;", html);
        // Unit cost is never on a customer document.
        Assert.DoesNotContain("10.00", html);
    }

    // ── Revenue period ───────────────────────────────────────────────────────────────

    [Fact]
    public void Revenue_by_close_date_covers_the_whole_period_including_future_close_dates()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        var quarter = IronMonkey.ApiService.Features.Reports.Revenue.GetRevenueReportEndpoint.ForCloseDates(
            IronMonkey.ApiService.Features.Reports.Dashboard.DashboardDateRange.Resolve("thisquarter", null, null, now));

        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), quarter.From);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), quarter.ToExclusive);

        var allTime = IronMonkey.ApiService.Features.Reports.Revenue.GetRevenueReportEndpoint.ForCloseDates(
            IronMonkey.ApiService.Features.Reports.Dashboard.DashboardDateRange.Resolve("alltime", null, null, now));
        Assert.True(allTime.ToExclusive > now.AddYears(100));
    }

    // ── Recipes ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_recipe_stored_before_catalogs_existed_still_deserializes_and_validates()
    {
        const string legacy = "{\"PipelineStages\":[{\"Name\":\"New\",\"Order\":0,\"StageType\":\"Entry\"}]," +
                              "\"CustomFields\":[{\"FieldName\":\"Model\",\"FieldType\":\"Text\",\"IsRequired\":false,\"Options\":[]}]}";

        var model = System.Text.Json.JsonSerializer.Deserialize<RecipeContentModel>(legacy)!;

        Assert.Null(model.Catalog);
        Assert.Null(model.CustomFields[0].AppliesTo);
        Assert.True(new RecipeContentValidator().Validate(model).IsValid);
    }

    [Fact]
    public void A_recipe_catalog_with_duplicate_codes_is_refused_at_authoring_time()
    {
        var model = new RecipeContentModel
        {
            PipelineStages = [new() { Name = "New", StageType = "Entry" }],
            Catalog = new RecipeCatalogDefinition
            {
                Products = [new() { Code = "A-1", Name = "One" }, new() { Code = "a-1", Name = "Two" }]
            }
        };

        Assert.False(new RecipeContentValidator().Validate(model).IsValid);
    }
}
