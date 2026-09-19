using PeopleCore.DemoSeed.Api;

namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    /// <summary>
    /// Bayanihan Trading's employer details, printed on payslips and BIR Form 2316. All made up:
    /// the TIN and agency numbers are placeholders, not anyone's real registration.
    /// </summary>
    public static readonly object DemoCompanyProfile = new
    {
        name = "Bayanihan Trading Corporation",
        tin = "123-456-789-00000",
        rdoCode = "050",
        address = "Unit 1204, 88 Ayala Avenue",
        city = "Makati City",
        zipCode = "1226",
        contactEmail = "hr@" + Plan.PeopleBuilder.EmailDomain,
        contactPhone = "(02) 8123 4567",
        sssNumber = "03-9876543-2",
        philHealthNumber = "20-123456789-0",
        pagIbigNumber = "2012-3456-7890",
    };

    /// <summary>
    /// Fills in the employer details, but only on a site that has not set them up: a company with a
    /// TIN already has real details, and the demo must never overwrite those.
    /// </summary>
    private async Task FillCompanyProfileAsync()
    {
        const string step = "Fill in the company details";
        var admin = await AdminAsync();
        var current = await api.GetAsync(step, "api/company-profile", admin);
        if (!string.IsNullOrWhiteSpace(current?["tin"]?.GetValue<string>()))
        {
            Say("Company details: already set on this site, left as they are.");
            return;
        }

        await api.PutAsync(step, "api/company-profile", DemoCompanyProfile, admin);
        Count("company profile");
        Say("Company details: Bayanihan Trading Corporation.");
    }

    /// <summary>
    /// Just the company details, for a site seeded before they were part of the run. Signs in, checks
    /// the account is an Admin with its own password, and nothing else.
    /// </summary>
    public async Task FillCompanyProfileOnlyAsync(string adminEmail, string adminPassword)
    {
        var signIn = await _logins.AddAdminAsync(adminEmail, adminPassword);
        if (Preflight.AdminProblem(signIn) is { } notAdmin)
            throw new PreflightRefusedException($"{notAdmin} Nothing was changed.");

        WroteAnything = true;
        await FillCompanyProfileAsync();
    }
}
