using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

// Money columns are numeric(18,4): wide enough for any deal, and four places so a unit price
// can carry sub-minor precision (a per-unit price of 0.0125) while every computed figure is
// stored already rounded to the currency's minor unit by LineCalculator.

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Category).HasMaxLength(100);
        builder.Property(p => p.UnitOfMeasure).HasMaxLength(50);
        builder.Property(p => p.ChargeType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.BillingFrequency).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.DefaultTaxRatePercent).HasPrecision(9, 4);

        builder.Property(p => p.CustomFields)
            .HasColumnName("custom_field_values")
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<CustomFieldValues>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());

        // Code uniqueness is case-insensitive and enforced by a raw-SQL lower(Code) index in
        // the migration, like stage names: EF cannot express an expression index.
        builder.HasIndex(p => new { p.TenantId, p.Category });
    }
}

internal sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.ToTable("price_lists");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.CurrencyCode).HasMaxLength(3);

        // Exactly one default per tenant. Without it the fallback step of price resolution
        // could find two lists and pick one arbitrarily.
        builder.HasIndex(p => p.TenantId)
            .IsUnique()
            .HasFilter("\"IsDefault\" = true AND \"IsDeleted\" = false")
            .HasDatabaseName("IX_price_lists_TenantId_Default");
    }
}

internal sealed class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> builder)
    {
        builder.ToTable("product_prices");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.UnitPrice).HasPrecision(18, 4);
        builder.Property(p => p.UnitCost).HasPrecision(18, 4);

        builder.HasOne(p => p.Product).WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.PriceList).WithMany().HasForeignKey(p => p.PriceListId).OnDelete(DeleteBehavior.Restrict);

        // Resolution is "the version of (product, list) effective at t".
        builder.HasIndex(p => new { p.TenantId, p.ProductId, p.PriceListId, p.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName("IX_product_prices_Product_List_EffectiveFrom");
    }
}

internal sealed class OpportunityLineItemConfiguration : IEntityTypeConfiguration<OpportunityLineItem>
{
    public void Configure(EntityTypeBuilder<OpportunityLineItem> builder)
    {
        builder.ToTable("opportunity_line_items");
        builder.HasKey(l => l.Id);

        // Client-generated key, declared as such. By convention a Guid key is "generated on
        // add", and EF then treats a child with its Id already set — discovered through the
        // parent's collection rather than added via its DbSet — as an existing row to UPDATE,
        // which affects nothing and fails. ValueGeneratedNever makes it an INSERT.
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Description).IsRequired().HasMaxLength(500);
        builder.Property(l => l.ProductCode).HasMaxLength(64);
        builder.Property(l => l.Category).HasMaxLength(100);
        builder.Property(l => l.ChargeType).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.BillingFrequency).HasConversion<string>().HasMaxLength(20);

        builder.Property(l => l.Quantity).HasPrecision(18, 4);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 4);
        builder.Property(l => l.UnitCost).HasPrecision(18, 4);
        builder.Property(l => l.DiscountPercent).HasPrecision(9, 4);
        builder.Property(l => l.TaxRatePercent).HasPrecision(9, 4);
        builder.Property(l => l.GrossAmount).HasPrecision(18, 4);
        builder.Property(l => l.DiscountAmount).HasPrecision(18, 4);
        builder.Property(l => l.NetAmount).HasPrecision(18, 4);
        builder.Property(l => l.TaxAmount).HasPrecision(18, 4);
        builder.Property(l => l.TotalAmount).HasPrecision(18, 4);

        builder.HasOne<Opportunity>()
            .WithMany(o => o.LineItems)
            .HasForeignKey(l => l.OpportunityId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: a product that has been sold cannot be hard-deleted out from under the
        // deals that sold it. Products are deactivated instead.
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.TenantId, l.OpportunityId, l.Position });
        builder.HasIndex(l => new { l.TenantId, l.ProductId });
    }
}

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("quotes");
        builder.HasKey(q => q.Id);

        builder.Property(q => q.NumberPrefix).IsRequired().HasMaxLength(20);
        builder.Property(q => q.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(q => q.Title).IsRequired().HasMaxLength(200);
        builder.Property(q => q.CurrencyCode).HasMaxLength(3);
        builder.Property(q => q.Terms).HasMaxLength(8000);
        builder.Property(q => q.RecipientName).IsRequired().HasMaxLength(200);
        builder.Property(q => q.RecipientEmail).HasMaxLength(320);
        builder.Property(q => q.RespondedByName).HasMaxLength(200);
        builder.Property(q => q.ResponseNote).HasMaxLength(2000);

        foreach (var money in new[] { nameof(Quote.Subtotal), nameof(Quote.DiscountTotal), nameof(Quote.TaxTotal),
                     nameof(Quote.Total), nameof(Quote.OneOffTotal), nameof(Quote.RecurringTotal) })
            builder.Property<decimal>(money).HasPrecision(18, 4);
        builder.Property(q => q.MaxDiscountPercent).HasPrecision(9, 4);

        builder.HasOne<Opportunity>().WithMany().HasForeignKey(q => q.OpportunityId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(q => q.Lines).WithOne().HasForeignKey(l => l.QuoteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(q => q.StatusChanges).WithOne().HasForeignKey(c => c.QuoteId).OnDelete(DeleteBehavior.Cascade);

        // Two concurrent revisions must not both become the same version.
        builder.HasIndex(q => new { q.TenantId, q.Number, q.Version })
            .IsUnique()
            .HasDatabaseName("IX_quotes_TenantId_Number_Version");

        builder.HasIndex(q => new { q.TenantId, q.OpportunityId });
    }
}

internal sealed class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> builder)
    {
        builder.ToTable("quote_lines");
        builder.HasKey(l => l.Id);

        // Client-generated key, declared as such. By convention a Guid key is "generated on
        // add", and EF then treats a child with its Id already set — discovered through the
        // parent's collection rather than added via its DbSet — as an existing row to UPDATE,
        // which affects nothing and fails. ValueGeneratedNever makes it an INSERT.
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Description).IsRequired().HasMaxLength(500);
        builder.Property(l => l.ProductCode).HasMaxLength(64);
        builder.Property(l => l.Category).HasMaxLength(100);
        builder.Property(l => l.ChargeType).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.BillingFrequency).HasConversion<string>().HasMaxLength(20);

        builder.Property(l => l.Quantity).HasPrecision(18, 4);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 4);
        builder.Property(l => l.DiscountPercent).HasPrecision(9, 4);
        builder.Property(l => l.TaxRatePercent).HasPrecision(9, 4);
        builder.Property(l => l.GrossAmount).HasPrecision(18, 4);
        builder.Property(l => l.DiscountAmount).HasPrecision(18, 4);
        builder.Property(l => l.NetAmount).HasPrecision(18, 4);
        builder.Property(l => l.TaxAmount).HasPrecision(18, 4);
        builder.Property(l => l.TotalAmount).HasPrecision(18, 4);

        builder.HasIndex(l => new { l.TenantId, l.QuoteId, l.Position });
    }
}

