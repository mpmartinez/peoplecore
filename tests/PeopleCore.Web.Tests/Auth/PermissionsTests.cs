using System.Reflection;
using FluentAssertions;
using PeopleCore.Web.Auth;

namespace PeopleCore.Web.Tests.Auth;

/// <summary>
/// AllKeys is typed out by hand alongside the consts it lists, so a key added to one and
/// forgotten in the other would compile fine and simply never be checked against. Reflection
/// catches that: every permission-shaped const must appear in AllKeys, in the same order.
/// </summary>
public class PermissionsTests
{
    [Fact]
    public void AllKeys_ListsEveryPermissionConstant_InDeclarationOrder()
    {
        var declared = typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(Permissions.ClaimType))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => v.Contains('.'))
            .ToList();

        declared.Should().Equal(Permissions.AllKeys);
    }
}
