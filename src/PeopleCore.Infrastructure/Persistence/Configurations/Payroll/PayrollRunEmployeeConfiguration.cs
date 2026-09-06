using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class PayrollRunEmployeeConfiguration : IEntityTypeConfiguration<PayrollRunEmployee>
{
    public void Configure(EntityTypeBuilder<PayrollRunEmployee> builder)
    {
        builder.HasKey(x => x.Id);

        // Work details
        builder.Property(x => x.DaysWorked).HasColumnType("numeric(6,2)");
        builder.Property(x => x.OvertimeHours).HasColumnType("numeric(6,2)");
        builder.Property(x => x.HolidayDays).HasColumnType("numeric(6,2)");
        builder.Property(x => x.AbsenceDays).HasColumnType("numeric(6,2)");
        builder.Property(x => x.LateMinutes).HasColumnType("numeric(6,2)");
        builder.Property(x => x.UndertimeMinutes).HasColumnType("numeric(6,2)");
        builder.Property(x => x.NightDiffHours).HasColumnType("numeric(6,2)");
        builder.Property(x => x.RestDayOTHours).HasColumnType("numeric(6,2)");
        builder.Property(x => x.HolidayRegularDays).HasColumnType("numeric(6,2)");
        builder.Property(x => x.HolidaySpecialDays).HasColumnType("numeric(6,2)");

        // Earnings
        builder.Property(x => x.RegularPay).HasColumnType("numeric(18,2)");
        builder.Property(x => x.OvertimePay).HasColumnType("numeric(18,2)");
        builder.Property(x => x.HolidayPay).HasColumnType("numeric(18,2)");
        builder.Property(x => x.NightDiffPay).HasColumnType("numeric(18,2)");
        builder.Property(x => x.TaxableAllowances).HasColumnType("numeric(18,2)");
        builder.Property(x => x.NonTaxableAllowances).HasColumnType("numeric(18,2)");
        builder.Property(x => x.ThirteenthMonth).HasColumnType("numeric(18,2)");

        // Rate basis and attendance adjustments
        builder.Property(x => x.DailyRate).HasColumnType("numeric(18,2)");
        builder.Property(x => x.DailyRateFactor).HasColumnType("numeric(8,2)");
        builder.Property(x => x.AbsenceDeduction).HasColumnType("numeric(18,2)");
        builder.Property(x => x.TardinessDeduction).HasColumnType("numeric(18,2)");

        // Mandatory deductions
        builder.Property(x => x.SSSEmployee).HasColumnType("numeric(18,2)");
        builder.Property(x => x.SSSEmployer).HasColumnType("numeric(18,2)");
        builder.Property(x => x.PhilHealthEmployee).HasColumnType("numeric(18,2)");
        builder.Property(x => x.PhilHealthEmployer).HasColumnType("numeric(18,2)");
        builder.Property(x => x.PagIbigEmployee).HasColumnType("numeric(18,2)");
        builder.Property(x => x.PagIbigEmployer).HasColumnType("numeric(18,2)");
        builder.Property(x => x.WithholdingTax).HasColumnType("numeric(18,2)");

        // Other deductions
        builder.Property(x => x.LoanDeductions).HasColumnType("numeric(18,2)");
        builder.Property(x => x.OtherDeductions).HasColumnType("numeric(18,2)");

        // Computed properties - not columns.
        builder.Ignore(x => x.GrossPay);
        builder.Ignore(x => x.TotalDeductions);
        builder.Ignore(x => x.NetPay);
        builder.Ignore(x => x.TotalEmployerCost);

        // An employee with payroll history cannot be deleted.
        builder.HasOne(x => x.Employee)
               .WithMany()
               .HasForeignKey(x => x.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.LoanDeductionLines)
               .WithOne(x => x.PayrollRunEmployee)
               .HasForeignKey(x => x.PayrollRunEmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
