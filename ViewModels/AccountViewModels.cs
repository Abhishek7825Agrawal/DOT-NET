using System.ComponentModel.DataAnnotations;
using LibraryManagementSystem.Models;

namespace LibraryManagementSystem.ViewModels;

public sealed class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;
    [Phone, StringLength(30)]
    public string? PhoneNumber { get; set; }
    [StringLength(300)]
    public string? Address { get; set; }
    [Required, DataType(DataType.Password), MinLength(10)]
    public string Password { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class BookFormViewModel
{
    public int Id { get; set; }
    public string? RowVersion { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [Required, StringLength(160)]
    public string Author { get; set; } = string.Empty;
    [StringLength(32), ValidIsbn]
    public string? Isbn { get; set; }
    [Required, StringLength(80)]
    public string Category { get; set; } = string.Empty;
    [StringLength(160)]
    public string? Publisher { get; set; }
    [Range(1450, 2100)]
    public int? PublicationYear { get; set; }
    [StringLength(2000)]
    public string? Description { get; set; }
    [Range(0, 100000)]
    public int TotalQuantity { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ValidIsbnAttribute : ValidationAttribute
{
    public ValidIsbnAttribute() : base("Enter a valid ISBN-10 or ISBN-13.")
    {
    }

    public override bool IsValid(object? value) =>
        value is null || value is string text && IsbnRules.IsValid(text);
}

public sealed class IssueBookViewModel
{
    [Range(1, int.MaxValue)]
    public int MemberId { get; set; }
    [Range(1, int.MaxValue)]
    public int BookId { get; set; }
}

public sealed class FinePaymentViewModel
{
    [Range(0.01, 1000000)]
    public decimal Amount { get; set; }

    [EnumDataType(typeof(FinePaymentMethod))]
    public FinePaymentMethod PaymentMethod { get; set; }

    [StringLength(120)]
    public string? ReferenceNumber { get; set; }
}

public sealed class CategoryFormViewModel
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Description { get; set; }
}

public sealed class ReturnBookViewModel
{
    [Required]
    public int IssueId { get; set; }

    [StringLength(300)]
    public string? Remarks { get; set; }
}

public sealed class MarkLostViewModel
{
    [Required]
    public int IssueId { get; set; }

    [StringLength(300)]
    public string? Remarks { get; set; }
}

public sealed class LibrarySettingsViewModel
{
    [Range(1, 365)]
    [Display(Name = "Standard Loan Duration (Days)")]
    public int LoanPeriodDays { get; set; } = 14;

    [Range(0, 10000)]
    [Display(Name = "Daily Overdue Fine Rate (₹)")]
    public decimal FineRatePerDay { get; set; } = 5.00m;

    [Range(1, 50)]
    [Display(Name = "Maximum Active Loans Per Member")]
    public int MaxBooksPerMember { get; set; } = 5;

    [Range(0, 50000)]
    [Display(Name = "Lost Book Standard Penalty Fee (₹)")]
    public decimal LostBookPenaltyFee { get; set; } = 250.00m;

    [Range(0, 30)]
    [Display(Name = "Overdue Grace Period (Days)")]
    public int GracePeriodDays { get; set; } = 0;

    [Required, StringLength(80)]
    [Display(Name = "Library Timezone Identifier")]
    public string TimeZoneId { get; set; } = "Asia/Kolkata";
}

public sealed class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(10)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class AdminProfileViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [Phone, StringLength(30)]
    public string? PhoneNumber { get; set; }
}
