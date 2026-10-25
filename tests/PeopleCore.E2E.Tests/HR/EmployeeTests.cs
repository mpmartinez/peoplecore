using Microsoft.Playwright;
using PeopleCore.E2E.Tests.Infrastructure;

namespace PeopleCore.E2E.Tests.HR;

public sealed class EmployeeTests(PeopleCoreApp app) : E2ETest(app)
{
    [Fact]
    public async Task AnAdministrator_AddsAnEmployee_AndFindsThemBySearch()
    {
        var number = Unique("E2E");
        var lastName = Unique("Dela Cruz");
        await SignInAsync();
        await Page.GotoAsync("/employees");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Employee" }).ClickAsync();
        await Page.GetByLabel("Employee Number").FillAsync(number);
        await Page.GetByLabel("First Name").FillAsync("Maria");
        await Page.GetByLabel("Last Name").FillAsync(lastName);
        await Page.GetByLabel("Gender").SelectOptionAsync("Female");
        await Page.GetByLabel("Work Email").FillAsync($"{number.ToLowerInvariant()}@peoplecore.test");
        await Page.GetByLabel("Employment Status").SelectOptionAsync("Probationary");
        await Page.GetByLabel("Employment Type").SelectOptionAsync("Probationary");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save Employee" }).ClickAsync();

        // The form closes only once the API has accepted the employee.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save Employee" })).ToBeHiddenAsync();

        await Page.GetByPlaceholder("Search by name or number...").FillAsync(number);

        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = number });
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToContainTextAsync($"Maria {lastName}");
        await Expect(row).ToContainTextAsync("Active");
    }

    [Fact]
    public async Task SavingAnEmployee_WithoutTheRequiredFields_IsRefusedWithAReason()
    {
        await SignInAsync();
        await Page.GotoAsync("/employees");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Add Employee" }).ClickAsync();
        await Page.GetByLabel("First Name").FillAsync("Juan");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save Employee" }).ClickAsync();

        await Expect(Page.GetByText("Please fill in all required fields.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save Employee" })).ToBeVisibleAsync();
    }
}
