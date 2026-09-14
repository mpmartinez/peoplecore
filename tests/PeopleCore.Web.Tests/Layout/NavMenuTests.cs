using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Layout;

namespace PeopleCore.Web.Tests.Layout;

public class NavMenuTests : BunitContext
{
    private readonly BunitAuthorizationContext _auth;

    public NavMenuTests() => _auth = AddAuthorization();

    private IRenderedComponent<NavMenu> RenderAs(params string[] roles)
    {
        _auth.SetAuthorized("someone@company.test");
        _auth.SetRoles(roles);
        return Render<NavMenu>();
    }

    private static List<string> Links(IRenderedComponent<NavMenu> cut) =>
        cut.FindAll("a[href]").Select(a => a.GetAttribute("href")!).ToList();

    [Fact]
    public void AnEmployeeWithNoRoles_SeesOnlyTheirOwnHrPages()
    {
        var cut = RenderAs();

        Links(cut).Should().Equal("/", "/my-profile", "/my-attendance", "/my-leave", "/my-payslips");
    }

    [Fact]
    public void AManager_AlsoSeesApprovals_ButNotPayrollOrOrganization()
    {
        var links = Links(RenderAs("Manager"));

        links.Should().Contain(["/leave-approvals", "/overtime-approvals", "/performance"]);
        links.Should().NotContain(["/payroll-runs", "/departments", "/employees", "/analytics"]);
    }

    [Fact]
    public void PayrollService_SeesPayroll_ButNotEmployeesOrApprovals()
    {
        var links = Links(RenderAs("PayrollService"));

        links.Should().Contain(["/payroll-runs", "/bir-2316"]);
        links.Should().NotContain(["/employees", "/leave-approvals"]);
    }

    [Fact]
    public void AnAdmin_SeesEverySection()
    {
        var cut = RenderAs("Admin");

        cut.FindAll("a[href]").Should().HaveCount(17);
        Links(cut).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("HRManager")]
    [InlineData("Manager")]
    [InlineData("PayrollService")]
    public void OnlyAnAdmin_SeesUserAdministration(string role)
    {
        Links(RenderAs(role)).Should().NotContain("/users");
    }

    [Fact]
    public void AnAdmin_SeesUserAdministration()
    {
        Links(RenderAs("Admin")).Should().Contain("/users");
    }

    [Fact]
    public void HighlightsTheSectionForANestedRoute()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/payroll-runs/3f1c0e7a-0000-0000-0000-000000000000?tab=employees");

        var cut = RenderAs("Admin");

        ActiveLinks(cut).Should().Equal("/payroll-runs");
    }

    [Fact]
    public void DashboardIsOnlyActive_OnTheRootItself()
    {
        // "/" is a prefix of every path, so a naive StartsWith would light Dashboard up on every page.
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/my-profile");
        var cut = RenderAs();

        ActiveLinks(cut).Should().Equal("/my-profile");

        nav.NavigateTo("/");
        cut.WaitForAssertion(() => ActiveLinks(cut).Should().Equal("/"));
    }

    [Fact]
    public void DoesNotConfuseASiblingRouteThatSharesAPrefix()
    {
        // /employees must not light up for a route that merely starts with the same letters.
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/employees-archive");

        var cut = RenderAs("Admin");

        ActiveLinks(cut).Should().BeEmpty();
    }

    private static List<string> ActiveLinks(IRenderedComponent<NavMenu> cut) =>
        cut.FindAll("a[href]")
            .Where(a => a.ClassList.Contains("bg-sidebar-accent"))
            .Select(a => a.GetAttribute("href")!)
            .ToList();
}
