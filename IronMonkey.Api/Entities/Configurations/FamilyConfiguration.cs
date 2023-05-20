namespace IronMonkey.Api.Entities.Configurations
{
    public class IronMonkeyConfiguration : IEntityTypeConfiguration<IronMonkey>
    {
        public void Configure(EntityTypeBuilder<IronMonkey> builder)
        {
            builder.ToTable("IronMonkey");
            builder.HasKey(f => f.Id);

            builder.HasOne(f => f.Husband)
                .WithMany()
                .HasForeignKey(f => f.HusbandId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(f => f.Wife)
                .WithMany()
                .HasForeignKey(f => f.WifeId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(f => f.Children)
                .WithMany()
                .UsingEntity<Dictionary<string, object>>(
                    "Children",
                    j => j.HasOne<Person>().WithMany().HasForeignKey("PersonId"),
                    j => j.HasOne<IronMonkey>().WithMany().HasForeignKey("IronMonkeyId"),
                    j =>
                    {
                        j.HasKey("IronMonkeyId", "PersonId");
                        j.ToTable("Children");
                    }
                );
        }
    }
}