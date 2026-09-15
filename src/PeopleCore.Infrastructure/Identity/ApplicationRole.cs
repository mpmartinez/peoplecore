using Microsoft.AspNetCore.Identity;

namespace PeopleCore.Infrastructure.Identity;

/// <summary>
/// A role: a name, a description, and the permissions it grants (rows in AspNetRoleClaims with claim
/// type "permission"). System roles are the ones the app relies on by name - Admin holds every
/// permission, Employee is the self-service baseline, Service is the attendance device - so they
/// can be neither edited nor deleted.
/// </summary>
public class ApplicationRole : IdentityRole
{
    public const int DescriptionMaxLength = 200;

    public string? Description { get; set; }

    public bool IsSystem { get; set; }
}
