using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Presentation;

namespace IronMonkey.Data.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(tenant => tenant.Id);

        builder.Property(tenant => tenant.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(tenant => tenant.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(tenant => tenant.Slug)
            .IsUnique();

        builder.Property(tenant => tenant.SubscriptionPlan)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(tenant => tenant.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(tenant => tenant.DatabaseConnectionString)
            .HasMaxLength(500);

        builder.Property(tenant => tenant.ThemeSettings)
            .HasColumnType("jsonb");

        // Serialized rather than mapped to columns: the shape is a presentation concern that
        // will keep growing, and none of it is ever queried or filtered on — it is read whole
        // for one tenant at a time.
        //
        // The comparer is required. Without one EF compares the POCO by reference, so mutating
        // a nested value in place (settings.Branding.PrimaryColor = ...) is invisible to change
        // tracking and the update is silently dropped. Comparing by serialized form makes the
        // edit detectable however the caller made it.
        builder.Property(tenant => tenant.Presentation)
            .HasColumnType("jsonb")
            .HasConversion(
                settings => Serialize(settings),
                json => Deserialize(json),
                new ValueComparer<TenantPresentationSettings?>(
                    (left, right) => Serialize(left) == Serialize(right),
                    settings => settings == null ? 0 : Serialize(settings)!.GetHashCode(),
                    settings => Deserialize(Serialize(settings))));

        builder.Property(tenant => tenant.ApprovalStatus)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Pending");

        builder.Property(tenant => tenant.ApprovalNote)
            .HasMaxLength(1000);

        builder.Property(tenant => tenant.IsProvisioned)
            .HasDefaultValue(false);

        builder.Property(tenant => tenant.ApprovedAt);
        builder.Property(tenant => tenant.ProvisionedAt);
        builder.Property(tenant => tenant.AppliedRecipeId);
        builder.Property(tenant => tenant.AppliedRecipeVersion);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Omit the many nulls a partially-configured tenant produces, so the stored document
        // stays readable and a tenant that configured only terminology does not carry an
        // empty locale and branding block forever.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static string? Serialize(TenantPresentationSettings? settings) =>
        settings is null ? null : JsonSerializer.Serialize(settings, JsonOptions);

    private static TenantPresentationSettings? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<TenantPresentationSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // A malformed document must not make the tenant unloadable — every consumer
            // already treats null as "configured nothing" and falls back to defaults.
            return null;
        }
    }
}
