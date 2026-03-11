using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Leave;

public class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.HasKey(lt => lt.Id);
        builder.HasIndex(lt => lt.Code).IsUnique();
        builder.Property(lt => lt.Name).IsRequired().HasMaxLength(100);
        builder.Property(lt => lt.Code).IsRequired().HasMaxLength(50);
        builder.Property(lt => lt.MaxDaysPerYear).HasPrecision(5, 2);
        builder.Property(lt => lt.CarryOverMaxDays).HasPrecision(5, 2);
        builder.ToTable("leave_types");
    }
}
