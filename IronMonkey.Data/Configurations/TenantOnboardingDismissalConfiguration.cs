using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class TenantOnboardingDismissalConfiguration
    : IEntityTypeConfiguration<TenantOnboardingDismissal>
{
    public void Configure(EntityTypeBuilder<TenantOnboardingDismissal> builder)
    {
        builder.ToTable("tenant_onboarding_dismissals");
        builder.HasKey(d => d.Id);

        // One row per user per tenant, enforced by the database. Two rows could disagree, and
        // whichever the read happened to pick would make a dismissal look intermittent.
        builder.HasIndex(d => new { d.TenantId, d.UserId })
            .IsUnique()
            .HasDatabaseName("IX_TenantOnboardingDismissals_TenantId_UserId");
    }
}
