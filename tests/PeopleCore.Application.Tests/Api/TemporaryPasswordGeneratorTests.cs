using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using PeopleCore.API.Accounts;
using PeopleCore.API.Extensions;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// A temporary password is read aloud or copied by hand from one person to another, so it avoids
/// characters that look alike - and it must always satisfy the password policy, or creating an
/// account would fail at random.
/// </summary>
public class TemporaryPasswordGeneratorTests
{
    private const int Samples = 500;

    [Fact]
    public void APassword_IsSixteenCharacters()
    {
        TemporaryPasswordGenerator.Generate().Should().HaveLength(16);
    }

    [Fact]
    public void EveryPassword_MixesCaseAndDigits_WithoutLookAlikes()
    {
        for (var i = 0; i < Samples; i++)
        {
            var password = TemporaryPasswordGenerator.Generate();

            password.Should().Match(p => p.Any(char.IsUpper) && p.Any(char.IsLower) && p.Any(char.IsDigit), password);
            password.Should().NotContainAny(["0", "O", "1", "l", "I"]);
            password.All(char.IsLetterOrDigit).Should().BeTrue(password);
        }
    }

    [Fact]
    public void Passwords_AreNotRepeated()
    {
        Enumerable.Range(0, Samples).Select(_ => TemporaryPasswordGenerator.Generate())
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task EveryPassword_SatisfiesTheAppsOwnPasswordPolicy()
    {
        var options = new IdentityOptions();
        ServiceExtensions.ConfigureIdentityOptions(options);
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), Options.Create(options), null!, null!, null!, null!, null!, null!, null!);
        var validator = new PasswordValidator<ApplicationUser>();

        for (var i = 0; i < Samples; i++)
        {
            var password = TemporaryPasswordGenerator.Generate();
            var result = await validator.ValidateAsync(users.Object, new ApplicationUser(), password);
            result.Succeeded.Should().BeTrue(password);
        }
    }
}
