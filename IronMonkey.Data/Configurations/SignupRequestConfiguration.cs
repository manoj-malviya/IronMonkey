using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

public class SignupRequestConfiguration : IEntityTypeConfiguration<SignupRequest>
{
    public void Configure(EntityTypeBuilder<SignupRequest> builder)
    {
        builder.ToTable("SignupRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CompanyName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.AdminEmail).IsRequired().HasMaxLength(320);
        builder.Property(x => x.Phone).IsRequired().HasMaxLength(50);
        builder.Property(x => x.RecipeId);
        builder.Property(x => x.CompanySize).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Address).IsRequired().HasMaxLength(500);
        builder.Property(x => x.BillingContact).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(50);
        builder.Property(x => x.ReviewNote).HasMaxLength(1000);
        builder.HasIndex(x => x.AdminEmail);
    }
}
