namespace PeopleCore.Application.Payroll.DTOs;

public record PayrollEarningLineDto(string Description, decimal Amount, bool IsTaxable = true);
public record PayrollDeductionLineDto(string Description, decimal Amount, bool IsEmployer = false);
