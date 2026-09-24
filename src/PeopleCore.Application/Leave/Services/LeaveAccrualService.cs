using System.Text.Json;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Interfaces;

namespace PeopleCore.Application.Leave.Services;

public class LeaveAccrualService : ILeaveAccrualService
{
    private readonly ILeaveAccrualRepository _accrualRepo;
    private readonly ILeaveBalanceRepository _balanceRepo;
    private readonly IEmployeeRepository _employeeRepo;

    public LeaveAccrualService(
        ILeaveAccrualRepository accrualRepo,
        ILeaveBalanceRepository balanceRepo,
        IEmployeeRepository employeeRepo)
    {
        _accrualRepo = accrualRepo;
        _balanceRepo = balanceRepo;
        _employeeRepo = employeeRepo;
    }

    public async Task<IReadOnlyList<LeaveAccrualPolicyDto>> GetPoliciesAsync(Guid leaveTypeId, CancellationToken ct = default)
    {
        var policies = await _accrualRepo.GetPoliciesByLeaveTypeAsync(leaveTypeId, ct);
        return policies.Select(p => new LeaveAccrualPolicyDto(
            p.Id,
            p.LeaveTypeId,
            p.LeaveType?.Name ?? string.Empty,
            p.TenureMonthsMin,
            p.TenureMonthsMax,
            p.DaysPerYear,
            p.AccrualFrequency.ToString(),
            p.IsActive)).ToList().AsReadOnly();
    }

    public async Task<LeaveAccrualPolicyDto> CreatePolicyAsync(CreateLeaveAccrualPolicyRequest request, CancellationToken ct = default)
    {
        var policy = new LeaveAccrualPolicy
        {
            LeaveTypeId = request.LeaveTypeId,
            TenureMonthsMin = request.TenureMonthsMin,
            TenureMonthsMax = request.TenureMonthsMax,
            DaysPerYear = request.DaysPerYear,
            AccrualFrequency = request.AccrualFrequency,
            IsActive = true
        };

        var created = await _accrualRepo.AddPolicyAsync(policy, ct);
        var loaded = await _accrualRepo.GetPolicyByIdAsync(created.Id, ct);

        return new LeaveAccrualPolicyDto(
            loaded!.Id,
            loaded.LeaveTypeId,
            loaded.LeaveType?.Name ?? string.Empty,
            loaded.TenureMonthsMin,
            loaded.TenureMonthsMax,
            loaded.DaysPerYear,
            loaded.AccrualFrequency.ToString(),
            loaded.IsActive);
    }

    public async Task UpdatePolicyAsync(Guid id, CreateLeaveAccrualPolicyRequest request, CancellationToken ct = default)
    {
        var policy = await _accrualRepo.GetPolicyByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave accrual policy {id} not found.");

        policy.LeaveTypeId = request.LeaveTypeId;
        policy.TenureMonthsMin = request.TenureMonthsMin;
        policy.TenureMonthsMax = request.TenureMonthsMax;
        policy.DaysPerYear = request.DaysPerYear;
        policy.AccrualFrequency = request.AccrualFrequency;

        await _accrualRepo.UpdatePolicyAsync(policy, ct);
    }

    public async Task DeletePolicyAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await _accrualRepo.GetPolicyByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave accrual policy {id} not found.");

