using Microsoft.AspNetCore.Identity;

namespace PeopleCore.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public const int NameMaxLength = 100;

    public Guid? EmployeeId { get; set; }

    // The account holder's own names, edited on My Profile. Kept on the account rather than read
    // from the employee record because not every account has one (the seeded admin, for one),
    // and because the employee record is HR's to maintain.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
