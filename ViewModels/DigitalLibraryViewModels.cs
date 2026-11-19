using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LibraryManagementSystem.ViewModels;

public sealed class ManageDigitalBookViewModel
{
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Isbn { get; set; }
    public int TotalPhysicalCopies { get; set; }
    public int AvailablePhysicalCopies { get; set; }

    public bool HasDigitalEdition { get; set; }
    public int? DigitalBookId { get; set; }
    public string? OriginalFileName { get; set; }
    public long FileSizeBytes { get; set; }
    public int PageCount { get; set; }
    public int DocumentVersion { get; set; } = 1;
    public bool IsDigitalAvailable { get; set; } = true;
    public DateTime? UploadedAtUtc { get; set; }
    public string? ChecksumSha256 { get; set; }

    [Display(Name = "Digital Document (PDF)")]
    public IFormFile? PdfFile { get; set; }
}

public sealed class DigitalBookCardViewModel
{
    public int BookId { get; set; }
    public int DigitalBookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PageCount { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsDigitalAvailable { get; set; }
    public int? LastPageRead { get; set; }
    public decimal? PercentComplete { get; set; }
    public bool HasStartedReading => LastPageRead.HasValue && LastPageRead.Value > 1;
}

public sealed class WorkspaceViewModel
{
    public int BookId { get; set; }
    public int DigitalBookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public int DocumentVersion { get; set; }
    public int ArchivedAnnotationCount { get; set; }
    public int InitialPage { get; set; } = 1;
    public string StreamUrl { get; set; } = string.Empty;
    public List<UserNoteItemViewModel> InitialNotes { get; set; } = [];
    public List<BookmarkItemViewModel> InitialBookmarks { get; set; } = [];
    public List<AnnotationItemViewModel> InitialAnnotations { get; set; } = [];
}

public sealed class UserNoteItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentDeltaJson { get; set; } = "{\"ops\":[]}";
    public string? SanitizedContentHtml { get; set; }
    public string? PlainTextExcerpt { get; set; }
    public int Version { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

public sealed class BookmarkItemViewModel
{
    public int Id { get; set; }
    public int PageNumber { get; set; }
    public string? Label { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AnnotationItemViewModel
{
    public int Id { get; set; }
    public int PageNumber { get; set; }
    public string SelectedText { get; set; } = string.Empty;
    public string HighlightColor { get; set; } = "yellow";
    public string TextAnchorJson { get; set; } = "{}";
    public string PositionJson { get; set; } = "{}";
    public int? LinkedNoteId { get; set; }
    public int DocumentVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class SaveNoteRequest
{
    public int NoteId { get; set; }
    public int BookId { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = "Reading Notes";
    public string ContentDeltaJson { get; set; } = "{\"ops\":[]}";
    public string? ContentHtml { get; set; }
    public string? PlainTextExcerpt { get; set; }
    public int ClientVersion { get; set; }
}

public sealed class SaveNoteResponse
{
    public bool Success { get; set; }
    public int NoteId { get; set; }
    public int NewVersion { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class SaveAnnotationRequest
{
    public int? AnnotationId { get; set; }
    public int DigitalBookId { get; set; }
    public int DocumentVersion { get; set; } = 1;
    public int PageNumber { get; set; }
    [Required, StringLength(2000)]
    public string SelectedText { get; set; } = string.Empty;
    [Required, StringLength(30)]
    public string HighlightColor { get; set; } = "yellow";
    public string TextAnchorJson { get; set; } = "{}";
    public string PositionJson { get; set; } = "{}";
    public int? LinkedNoteId { get; set; }
}

public sealed class ToggleBookmarkRequest
{
    public int DigitalBookId { get; set; }
    public int PageNumber { get; set; }
    public string? Label { get; set; }
}

public sealed class UpdateProgressRequest
{
    public int DigitalBookId { get; set; }
    public int LastPageRead { get; set; }
}
