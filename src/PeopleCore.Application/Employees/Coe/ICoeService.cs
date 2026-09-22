namespace PeopleCore.Application.Employees.Coe;

/// <summary>Gathers an employee's facts and produces their Certificate of Employment PDF.</summary>
public interface ICoeService
{
    Task<(byte[] Pdf, string FileName)> GenerateAsync(Guid employeeId, CoeRequest request, CancellationToken ct = default);
}
