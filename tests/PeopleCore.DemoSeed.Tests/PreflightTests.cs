using System.Text.Json.Nodes;
using FluentAssertions;
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Seeding;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>The checks the seeder makes before it writes anything.</summary>
public class PreflightTests
{
    [Fact]
    public void AnAdminWithTheirOwnPassword_IsAccepted()
    {
        Preflight.AdminProblem(new SignIn("t", ["Employee", "Admin"], MustChangePassword: false)).Should().BeNull();
    }

    [Theory]
    [InlineData("Employee,HRManager")]
    [InlineData("Employee")]
    [InlineData("")]
    [InlineData("Employee,admin")]
    public void AnAccountWithoutTheAdminRole_IsRefused(string roles)
    {
        var signIn = new SignIn("t", roles.Split(',', StringSplitOptions.RemoveEmptyEntries), MustChangePassword: false);

        Preflight.AdminProblem(signIn).Should().StartWith("PEOPLECORE_ADMIN_EMAIL must be an Admin account with its own password");
    }

    [Fact]
    public void AnAdminStillOnATemporaryPassword_IsRefused()
    {
        var signIn = new SignIn("t", ["Employee", "Admin"], MustChangePassword: true);

        Preflight.AdminProblem(signIn).Should().StartWith("PEOPLECORE_ADMIN_EMAIL must be an Admin account with its own password")
            .And.Contain("temporary password");
    }

    private static JsonNode LeaveType(string genderRestriction, bool isPaid) => JsonNode.Parse($$"""
        {"id":"dd1292fa-9b35-4f49-8130-c3a1984afe87","name":"Vacation Leave","code":"VL","maxDaysPerYear":15.00,
         "isPaid":{{(isPaid ? "true" : "false")}},"isCarryOver":false,"carryOverMaxDays":null,
         "genderRestriction":{{genderRestriction}},"requiresDocument":false,"isActive":true}
        """)!;

    [Fact]
    public void APaidLeaveTypeOpenToEveryone_IsAccepted()
    {
        Preflight.LeaveTypeProblem("VL", LeaveType("null", isPaid: true)).Should().BeNull();
    }

    [Theory]
    [InlineData("\"Female\"")]
    [InlineData("\"Male\"")]
    [InlineData("\"\"")]
    public void ALeaveTypeLimitedToOneGender_IsRefused(string genderRestriction)
    {
        // LeaveRequestService refuses any request whose gender differs from a non-null restriction,
        // an empty string included, so half the demo's leave would be refused.
        Preflight.LeaveTypeProblem("VL", LeaveType(genderRestriction, isPaid: true))
            .Should().Contain("VL").And.Contain("gender");
    }

    [Fact]
    public void AnUnpaidLeaveType_IsRefused()
    {
        Preflight.LeaveTypeProblem("SL", LeaveType("null", isPaid: false)).Should().Contain("SL").And.Contain("unpaid");
    }

    [Fact]
    public void AnInactiveLeaveType_IsRefused()
    {
        // An inactive type accrues nothing and can't be filed.
        var type = LeaveType("null", isPaid: true);
        type["isActive"] = false;

        Preflight.LeaveTypeProblem("VL", type).Should().Contain("VL").And.Contain("inactive");
    }

    [Theory]
    [InlineData("YearlyAllowance")]
    [InlineData("PerEvent")]
    public void ALeaveTypeThatIsNotAccrued_IsRefused(string kind)
    {
        // The demo's balances come from its monthly accrual policies, which only accrued types use.
        var type = LeaveType("null", isPaid: true);
        type["entitlementKind"] = kind;

        Preflight.LeaveTypeProblem("SL", type).Should().Contain("SL").And.Contain("accrue");
    }

    [Theory]
    [InlineData("requiresDocument", "true", "supporting document")]
    [InlineData("minServiceMonths", "6", "6 months of service")]
    [InlineData("requiresMarried", "true", "married")]
    [InlineData("requiresSoloParentId", "true", "solo parent ID")]
    [InlineData("isConfidential", "true", "confidential")]
    [InlineData("countsCalendarDays", "true", "calendar days")]
    public void ALeaveTypeWithARuleTheDemoCantMeet_IsRefused(string field, string value, string named)
    {
        // Each would refuse, hold up or miscount the demo's leave partway through: approval needs a
        // document it never uploads; staff lack the service, marriage or ID; a confidential type
        // can't be approved by the demo's managers; calendar days overdraw its balances.
        var type = LeaveType("null", isPaid: true);
        type[field] = JsonNode.Parse(value);

        Preflight.LeaveTypeProblem("VL", type).Should().StartWith("The existing VL leave type").And.Contain(named);
    }

    [Fact]
    public void ALeaveTypeWithEveryRuleOff_IsAccepted()
    {
        var type = LeaveType("null", isPaid: true);
        type["requiresDocument"] = false;
        type["requiresMarried"] = false;
        type["requiresSoloParentId"] = false;
        type["isConfidential"] = false;
        type["countsCalendarDays"] = false;
        type["minServiceMonths"] = 0; // no minimum

        Preflight.LeaveTypeProblem("VL", type).Should().BeNull();
    }

    [Fact]
    public void AnAccruedLeaveType_IsAccepted()
    {
        var type = LeaveType("null", isPaid: true);
        type["entitlementKind"] = "Accrued";

        Preflight.LeaveTypeProblem("VL", type).Should().BeNull();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(5, false)]
    public void OnlyASingleCompanySite_IsAccepted(int companies, bool accepted)
    {
        var problem = Preflight.CompanyProblem(companies);

        if (accepted) problem.Should().BeNull();
        else problem.Should().NotBeNull();
        if (companies > 1) problem.Should().Contain("single-company site");
    }

    [Theory]
    [InlineData("katherine.garcia@bayanihantrading.example", true)]
    [InlineData("Someone@BayanihanTrading.Example", true)]
    [InlineData("owner@bayanihantrading.example.com", false)]
    [InlineData("bayanihantrading.example@elsewhere.test", false)]
    [InlineData("admin@peoplecore.local", false)]
    public void ADemoEmail_IsOneOnTheDemoDomain(string email, bool isDemo)
    {
        Preflight.IsDemoEmail(email).Should().Be(isDemo);
    }
}
