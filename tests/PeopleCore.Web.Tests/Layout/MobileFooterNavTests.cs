using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using PeopleCore.Web.Layout;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Layout;

public class MobileFooterNavTests : BunitContext
{
    private readonly BunitAuthorizationContext _auth;

    public MobileFooterNavTests() => _auth = AddAuthorization();

    private IRenderedComponent<MobileFooterNav> RenderAs(params string[] roles)
    {
        _auth.SetAuthorized("someone@company.test");
        _auth.SetClaims(SeededPermissions.ClaimsFor(roles));
        return Render<MobileFooterNav>();
    }

    private static IRenderedComponent<MobileFooterNav> WithMoreOpen(IRenderedComponent<MobileFooterNav> cut)
    {
        cut.Find("nav button").Click();
        return cut;
    }

    private static List<string> MoreLinks(IRenderedComponent<MobileFooterNav> cut) =>
        cut.FindAll("div.rounded-t-2xl a[href]").Select(a => a.GetAttribute("href")!).ToList();

    [Fact]
    public void AnEmployeeWithNoRoles_StillHasSomethingUnderMore_NotABlankSheet()
    {
        var cut = WithMoreOpen(RenderAs());

        MoreLinks(cut).Should().Equal("my-payslips");
        cut.FindAll("div.rounded-t-2xl p").Select(p => p.TextContent).Should().Equal("My HR");
    }

    [Fact]
    public void AnAdmin_SeesEveryGroup_UnderMore()
    {
        var links = MoreLinks(WithMoreOpen(RenderAs("Admin")));

        links.Should().Contain(["my-payslips", "employees", "leave-approvals", "payroll-runs", "job-postings", "admin/users", "admin/roles", "admin/email"]);
        links.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AManager_SeesApprovals_ButNoHeadingForGroupsWithNoLinks()
    {
        var cut = WithMoreOpen(RenderAs("Manager"));

        MoreLinks(cut).Should().Equal("my-payslips", "leave-approvals", "overtime-approvals", "performance");
        cut.FindAll("div.rounded-t-2xl p").Select(p => p.TextContent).Should().Equal("My HR", "Management");
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("HRManager", false)]
    [InlineData("Manager", false)]
    public void TheRolesPage_IsListedOnlyForThoseWhoManageRoles(string role, bool listed)
    {
        MoreLinks(WithMoreOpen(RenderAs(role))).Contains("admin/roles").Should().Be(listed);
    }
}
