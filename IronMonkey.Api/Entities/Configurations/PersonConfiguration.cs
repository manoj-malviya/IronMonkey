namespace IronMonkey.Api.Entities.Configurations
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class PersonConfiguration : IEntityTypeConfiguration<Person>
    {
        public void Configure(EntityTypeBuilder<Person> builder)
        {
            builder.ToTable("Person");
            builder.HasKey(p => p.Id);

            builder.Property(p => p.Name).IsRequired().HasMaxLength(255);
            builder.Property(p => p.DateOfBirth);
            builder.Property(p => p.PlaceOfBirth).HasMaxLength(255);
            builder.Property(p => p.Sex).HasMaxLength(10);

            builder.HasOne(p => p.Spouse)
                .WithMany()
                .HasForeignKey(p => p.SpouseId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}