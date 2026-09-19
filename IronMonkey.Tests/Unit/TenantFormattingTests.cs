using IronMonkey.Data.Presentation;
using Xunit;

namespace IronMonkey.Tests.Unit;

/// <summary>
/// Currency and timezone rendering. The specific bug being prevented is "C0" under the
/// invariant culture, which renders the placeholder ¤ rather than a currency anyone can read.
/// </summary>
public class TenantFormattingTests
{
    [Fact]
    public void UnconfiguredTenant_RendersReadableNumber_NotCurrencyPlaceholder()
    {
        var formatting = TenantFormatting.Default;

        var rendered = formatting.Amount(308000m);

        Assert.Equal("308,000", rendered);
        Assert.DoesNotContain("¤", rendered);
    }

    [Fact]
    public void ConfiguredCurrency_RendersWithSymbol()
    {
        var formatting = TenantFormatting.From(new TenantLocale
        {
            CurrencyCode = "GBP",
            CurrencySymbol = "£"
        });

        Assert.Equal("£308,000", formatting.Amount(308000m));
        Assert.DoesNotContain("¤", formatting.Amount(308000m));
    }

    [Fact]
    public void TwoTenants_RenderTheirOwnCurrencies()
    {
        var uk = TenantFormatting.From(new TenantLocale { CurrencySymbol = "£" });
        var india = TenantFormatting.From(new TenantLocale { CurrencySymbol = "₹" });

        Assert.Equal("£1,000", uk.Amount(1000m));
        Assert.Equal("₹1,000", india.Amount(1000m));
    }

    [Fact]
    public void Compact_KeepsTheSymbol()
    {
        var formatting = TenantFormatting.From(new TenantLocale { CurrencySymbol = "£" });

        Assert.Equal("£308K", formatting.Compact(308_000m));
        Assert.Equal("£1.2M", formatting.Compact(1_200_000m));
        // Below 10,000 the full number is kept — abbreviating loses precision that matters.
        Assert.Equal("£9,999", formatting.Compact(9_999m));
    }

    [Fact]
    public void NullAmount_RendersDash()
    {
        Assert.Equal("—", TenantFormatting.Default.Nullable(null));
    }

    [Fact]
    public void UnknownCulture_FallsBackRatherThanThrowing()
    {
        // A stored value can go stale or be hand-edited; a bad one must not take pages down.
        var formatting = TenantFormatting.From(new TenantLocale { Culture = "not-a-culture" });

        Assert.Equal("1,000", formatting.Amount(1000m));
    }

    [Fact]
    public void UnknownTimeZone_FallsBackToUtcAndSaysSo()
    {
        var formatting = TenantFormatting.From(new TenantLocale { TimeZoneId = "Mars/Olympus" });

        Assert.Equal("UTC", formatting.TimeZoneLabel);
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, formatting.ToTenantTime(utc));
    }

    [Fact]
    public void ConfiguredTimeZone_ShiftsDisplayedTime()
    {
        var formatting = TenantFormatting.From(new TenantLocale { TimeZoneId = "Asia/Kolkata" });

        // 12:00 UTC is 17:30 in Kolkata (UTC+5:30, no DST).
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var local = formatting.ToTenantTime(utc);

        Assert.Equal(17, local.Hour);
        Assert.Equal(30, local.Minute);
        Assert.Equal("Asia/Kolkata", formatting.TimeZoneLabel);
    }

    [Fact]
    public void TenantTimeZone_DecidesWhatTodayMeans()
    {
        // 22:00 UTC on the 1st is already the 2nd in Kolkata. A date-range preset that used
        // server-local "today" would show the tenant the wrong day's work.
        var formatting = TenantFormatting.From(new TenantLocale { TimeZoneId = "Asia/Kolkata" });

        var lateUtc = new DateTime(2026, 6, 1, 22, 0, 0, DateTimeKind.Utc);
        var local = formatting.ToTenantTime(lateUtc);

        Assert.Equal(2, local.Day);
    }

    [Fact]
    public void UnspecifiedKind_IsTreatedAsUtc_NotServerLocal()
    {
        // Every timestamp in this system is written with DateTime.UtcNow; a value that lost
        // its Kind on the way through must not be reinterpreted as server-local time.
        var formatting = TenantFormatting.From(new TenantLocale { TimeZoneId = "Asia/Kolkata" });

        var unspecified = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var local = formatting.ToTenantTime(unspecified);

        Assert.Equal(17, local.Hour);
        Assert.Equal(30, local.Minute);
    }

    [Fact]
    public void DateTimeWithZone_NamesTheZone()
    {
        var formatting = TenantFormatting.From(new TenantLocale { TimeZoneId = "Asia/Kolkata" });
        var rendered = formatting.DateTimeWithZone(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.Contains("Asia/Kolkata", rendered);
    }
}
