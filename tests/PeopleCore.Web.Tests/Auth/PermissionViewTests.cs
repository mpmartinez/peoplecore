using System.Security.Claims;
using Bunit;
using FluentAssertions;
using PeopleCore.Web.Auth;

namespace PeopleCore.Web.Tests.Auth;

public class PermissionViewTests : BunitContext
{
    private IRenderedComponent<PermissionView> Render(params string[] anyOf) =>
        Render<PermissionView>(p => p
            .Add(x => x.AnyOf, anyOf)
            .AddChildContent("""<p id="guarded">Payroll</p>"""));

    [Fact]
    public void HoldingThePermission_ShowsTheContent()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("pay@company.test");
        auth.SetClaims(new Claim(Permissions.ClaimType, Permissions.PayrollManage));

        Render(Permissions.PayrollManage).FindAll("#guarded").Should().ContainSingle();
    }

    [Fact]
    public void HoldingAnyOfSeveral_ShowsTheContent()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("manager@company.test");
        auth.SetClaims(new Claim(Permissions.ClaimType, Permissions.ApprovalsTeam));

        Render(Permissions.ApprovalsTeam, Permissions.ApprovalsAll).FindAll("#guarded").Should().ContainSingle();
    }

    [Fact]
    public void WithoutThePermission_ShowsNothing()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("someone@company.test");
        auth.SetClaims(new Claim(Permissions.ClaimType, Permissions.ApprovalsTeam));

        Render(Permissions.PayrollManage).Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void ASignedOutVisitor_SeesNothing()
    {
        AddAuthorization().SetNotAuthorized();

        Render(Permissions.PayrollManage).Markup.Trim().Should().BeEmpty();
    }
}
