namespace PeopleCore.DemoSeed.Plan;

/// <summary>
/// The twenty jobs in Bayanihan Trading, in creation order: every manager comes before the people who
/// report to them. Salaries are monthly pesos. Hire dates are fixed so the hiring trend has a shape.
/// </summary>
public static class Roster
{
    public record Seat(
        string Department, string Title, string? Team, int? ManagerNumber, decimal MonthlySalary,
        DateOnly HireDate, string EmploymentStatus);

    public static readonly IReadOnlyList<Seat> Seats =
    [
        /*  1 */ new("Executive", "General Manager", null, null, 150_000m, new(2018, 3, 5), "Regular"),
        /*  2 */ new("Human Resources", "HR Manager", null, 1, 85_000m, new(2019, 6, 17), "Regular"),
        /*  3 */ new("Human Resources", "HR Officer", null, 2, 32_000m, new(2022, 2, 1), "Regular"),
        /*  4 */ new("Finance", "Finance Manager", null, 1, 95_000m, new(2018, 9, 3), "Regular"),
        /*  5 */ new("Finance", "Senior Accountant", null, 4, 45_000m, new(2020, 1, 13), "Regular"),
        /*  6 */ new("Finance", "Accountant", null, 4, 30_000m, new(2023, 7, 10), "Regular"),
        /*  7 */ new("Operations", "Operations Manager", null, 1, 90_000m, new(2018, 5, 21), "Regular"),
        /*  8 */ new("Operations", "Warehouse Supervisor", "Warehouse", 7, 38_000m, new(2019, 11, 4), "Regular"),
        /*  9 */ new("Operations", "Inventory Clerk", "Warehouse", 8, 21_000m, new(2021, 4, 5), "Regular"),
        /* 10 */ new("Operations", "Warehouse Staff", "Warehouse", 8, 18_000m, new(2022, 8, 15), "Regular"),
        /* 11 */ new("Operations", "Warehouse Staff", "Warehouse", 8, 18_000m, new(2024, 3, 11), "Regular"),
        /* 12 */ new("Operations", "Logistics Coordinator", "Logistics", 7, 26_000m, new(2023, 1, 16), "Regular"),
        /* 13 */ new("Operations", "Delivery Driver", "Logistics", 12, 19_000m, new(2026, 2, 2), "Probationary"),
        /* 14 */ new("Sales", "Sales Manager", null, 1, 88_000m, new(2019, 2, 18), "Regular"),
        /* 15 */ new("Sales", "Account Executive", "Metro Manila", 14, 28_000m, new(2021, 10, 4), "Regular"),
        /* 16 */ new("Sales", "Account Executive", "Metro Manila", 14, 28_000m, new(2023, 5, 22), "Regular"),
        /* 17 */ new("Sales", "Account Executive", "Provincial", 14, 26_000m, new(2026, 6, 1), "Probationary"),
        /* 18 */ new("IT", "IT Lead", null, 1, 80_000m, new(2020, 8, 10), "Regular"),
        /* 19 */ new("IT", "Software Developer", null, 18, 55_000m, new(2022, 10, 3), "Regular"),
        /* 20 */ new("IT", "Software Developer", null, 18, 42_000m, new(2024, 9, 9), "Regular"),
    ];
}
