using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    private static readonly string[] Cities =
    [
        "Quezon City", "Makati City", "Pasig City", "Taguig City", "Mandaluyong City",
        "Caloocan City", "Marikina City", "Valenzuela City", "Parañaque City", "Las Piñas City",
    ];

    /// <summary>
    /// Refuses before changing anything if the site already holds the demo, or holds leave setup
    /// the demo's leave plan could not live with. The employee scan narrows the server-side search
    /// to "DEMO-" (EmployeeRepository.GetPagedAsync matches it against EmployeeNumber, FirstName,
    /// LastName and WorkEmail via Contains), so the DEMO- check does not depend on paging landing
    /// every existing employee on some page before an earlier demo's pages are reached. The
    /// StartsWith check on the results still guards against a false hit inside another field.
    /// </summary>
    private async Task PreflightAsync()
    {
        const string step = "Check for an earlier demo";
        for (var page = 1; ; page++)
        {
            var result = await api.GetAsync(step, $"api/employees?page={page}&pageSize=100&search=DEMO-", await AdminAsync());
            var items = result!["items"]!.AsArray();
            if (items.Any(e => e!["employeeNumber"]!.GetValue<string>().StartsWith("DEMO-", StringComparison.Ordinal)))
                throw new AlreadySeededException("This site already has DEMO- employees. Nothing was changed.");
            if (page * 100 >= result["totalCount"]!.GetValue<int>()) break;
        }

        await CheckLeaveSetupAsync();
        Say("Preflight passed: no earlier demo on this site.");
    }

    private async Task CreateOrganizationAsync()
    {
        var companies = (await api.GetAsync("Find the company", "api/companies", await AdminAsync()))!.AsArray();
        if (companies.Count == 0)
            throw new SeedException("Find the company", "GET", "api/companies", 200, "The site has no company record.");
        var companyId = IdOf(companies[0]);

        foreach (var department in plan.People.Select(p => p.Department).Distinct())
        {
            var code = department switch
            {
                "Human Resources" => "HR", "Operations" => "OPS", "Executive" => "EXEC",
                "Finance" => "FIN", "Sales" => "SLS", _ => department.ToUpperInvariant(),
            };
            _departmentIds[department] = IdOf(await api.PostAsync($"Create the {department} department", "api/departments",
                new { companyId, parentDepartmentId = (Guid?)null, name = department, code }, await AdminAsync()));
            Count("departments");
        }

        foreach (var (department, title) in plan.People.Select(p => (p.Department, p.Title)).Distinct())
        {
            _positionIds[(department, title)] = IdOf(await api.PostAsync($"Create the {title} position", "api/positions",
                new { departmentId = _departmentIds[department], title, level = (string?)null }, await AdminAsync()));
            Count("positions");
        }

        foreach (var (department, team) in plan.People.Where(p => p.Team is not null).Select(p => (p.Department, p.Team!)).Distinct())
        {
            _teamIds[(department, team)] = IdOf(await api.PostAsync($"Create the {team} team", "api/teams",
                new { departmentId = _departmentIds[department], name = team }, await AdminAsync()));
            Count("teams");
        }

        Say($"Organisation: {_departmentIds.Count} departments, {_positionIds.Count} positions, {_teamIds.Count} teams.");
    }

    private async Task CreateEmployeesAsync()
    {
        var rng = new Random(plan.Seed);
        foreach (var person in plan.People)
        {
            var step = $"Create {person.EmployeeNumber}";
            var admin = await AdminAsync();
            var departmentId = _departmentIds[person.Department];
            var positionId = _positionIds[(person.Department, person.Title)];
            Guid? managerId = person.ManagerNumber is { } manager ? EmployeeId(manager) : null;
            var employmentType = person.EmploymentStatus == "Probationary" ? "Probationary" : "Regular";

            var id = IdOf(await api.PostAsync(step, "api/employees", new
            {
                employeeNumber = person.EmployeeNumber,
                firstName = person.FirstName, middleName = person.MiddleName, lastName = person.LastName,
                dateOfBirth = person.BirthDate, gender = person.Gender,
                workEmail = person.Email, mobileNumber = person.Mobile,
                departmentId, positionId, reportingManagerId = managerId,
                employmentStatus = person.EmploymentStatus, employmentType,
                hireDate = person.HireDate,
            }, admin));
            _employeeIds[person.Number] = id;

            // Civil status, team, address and regularisation can only be set by an update.
            await api.PutAsync($"{step}: details", $"api/employees/{id}", new
            {
                firstName = person.FirstName, middleName = person.MiddleName, lastName = person.LastName,
                civilStatus = person.CivilStatus, personalEmail = (string?)null, mobileNumber = person.Mobile,
                address = $"{Cities[rng.Next(Cities.Length)]}, Metro Manila",
                departmentId, positionId,
                teamId = person.Team is null ? (Guid?)null : _teamIds[(person.Department, person.Team)],
                reportingManagerId = managerId,
                employmentStatus = person.EmploymentStatus,
                regularizationDate = person.EmploymentStatus == "Regular" ? person.HireDate.AddMonths(6) : (DateOnly?)null,
                is13thMonthEligible = true,
            }, admin);

            foreach (var (idType, idNumber) in new[]
                     {
                         ("SSS", person.Ids.Sss), ("PhilHealth", person.Ids.PhilHealth),
                         ("PagIbig", person.Ids.PagIbig), ("TIN", person.Ids.Tin),
                     })
                await api.PutAsync($"{step}: {idType}", $"api/employees/{id}/government-ids", new { idType, idNumber }, admin);

            await api.PostAsync($"{step}: emergency contact", $"api/employees/{id}/emergency-contacts", new
            {
                name = person.Contact.Name, relationship = person.Contact.Relationship,
                phone = person.Contact.Phone, address = (string?)null,
            }, admin);

            await api.PutAsync($"{step}: pay", $"api/employee-compensation/{id}", new
            {
                basicSalary = person.MonthlySalary, payFrequency = "SemiMonthly",
                taxCode = person.TaxCode, dependents = person.Dependents,
            }, admin);

            Count("employees");
        }

        Say($"Employees: {_employeeIds.Count}, each with pay, government IDs and an emergency contact.");
    }
}
