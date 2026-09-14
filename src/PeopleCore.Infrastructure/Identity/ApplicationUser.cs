using Microsoft.AspNetCore.Identity;

namespace PeopleCore.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public const int NameMaxLength = 100;

    public Guid? EmployeeId { get; set; }

    // A deactivated account cannot sign in, and the tokens it already holds stop working on their
    // next request. Deliberately not Identity's lockout: failed sign-ins write LockoutEnd, and
    // sharing that field would let a lockout expiring quietly reactivate the account.
    public bool IsActive { get; set; } = true;

    // Set when Admin or HR issues a temporary password; cleared once the user chooses their own.
    public bool MustChangePassword { get; set; }

    // The account holder's own names, edited on My Profile. Kept on the account rather than read
    // from the employee record because not every account has one (the seeded admin, for one),
    // and because the employee record is HR's to maintain.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
