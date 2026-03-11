using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Organization;

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Title).IsRequired().HasMaxLength(200);
        builder.HasOne(p => p.Department)
               .WithMany(d => d.Positions)
               .HasForeignKey(p => p.DepartmentId);
        builder.ToTable("positions");
    }
}
