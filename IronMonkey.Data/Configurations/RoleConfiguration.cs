using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IronMonkey.Data.Entities;

namespace IronMonkey.Data.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(role => role.Id);

        builder.HasMany(role => role.Permissions)
            .WithMany()
            .UsingEntity<RolePermission>();

        builder.HasData([
            Role.SuperAdmin,
            Role.Admin,
            Role.Owner,
            Role.TeleCaller
        ]);
    }
}