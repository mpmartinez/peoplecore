using Microsoft.Playwright;
using PeopleCore.E2E.Tests.Infrastructure;

namespace PeopleCore.E2E.Tests.Organization;

public sealed class DepartmentTests(PeopleCoreApp app) : E2ETest(app)
{
    [Fact]
    public async Task AnAdministrator_AddsADepartment_AndItIsStillThereAfterAReload()
    {
        var name = Unique("Finance");
        await SignInAsync();
        await Page.GotoAsync("/departments");

        await Page.GetByPlaceholder("Department name").FillAsync(name);
        await Page.GetByPlaceholder("e.g. HR, FIN").FillAsync("FIN");
        await Page.GetByRole(AriaRole.Button, new() { Name = "+ Add" }).ClickAsync();

        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await Expect(row).ToBeVisibleAsync();
        await Expect(row).ToContainTextAsync("FIN");

        // Saved by the API, not just added to the page's list.
        await Page.ReloadAsync();
        await Expect(row).ToBeVisibleAsync();
    }
}