internal sealed class QuoteStatusChangeConfiguration : IEntityTypeConfiguration<QuoteStatusChange>
{
    public void Configure(EntityTypeBuilder<QuoteStatusChange> builder)
    {
        builder.ToTable("quote_status_changes");
        builder.HasKey(c => c.Id);

        // Client-generated key, declared as such. By convention a Guid key is "generated on
        // add", and EF then treats a child with its Id already set — discovered through the
        // parent's collection rather than added via its DbSet — as an existing row to UPDATE,
        // which affects nothing and fails. ValueGeneratedNever makes it an INSERT.
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ChangedByName).HasMaxLength(200);
        builder.Property(c => c.Note).HasMaxLength(2000);

        builder.HasIndex(c => new { c.TenantId, c.QuoteId, c.ChangedAt });
    }
}

internal sealed class QuoteShareLinkConfiguration : IEntityTypeConfiguration<QuoteShareLink>
{
    public void Configure(EntityTypeBuilder<QuoteShareLink> builder)
    {
        builder.ToTable("quote_share_links");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.TokenHash).IsRequired().HasMaxLength(64);

        builder.HasOne<Quote>().WithMany().HasForeignKey(l => l.QuoteId).OnDelete(DeleteBehavior.Cascade);

        // The public page resolves a link by hash alone; unique so a hash names one quote.
        builder.HasIndex(l => l.TokenHash).IsUnique().HasDatabaseName("IX_quote_share_links_TokenHash");
        builder.HasIndex(l => new { l.TenantId, l.QuoteId });
    }
}

internal sealed class QuoteSettingsConfiguration : IEntityTypeConfiguration<QuoteSettings>
{
    public void Configure(EntityTypeBuilder<QuoteSettings> builder)
    {
        builder.ToTable("quote_settings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.NumberPrefix).IsRequired().HasMaxLength(20);
        builder.Property(s => s.DefaultTerms).HasMaxLength(8000);
        builder.Property(s => s.ApprovalDiscountThresholdPercent).HasPrecision(9, 4);

        builder.HasIndex(s => s.TenantId).IsUnique().HasDatabaseName("IX_quote_settings_TenantId");
    }
}
