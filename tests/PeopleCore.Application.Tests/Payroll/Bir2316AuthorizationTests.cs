using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir2316AuthorizationTests
{
    private static MethodInfo Action(string name) =>
        typeof(Bir2316Controller).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Bir2316Controller has no public action '{name}'.");

    [Fact]
    public void Generate_TakesBir2316ManualInputsNotBir2316Dto()
    {
        // The type is the enforcement: Bir2316ManualInputs has no field capable of carrying a
        // derived figure (no basic salary, no present-employer withholding), so this endpoint
        // cannot be made to render caller-supplied money even by mistake. Widening this parameter
        // to Bir2316Dto - or wrapping one inside a new request type - would reopen exactly the
        // hole PayZen's equivalent endpoint had, so this test pins the parameter's exact type
        // rather than merely checking a field count, which a wrapper type could dodge.
        var parameters = Action("Generate").GetParameters();

        var body = parameters.Should().ContainSingle(p => p.ParameterType == typeof(Bir2316ManualInputs)
            || p.ParameterType == typeof(Bir2316Dto))
            .Subject;

        body.ParameterType.Should().Be(typeof(Bir2316ManualInputs),
            "Generate must recompute every derived figure from payroll, never accept one in the request body");
    }

    [Fact]
    public void ClassLevelAuthorize_AdmitsOnlyPayrollRoles()
    {
        // Bir2316Controller, unlike ReportsController, has no self-service action - an employee's
        // own 2316 is handed over by HR, not self-served - so the class-level attribute is the
        // one thing guarding every action here. Nothing else would notice it being loosened or
        // dropped from a single action, or the whole class.
        var attribute = typeof(Bir2316Controller).GetCustomAttribute<AuthorizeAttribute>();

        attribute.Should().NotBeNull("Bir2316Controller must be role-restricted at class level");
        attribute!.Roles!.Split(',', StringSplitOptions.TrimEntries)
            .Should().BeEquivalentTo(["Admin", "HRManager", "PayrollService"]);
    }

    [Theory]
    [InlineData("GetYears")]
    [InlineData("GetPreview")]
    [InlineData("Generate")]
    [InlineData("GenerateAll")]
    public void EveryAction_CarriesNoActionLevelAuthorizeOfItsOwn(string actionName)
    {
        // The class-level attribute is the only guard - no action here should carry its own
        // [Authorize], which would only ever narrow it accidentally (e.g. to a single role) and
        // make the class-level attribute misleading about what actually protects each action.
        var attribute = Action(actionName).GetCustomAttribute<AuthorizeAttribute>();

        attribute.Should().BeNull($"{actionName} should rely on the class-level [Authorize], not its own");
    }
}
