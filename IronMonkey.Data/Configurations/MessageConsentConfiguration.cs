using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class MessageConsentConfiguration : IEntityTypeConfiguration<MessageConsent>
{
    public void Configure(EntityTypeBuilder<MessageConsent> builder)
    {
        builder.ToTable("message_consents");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Address).HasMaxLength(320).IsRequired();
        builder.Property(c => c.Source).HasMaxLength(50).IsRequired();

        // One consent state per address per channel. Unique rather than merely indexed: two
        // rows for the same address could disagree, and a suppression check that reads the
        // wrong one messages someone who opted out. The database refuses the ambiguity
        // outright.
        builder.HasIndex(c => new { c.TenantId, c.Channel, c.Address })
            .IsUnique()
            .HasDatabaseName("IX_MessageConsents_TenantId_Channel_Address");
    }
}