        await _accrualRepo.DeletePolicyAsync(policy, ct);
    }

    public async Task RunAccrualsAsync(int year, int month, CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);
        var activeEmployees = employees.Where(e => e.SeparationDate == null);
        // Only accrued types build balances from policies: a yearly allowance's balance is created on
        // first filing and a per-event type has none, so accruing either would grant days twice or
        // out of nothing. An inactive type can't be filed, so it accrues nothing either. The
        // repository loads each policy with its type.
        var policies = (await _accrualRepo.GetAllActivePoliciesAsync(ct))
            .Where(p => p.LeaveType is { IsActive: true, EntitlementKind: LeaveEntitlementKind.Accrued })
            .ToList();
        var accrualDate = new DateOnly(year, month, 1);

        foreach (var employee in activeEmployees)
        {
            var tenureMonths = ((accrualDate.Year - employee.HireDate.Year) * 12)
                              + accrualDate.Month - employee.HireDate.Month;

            foreach (var policy in policies)
            {
                // A type for one gender accrues nothing for the other (the same test LeaveRules files
                // by). A blank restriction means any gender, as saving a type now stores it.
                if (!string.IsNullOrWhiteSpace(policy.LeaveType.GenderRestriction)
                    && policy.LeaveType.GenderRestriction.Trim() != employee.Gender.ToString()) continue;

                if (tenureMonths < policy.TenureMonthsMin) continue;
                if (policy.TenureMonthsMax.HasValue && tenureMonths > policy.TenureMonthsMax.Value) continue;

                if (policy.DaysPerYear <= 0) continue;

                // For annual policies, only accrue in January (month 1)
                if (policy.AccrualFrequency == AccrualFrequency.Annual && month != 1)
                    continue;

                if (await _accrualRepo.TransactionExistsAsync(employee.Id, policy.LeaveTypeId, year, month, ct))
                    continue;

                var daysAccrued = policy.AccrualFrequency == AccrualFrequency.Annual
                    ? policy.DaysPerYear
                    : MonthlyAmount(policy.DaysPerYear, month);
                var transaction = new LeaveAccrualTransaction
                {
                    EmployeeId = employee.Id,
                    LeaveTypeId = policy.LeaveTypeId,
                    AccrualDate = accrualDate,
                    DaysAccrued = daysAccrued,
                    PolicySnapshot = JsonSerializer.Serialize(policy),
                    PeriodYear = year,
                    PeriodMonth = month
                };
                await _accrualRepo.AddTransactionAsync(transaction, ct);

                // Update the employee's leave balance
                var balance = await _balanceRepo.GetByEmployeeAndTypeAsync(employee.Id, policy.LeaveTypeId, year, ct);
                if (balance is not null)
                {
                    balance.TotalDays += daysAccrued;
                    await _balanceRepo.UpdateAsync(balance, ct);
                }
                else
                {
                    await _balanceRepo.AddAsync(new LeaveBalance
                    {
                        EmployeeId = employee.Id,
                        LeaveTypeId = policy.LeaveTypeId,
                        Year = year,
                        TotalDays = daysAccrued
                    }, ct);
                }
            }
        }
    }

    /// <summary>
    /// Month <paramref name="month"/>'s share of <paramref name="daysPerYear"/>, at the two decimals
    /// the balances store: the rounded running total to this month less the rounded running total to
    /// last month. The twelve months then add up to exactly <paramref name="daysPerYear"/> (5 days
    /// is 0.42, 0.41, 0.42, 0.42, ...; rounding each month's 5/12 on its own would give 0.42 x 12 =
    /// 5.04). An amount that divides evenly, such as 15 days at 1.25, is the same every month.
    /// </summary>
    internal static decimal MonthlyAmount(decimal daysPerYear, int month)
        => RunningTotal(daysPerYear, month) - RunningTotal(daysPerYear, month - 1);

    private static decimal RunningTotal(decimal daysPerYear, int months)
        => Math.Round(daysPerYear * months / 12m, 2, MidpointRounding.AwayFromZero);

    public async Task<IReadOnlyList<LeaveAccrualTransactionDto>> GetEmployeeAccrualHistoryAsync(Guid employeeId, CancellationToken ct = default)
    {
        var transactions = await _accrualRepo.GetTransactionsByEmployeeAsync(employeeId, ct);
        return transactions.Select(t => new LeaveAccrualTransactionDto(
            t.Id,
            t.LeaveType?.Name ?? string.Empty,
            t.AccrualDate,
            t.DaysAccrued,
            t.PeriodYear,
            t.PeriodMonth)).ToList().AsReadOnly();
    }
}
