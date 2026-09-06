namespace PeopleCore.Application.Payroll.Interfaces;

public interface IPayslipService
{
    /// <summary>One employee's payslip, or null when the run or the entry does not exist.</summary>
    Task<byte[]?> GenerateAsync(Guid runId, Guid employeeId, CancellationToken ct = default);

    /// <summary>Every payslip in the run merged into one PDF, or null when the run does not exist.</summary>
    Task<byte[]?> GenerateForRunAsync(Guid runId, CancellationToken ct = default);
}
