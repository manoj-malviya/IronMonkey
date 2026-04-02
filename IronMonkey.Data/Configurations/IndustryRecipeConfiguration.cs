using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class IndustryRecipeConfiguration : IEntityTypeConfiguration<IndustryRecipe>
{
    public void Configure(EntityTypeBuilder<IndustryRecipe> builder)
    {
        builder.ToTable("industry_recipes");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Description)
            .HasMaxLength(1000);

        builder.Property(r => r.IndustrySlug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.IconIdentifier)
            .HasMaxLength(100);

        builder.Property(r => r.IsBlank)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(r => r.Version)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(r => r.ContentJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValue("{}");

        builder.HasIndex(r => r.IndustrySlug).IsUnique();
        builder.HasIndex(r => r.IsActive);
    }
}
