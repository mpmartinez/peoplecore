using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class PayrollLoanDeductionConfiguration : IEntityTypeConfiguration<PayrollLoanDeduction>
{
    public void Configure(EntityTypeBuilder<PayrollLoanDeduction> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.LoanType).HasMaxLength(64);

        builder.HasIndex(x => x.EmployeeLoanId);
    }
}
