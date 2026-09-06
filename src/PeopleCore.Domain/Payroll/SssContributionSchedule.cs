using System.Linq;

namespace PeopleCore.Domain.Payroll;

/// <summary>
/// One SSS Schedule of Contributions, as issued by a single SSS circular.
/// <para>
/// Schedules are effective-dated because a payroll run must be computed under the schedule
/// that was in force for its period - not under whatever schedule is newest. Registering a
/// new circular means ADDING an entry, never editing an existing one.
/// </para>
/// </summary>
public sealed record SssSchedule(
    DateOnly EffectiveFrom,
    string Citation,
    (decimal MaxSalary, decimal EmployeeContrib, decimal EmployerContrib)[] Rows);

/// <summary>
/// SSS Schedule of Contributions tables, effective-dated by circular.
/// </summary>
public static class SssContributionSchedule
{
    // ─── SSS Schedule of Contributions — Circular No. 2024-006 ────────────────
    // "Schedule of SSS Contributions Effective January 2025", issued 19 Dec 2024,
    // repealing Circular No. 2022-033. This is the schedule currently in force.
    //
    // Total rate is 15% of the Monthly Salary Credit: employee 5%, employer 10%.
    // MSC runs 5,000 to 35,000 in steps of 500. Compensation maps to the NEAREST
    // MSC, so each bracket spans MSC-250 .. MSC+249.99 (first row is "below 5,250",
    // last is "34,750 and over").
    //
    // EmployerContrib is the employer's full remittance: 10% of the MSC (Regular SS
    // on the first 20,000 plus MPF on the excess) PLUS the Employees' Compensation
    // (EC) contribution — 10.00 below MSC 15,000, 30.00 from MSC 15,000 up.
    // EmployeeContrib is the employee's 5% (Regular SS plus MPF).
    private static readonly (decimal MaxSalary, decimal EmployeeContrib, decimal EmployerContrib)[] Sss2025Rows =
    [
        (  5_249.99m,    250.00m,    510.00m),
        (  5_749.99m,    275.00m,    560.00m),
        (  6_249.99m,    300.00m,    610.00m),
        (  6_749.99m,    325.00m,    660.00m),
        (  7_249.99m,    350.00m,    710.00m),
        (  7_749.99m,    375.00m,    760.00m),
        (  8_249.99m,    400.00m,    810.00m),
        (  8_749.99m,    425.00m,    860.00m),
        (  9_249.99m,    450.00m,    910.00m),
        (  9_749.99m,    475.00m,    960.00m),
        ( 10_249.99m,    500.00m,  1_010.00m),
        ( 10_749.99m,    525.00m,  1_060.00m),
        ( 11_249.99m,    550.00m,  1_110.00m),
        ( 11_749.99m,    575.00m,  1_160.00m),
        ( 12_249.99m,    600.00m,  1_210.00m),
        ( 12_749.99m,    625.00m,  1_260.00m),
        ( 13_249.99m,    650.00m,  1_310.00m),
        ( 13_749.99m,    675.00m,  1_360.00m),
        ( 14_249.99m,    700.00m,  1_410.00m),
        ( 14_749.99m,    725.00m,  1_460.00m),
        ( 15_249.99m,    750.00m,  1_530.00m),
        ( 15_749.99m,    775.00m,  1_580.00m),
        ( 16_249.99m,    800.00m,  1_630.00m),
        ( 16_749.99m,    825.00m,  1_680.00m),
        ( 17_249.99m,    850.00m,  1_730.00m),
        ( 17_749.99m,    875.00m,  1_780.00m),
        ( 18_249.99m,    900.00m,  1_830.00m),
        ( 18_749.99m,    925.00m,  1_880.00m),
        ( 19_249.99m,    950.00m,  1_930.00m),
        ( 19_749.99m,    975.00m,  1_980.00m),
        ( 20_249.99m,  1_000.00m,  2_030.00m),
        ( 20_749.99m,  1_025.00m,  2_080.00m),
        ( 21_249.99m,  1_050.00m,  2_130.00m),
        ( 21_749.99m,  1_075.00m,  2_180.00m),
        ( 22_249.99m,  1_100.00m,  2_230.00m),
        ( 22_749.99m,  1_125.00m,  2_280.00m),
        ( 23_249.99m,  1_150.00m,  2_330.00m),
        ( 23_749.99m,  1_175.00m,  2_380.00m),
        ( 24_249.99m,  1_200.00m,  2_430.00m),
        ( 24_749.99m,  1_225.00m,  2_480.00m),
        ( 25_249.99m,  1_250.00m,  2_530.00m),
        ( 25_749.99m,  1_275.00m,  2_580.00m),
        ( 26_249.99m,  1_300.00m,  2_630.00m),
        ( 26_749.99m,  1_325.00m,  2_680.00m),
        ( 27_249.99m,  1_350.00m,  2_730.00m),
        ( 27_749.99m,  1_375.00m,  2_780.00m),
        ( 28_249.99m,  1_400.00m,  2_830.00m),
        ( 28_749.99m,  1_425.00m,  2_880.00m),
        ( 29_249.99m,  1_450.00m,  2_930.00m),
        ( 29_749.99m,  1_475.00m,  2_980.00m),
        ( 30_249.99m,  1_500.00m,  3_030.00m),
        ( 30_749.99m,  1_525.00m,  3_080.00m),
        ( 31_249.99m,  1_550.00m,  3_130.00m),
        ( 31_749.99m,  1_575.00m,  3_180.00m),
        ( 32_249.99m,  1_600.00m,  3_230.00m),
        ( 32_749.99m,  1_625.00m,  3_280.00m),
        ( 33_249.99m,  1_650.00m,  3_330.00m),
        ( 33_749.99m,  1_675.00m,  3_380.00m),
        ( 34_249.99m,  1_700.00m,  3_430.00m),
        ( 34_749.99m,  1_725.00m,  3_480.00m),
        (decimal.MaxValue,  1_750.00m,  3_530.00m)  // 34,750 and over (MSC = 35,000)
    ];

    /// <summary>
    /// SSS schedules in force, ordered by <see cref="SssSchedule.EffectiveFrom"/> descending
    /// (newest first). When SSS issues a circular, add an entry here; do not edit an existing
    /// one, or historical runs will silently change value.
    /// </summary>
    public static readonly SssSchedule[] Schedules =
    [
        new(new DateOnly(2025, 1, 1), "SSS Circular No. 2024-006", Sss2025Rows)
    ];

    /// <summary>The schedule in force for <paramref name="asOf"/>, never simply the newest.</summary>
    public static SssSchedule ForPeriod(DateOnly asOf) =>
        Schedules.First(s => s.EffectiveFrom <= asOf);
}
