using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.ViewModels;

public sealed class ProfileViewModel
{
    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Phone, StringLength(30)]
    public string? PhoneNumber { get; set; }
    [StringLength(300)]
    public string? Address { get; set; }
    public DateTime RegisteredAtUtc { get; set; }
}
