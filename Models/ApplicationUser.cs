using Microsoft.AspNetCore.Identity;

namespace LibraryManagementSystem.Models;

public sealed class ApplicationUser : IdentityUser
{
    public Member? MemberProfile { get; set; }
}
