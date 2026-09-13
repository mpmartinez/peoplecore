using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Every signed-in account has a profile - first name, last name, email - whether or not an
/// employee record stands behind it. It is always the account the bearer token names. The names
/// are the user's to edit; the email is the sign-in username and is not.
/// </summary>
public class ProfileControllerTests
{
    private const string UserId = "3f2b8c1e-7a4d-4e9b-9c6f-1d2e3f4a5b6c";
    private static readonly Guid EmployeeId = Guid.Parse("9c4e1a7b-3d2f-4b8a-8e6c-0f5d2a9b7c44");

    private readonly ApplicationUser _user = new() { Id = UserId, Email = "ana@company.test" };
    private readonly Mock<UserManager<ApplicationUser>> _users;
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly ProfileController _sut;

    public ProfileControllerTests()
    {
        _users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _users.Setup(u => u.FindByIdAsync(UserId)).ReturnsAsync(_user);
        _users.Setup(u => u.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);

        _sut = new ProfileController(_users.Object, _employees.Object);
        SignInAs(UserId);
    }

    private void SignInAs(string? userId)
    {
        var claims = userId is null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) }
        };
    }

    private void LinkedToEmployee(string firstName, string lastName)
    {
        _user.EmployeeId = EmployeeId;
        _employees.Setup(r => r.GetByIdAsync(EmployeeId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new Employee { FirstName = firstName, LastName = lastName });
    }

    private static UserProfileDto ProfileOf(ActionResult<UserProfileDto> result) =>
        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<UserProfileDto>().Subject;

    private static string? DetailOf(ActionResult<UserProfileDto> result) =>
        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<ProblemDetails>().Which.Detail;

    [Fact]
    public void TheWholeController_RequiresASignedInCaller()
    {
        typeof(ProfileController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }

    [Fact]
    public async Task TheProfile_IsTheTokensOwnAccount()
    {
        _user.FirstName = "Ana";
        _user.LastName = "Reyes";

        var profile = ProfileOf(await _sut.Get(CancellationToken.None));

        profile.Should().Be(new UserProfileDto("Ana", "Reyes", "ana@company.test"));
    }

    [Fact]
    public async Task AnAccountWithoutNamesOrEmployee_HasAProfile_WithBlankNames()
    {
        var profile = ProfileOf(await _sut.Get(CancellationToken.None));

        profile.Should().Be(new UserProfileDto(null, null, "ana@company.test"));
        _employees.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnAccountThatNeverSavedNames_BorrowsThemFromItsEmployeeRecord()
    {
        LinkedToEmployee("Ana", "Reyes");

        var profile = ProfileOf(await _sut.Get(CancellationToken.None));

        profile.Should().Be(new UserProfileDto("Ana", "Reyes", "ana@company.test"));
    }

    [Fact]
    public async Task NamesTheUserSaved_WinOverTheEmployeeRecord()
    {
        LinkedToEmployee("Ana", "Reyes");
        _user.FirstName = "Annie";
        _user.LastName = "Reyes-Cruz";

        var profile = ProfileOf(await _sut.Get(CancellationToken.None));

        profile.Should().Be(new UserProfileDto("Annie", "Reyes-Cruz", "ana@company.test"));
        _employees.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SavingNames_StoresThemTrimmedOnTheAccount_AndReturnsTheProfile()
    {
        var result = await _sut.Update(new UpdateProfileRequest("  Ana ", " Reyes  "), CancellationToken.None);

        ProfileOf(result).Should().Be(new UserProfileDto("Ana", "Reyes", "ana@company.test"));
        _user.FirstName.Should().Be("Ana");
        _user.LastName.Should().Be("Reyes");
        _users.Verify(u => u.UpdateAsync(_user), Times.Once);
    }

    [Fact]
    public async Task SavingNames_NeverChangesTheEmail()
    {
        await _sut.Update(new UpdateProfileRequest("Ana", "Reyes"), CancellationToken.None);

        _user.Email.Should().Be("ana@company.test");
        _users.Verify(u => u.SetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        _users.Verify(u => u.SetUserNameAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(null, "Reyes", "Enter your first name.")]
    [InlineData("   ", "Reyes", "Enter your first name.")]
    [InlineData("Ana", "", "Enter your last name.")]
    [InlineData("Ana", null, "Enter your last name.")]
    public async Task AMissingName_IsRejected_WithoutSaving(string? first, string? last, string message)
    {
        var result = await _sut.Update(new UpdateProfileRequest(first, last), CancellationToken.None);

        DetailOf(result).Should().Be(message);
        _users.Verify(u => u.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task ANameLongerThanTheColumn_IsRejected_WithoutSaving()
    {
        var tooLong = new string('a', ApplicationUser.NameMaxLength + 1);

        var result = await _sut.Update(new UpdateProfileRequest(tooLong, "Reyes"), CancellationToken.None);

        DetailOf(result).Should().Be($"First name must be {ApplicationUser.NameMaxLength} characters or fewer.");
        _users.Verify(u => u.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task AFailedSave_ReportsIdentitysReason()
    {
        _users.Setup(u => u.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Failed(
            new IdentityError { Code = "ConcurrencyFailure", Description = "Optimistic concurrency failure, object has been modified." }));

        var result = await _sut.Update(new UpdateProfileRequest("Ana", "Reyes"), CancellationToken.None);

        DetailOf(result).Should().Be("Optimistic concurrency failure, object has been modified.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-user-since-deleted")]
    public async Task ATokenWhoseAccountCannotBeFound_IsUnauthorized(string? userId)
    {
        SignInAs(userId);

        (await _sut.Get(CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
        (await _sut.Update(new UpdateProfileRequest("Ana", "Reyes"), CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
        _users.Verify(u => u.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }
}
