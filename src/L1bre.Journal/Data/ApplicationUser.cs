using Microsoft.AspNetCore.Identity;

namespace L1bre.Journal.Data;

public class ApplicationUser : IdentityUser
{
    /// <summary>Name shown in the app (e.g. to a coach). The login stays the email.</summary>
    [PersonalData]
    public string DisplayName { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
