using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;

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
        builder.Property(lt => lt.IsConvertibleToCash).HasDefaultValue(false);
        builder.Property(lt => lt.CountsAsVacationForDeMinimis).HasDefaultValue(true);

        builder.Property(lt => lt.EntitlementKind).HasConversion<string>().HasMaxLength(40)
               .HasDefaultValue(LeaveEntitlementKind.Accrued);
        builder.Property(lt => lt.CountsCalendarDays).HasDefaultValue(false);
        builder.Property(lt => lt.DaysPerEvent).HasPrecision(5, 2);
        builder.Property(lt => lt.RequiresMarried).HasDefaultValue(false);
        builder.Property(lt => lt.RequiresSoloParentId).HasDefaultValue(false);
        builder.Property(lt => lt.IsConfidential).HasDefaultValue(false);
        builder.Property(lt => lt.IsMaternity).HasDefaultValue(false);

        builder.ToTable("leave_types");
    }
}
