using System.ComponentModel.DataAnnotations;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Http;

namespace LibraryManagementSystem.ViewModels;

public sealed class StaffRegisterCustomerViewModel
{
    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Full Name must be between 2 and 120 characters.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Please provide a valid email address.")]
    [StringLength(256)]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Please enter a valid phone number format.")]
    [StringLength(30)]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [StringLength(300, ErrorMessage = "Address cannot exceed 300 characters.")]
    [Display(Name = "Residential Address")]
    public string? Address { get; set; }

    [StringLength(500)]
    [Display(Name = "Staff Notes / Desk Remarks")]
    public string? Remarks { get; set; }
}

public sealed class StaffRegistrationSuccessViewModel
{
    public int MemberId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string ActivationToken { get; set; } = string.Empty;
    public string ActivationUrl { get; set; } = string.Empty;
    public DateTime RegisteredAtUtc { get; set; }
}

public sealed class ExcelUploadViewModel
{
    [Required(ErrorMessage = "Please select an Excel (.xlsx) file to upload.")]
    [Display(Name = "Excel Spreadsheet (.xlsx)")]
    public IFormFile? ExcelFile { get; set; }
}

public sealed class ExcelImportPreviewViewModel
{
    public int BatchId { get; set; }
    public string BatchReference { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int TotalRows { get; set; }
    public int ValidCount { get; set; }
    public int InvalidCount { get; set; }
    public int DuplicateCount { get; set; }
    public List<CustomerImportRowItemViewModel> Rows { get; set; } = [];
}

public sealed class CustomerImportRowItemViewModel
{
    public int RowNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public ImportRowStatus Status { get; set; }
    public string? ValidationMessage { get; set; }
}

public sealed class CustomerImportSummaryViewModel
{
    public int BatchId { get; set; }
    public string BatchReference { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public int TotalProcessed { get; set; }
    public int SuccessfullyImported { get; set; }
    public int InvalidSkipped { get; set; }
    public int DuplicateSkipped { get; set; }
    public DateTime CommittedAtUtc { get; set; }
    public List<ImportedRowSummaryItem> ImportedMembers { get; set; } = [];
}

public sealed class ImportedRowSummaryItem
{
    public int RowNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ActivationUrl { get; set; } = string.Empty;
}

public sealed class SetupPasswordViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Create Password")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("NewPassword", ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
