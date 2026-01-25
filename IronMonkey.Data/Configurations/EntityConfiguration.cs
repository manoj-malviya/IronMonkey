using IronMonkey.Data.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IronMonkey.Data.Configurations;

public class EntityConfiguration<T> : IEntityTypeConfiguration<T> where T : Entity
{
    public void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OrganizationId)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false);

        builder.HasIndex(e => e.OrganizationId);
    }
}