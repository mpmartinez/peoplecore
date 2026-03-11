using Microsoft.AspNetCore.Identity;

namespace PeopleCore.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public Guid? EmployeeId { get; set; }
}
