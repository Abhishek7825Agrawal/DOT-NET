using System.Security.Claims;
using System.Text.RegularExpressions;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize]
public sealed class DigitalLibraryController(
    ApplicationDbContext db,
    DigitalDocumentService documents,
    ActivityLogService activityLogs) : Controller
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>
    /// Digital Library Catalogue: Lists all published digital manuscripts with reader progress.
    /// Accessible to authenticated members, staff, and administrators.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(
        string? q,
        string? category,
        CancellationToken cancellationToken = default)
    {
        var query = db.Books.AsNoTracking()
            .Include(b => b.DigitalEdition)
            .Where(b => b.IsActive && b.DigitalEdition != null && b.DigitalEdition.IsDigitalAvailable);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(b => b.Title.Contains(term)
                || b.Author.Contains(term)
                || b.Category.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(b => b.Category == category);
        }

        var booksList = await query.OrderBy(b => b.Title).ToListAsync(cancellationToken);

        // Load current user's reading progress if authenticated
        Dictionary<int, ReadingProgress> userProgress = [];
        if (User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(CurrentUserId))
        {
            var digitalIds = booksList.Select(b => b.DigitalEdition!.Id).ToList();
            userProgress = await db.ReadingProgresses.AsNoTracking()
                .Where(p => p.UserId == CurrentUserId && digitalIds.Contains(p.DigitalBookId))
                .ToDictionaryAsync(p => p.DigitalBookId, cancellationToken);
        }

        var cards = booksList.Select(b =>
        {
            userProgress.TryGetValue(b.DigitalEdition!.Id, out var progress);
            return new DigitalBookCardViewModel
            {
                BookId = b.Id,
                DigitalBookId = b.DigitalEdition.Id,
                Title = b.Title,
                Author = b.Author,
                Category = b.Category,
                Description = b.Description,
                PageCount = b.DigitalEdition.PageCount,
                FileSizeBytes = b.DigitalEdition.FileSizeBytes,
                IsDigitalAvailable = b.DigitalEdition.IsDigitalAvailable,
                LastPageRead = progress?.LastPageRead,
                PercentComplete = progress?.PercentComplete
            };
        }).ToList();

        ViewBag.Query = q;
        ViewBag.Category = category;
        ViewBag.Categories = await db.Categories.AsNoTracking().Select(c => c.Name).ToListAsync(cancellationToken);

        return View(cards);
    }

    /// <summary>
    /// Authorized PDF Streaming Endpoint with HTTP 206 Partial Content Range Request support.
    /// Enforces access rights, digital availability, and user standing before streaming bytes.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Stream(int id, CancellationToken cancellationToken)
    {
        var digital = await db.DigitalBooks.AsNoTracking()
            .Include(d => d.Book)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (digital is null || !digital.Book.IsActive || !digital.IsDigitalAvailable)
        {
            return NotFound("Digital document not found or digital access is unavailable.");
        }

        // Verify member status if current user is a Member
        if (User.IsInRole("Member"))
        {
            var member = await db.Members.AsNoTracking()
                .FirstOrDefaultAsync(m => m.UserId == CurrentUserId, cancellationToken);

            if (member is { IsActive: false })
            {
                return Forbid("Your member account has been suspended. Please contact the library desk.");
            }
        }

        var fullPath = documents.GetVerifiedDocumentPath(digital.StorageFileName);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound("The physical PDF document was not found in library storage.");
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{SanitizeFileName(digital.OriginalFileName)}\"";

        // Enable HTTP 206 Range Processing for on-demand page streaming in PDF.js
        return PhysicalFile(fullPath, "application/pdf", enableRangeProcessing: true);
    }

    /// <summary>
    /// Signature Workspace: Side-by-Side PDF.js Reader + Quill Rich-Text Personal Notes Editor.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Workspace(int id, CancellationToken cancellationToken)
    {
        var digital = await db.DigitalBooks.AsNoTracking()
            .Include(d => d.Book)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (digital is null || !digital.Book.IsActive || !digital.IsDigitalAvailable)
        {
            TempData["Error"] = "The requested digital book is not available.";
            return RedirectToAction(nameof(Index));
        }

        // Check active member standing
        if (User.IsInRole("Member"))
        {
            var member = await db.Members.AsNoTracking()
                .FirstOrDefaultAsync(m => m.UserId == CurrentUserId, cancellationToken);

            if (member is { IsActive: false })
            {
                TempData["Error"] = "Your cardholder account has been suspended.";
                return RedirectToAction("Index", "Dashboard");
            }
        }

        // Load existing notes for this book
        var notes = await db.UserNotes.AsNoTracking()
            .Where(n => n.UserId == CurrentUserId && n.BookId == digital.BookId)
            .OrderByDescending(n => n.LastModifiedUtc)
            .ToListAsync(cancellationToken);

        // Auto-provision initial note if user has no notes yet for this volume
        if (notes.Count == 0)
        {
            var defaultNote = new UserNote
            {
                UserId = CurrentUserId,
                BookId = digital.BookId,
                Title = "Reading Reflections",
                ContentDeltaJson = "{\"ops\":[{\"insert\":\"Notes for " + EscapeJsonString(digital.Book.Title) + "\\n\"}]}",
                SanitizedContentHtml = $"<p><strong>Notes for {System.Web.HttpUtility.HtmlEncode(digital.Book.Title)}</strong></p>",
                PlainTextExcerpt = $"Notes for {digital.Book.Title}",
                Version = 1,
                CreatedAtUtc = DateTime.UtcNow,
                LastModifiedUtc = DateTime.UtcNow
            };
            db.UserNotes.Add(defaultNote);
            await db.SaveChangesAsync(cancellationToken);
            notes.Add(defaultNote);
        }

        // Load user's bookmarks
        var bookmarks = await db.BookBookmarks.AsNoTracking()
            .Where(b => b.UserId == CurrentUserId && b.DigitalBookId == digital.Id)
            .OrderBy(b => b.PageNumber)
            .ToListAsync(cancellationToken);

        // Load user's annotations for current document version (prevents misalignment on PDF replacement)
        var annotations = await db.BookAnnotations.AsNoTracking()
            .Where(a => a.UserId == CurrentUserId && a.DigitalBookId == digital.Id && a.DocumentVersion == digital.DocumentVersion)
            .OrderBy(a => a.PageNumber)
            .ThenBy(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var archivedAnnotationCount = await db.BookAnnotations.AsNoTracking()
            .CountAsync(a => a.UserId == CurrentUserId && a.DigitalBookId == digital.Id && a.DocumentVersion < digital.DocumentVersion, cancellationToken);

        // Load user's last reading progress
        var progress = await db.ReadingProgresses.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == CurrentUserId && p.DigitalBookId == digital.Id, cancellationToken);

        var initialPage = progress?.LastPageRead ?? 1;
        if (initialPage < 1 || initialPage > digital.PageCount) initialPage = 1;

        var vm = new WorkspaceViewModel
        {
            BookId = digital.BookId,
            DigitalBookId = digital.Id,
            Title = digital.Book.Title,
            Author = digital.Book.Author,
            Category = digital.Book.Category,
            PageCount = digital.PageCount,
            DocumentVersion = digital.DocumentVersion,
            ArchivedAnnotationCount = archivedAnnotationCount,
            InitialPage = initialPage,
            StreamUrl = Url.Action(nameof(Stream), new { id = digital.Id }) ?? $"/DigitalLibrary/Stream/{digital.Id}",
            InitialNotes = notes.Select(n => new UserNoteItemViewModel
            {
                Id = n.Id,
                Title = n.Title,
                ContentDeltaJson = n.ContentDeltaJson,
                SanitizedContentHtml = n.SanitizedContentHtml,
                PlainTextExcerpt = n.PlainTextExcerpt,
                Version = n.Version,
                LastModifiedUtc = n.LastModifiedUtc
            }).ToList(),
            InitialBookmarks = bookmarks.Select(b => new BookmarkItemViewModel
            {
                Id = b.Id,
                PageNumber = b.PageNumber,
                Label = b.Label,
                CreatedAtUtc = b.CreatedAtUtc
            }).ToList(),
            InitialAnnotations = annotations.Select(a => new AnnotationItemViewModel
            {
                Id = a.Id,
                PageNumber = a.PageNumber,
                SelectedText = a.SelectedText,
                HighlightColor = a.HighlightColor,
                TextAnchorJson = a.TextAnchorJson,
                PositionJson = a.PositionJson,
                LinkedNoteId = a.LinkedNoteId,
                DocumentVersion = a.DocumentVersion,
                CreatedAtUtc = a.CreatedAtUtc
            }).ToList()
        };

        await activityLogs.RecordAsync(CurrentUserId, "DigitalBookOpened", "DigitalReading",
            $"You opened '{digital.Book.Title}'.", entityType: "DigitalBook", entityReference: digital.Id.ToString(),
            cancellationToken: cancellationToken);
        return View(vm);
    }

    #region Notes API

    [HttpPost]
    public async Task<IActionResult> SaveNote([FromBody] SaveNoteRequest request, CancellationToken cancellationToken)
    {
        if (request is null || (request.NoteId <= 0 && request.BookId <= 0))
        {
            return BadRequest(new SaveNoteResponse { Success = false, ErrorMessage = "Invalid note request payload." });
        }

        var sanitizedHtml = SanitizeHtml(request.ContentHtml);
        var plainText = string.IsNullOrWhiteSpace(request.PlainTextExcerpt)
            ? StripHtml(sanitizedHtml)
            : request.PlainTextExcerpt.Trim();

        if (plainText.Length > 400)
        {
            plainText = plainText[..400];
        }

        var deltaJson = string.IsNullOrWhiteSpace(request.ContentDeltaJson)
            ? "{\"ops\":[]}"
            : request.ContentDeltaJson;

        UserNote note;
        if (request.NoteId <= 0)
        {
            note = new UserNote
            {
                UserId = CurrentUserId,
                BookId = request.BookId,
                Title = string.IsNullOrWhiteSpace(request.Title) ? "Reading Notes" : request.Title.Trim(),
                ContentDeltaJson = deltaJson,
                SanitizedContentHtml = sanitizedHtml,
                PlainTextExcerpt = plainText,
                Version = 1,
                CreatedAtUtc = DateTime.UtcNow,
                LastModifiedUtc = DateTime.UtcNow
            };
            db.UserNotes.Add(note);
        }
        else
        {
            var existing = await db.UserNotes.FirstOrDefaultAsync(n => n.Id == request.NoteId, cancellationToken);
            if (existing is null || existing.UserId != CurrentUserId)
            {
                return Forbid();
            }

            note = existing;
            note.Title = string.IsNullOrWhiteSpace(request.Title) ? note.Title : request.Title.Trim();
            note.ContentDeltaJson = deltaJson;
            note.SanitizedContentHtml = sanitizedHtml;
            note.PlainTextExcerpt = plainText;
            note.Version++;
            note.LastModifiedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        await activityLogs.RecordAsync(CurrentUserId, request.NoteId <= 0 ? "NoteCreated" : "NoteEdited", "DigitalReading",
            request.NoteId <= 0 ? "You created reading notes." : "You edited reading notes.",
            entityType: "Note", entityReference: note.Id.ToString(),
            safeContext: new { note.BookId, note.Version }, cancellationToken: cancellationToken);

        return Ok(new SaveNoteResponse
        {
            Success = true,
            NoteId = note.Id,
            NewVersion = note.Version,
            LastModifiedUtc = note.LastModifiedUtc
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateNote(int bookId, string? title, CancellationToken cancellationToken)
    {
        var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken);
        if (book is null) return NotFound("Book not found.");

        var note = new UserNote
        {
            UserId = CurrentUserId,
            BookId = bookId,
            Title = string.IsNullOrWhiteSpace(title) ? "New Note" : title.Trim(),
            ContentDeltaJson = "{\"ops\":[]}",
            SanitizedContentHtml = "<p></p>",
            PlainTextExcerpt = "",
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };

        db.UserNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "NoteCreated", "DigitalReading",
            "You created reading notes.", entityType: "Note", entityReference: note.Id.ToString(),
            safeContext: new { note.BookId }, cancellationToken: cancellationToken);

        return Ok(new UserNoteItemViewModel
        {
            Id = note.Id,
            Title = note.Title,
            ContentDeltaJson = note.ContentDeltaJson,
            SanitizedContentHtml = note.SanitizedContentHtml,
            PlainTextExcerpt = note.PlainTextExcerpt,
            Version = note.Version,
            LastModifiedUtc = note.LastModifiedUtc
        });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteNote(int id, CancellationToken cancellationToken)
    {
        var note = await db.UserNotes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (note is null || note.UserId != CurrentUserId)
        {
            return Forbid();
        }

        var bookId = note.BookId;
        db.UserNotes.Remove(note);
        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "NoteDeleted", "DigitalReading",
            "You deleted reading notes.", entityType: "Note", entityReference: id.ToString(),
            safeContext: new { BookId = bookId }, cancellationToken: cancellationToken);

        return Ok(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> ExportNote(int id, CancellationToken cancellationToken)
    {
        var note = await db.UserNotes.AsNoTracking()
            .Include(n => n.Book)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (note is null || note.UserId != CurrentUserId)
        {
            return Forbid();
        }

        return View(note);
    }

    #endregion

    #region Annotations API

    [HttpPost]
    public async Task<IActionResult> SaveAnnotation([FromBody] SaveAnnotationRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.DigitalBookId <= 0 || request.PageNumber <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid annotation payload." });
        }

        var digital = await db.DigitalBooks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DigitalBookId, cancellationToken);
        if (digital is null || !digital.IsDigitalAvailable)
        {
            return NotFound(new { success = false, message = "Digital book not available." });
        }

        BookAnnotation annotation;
        if (request.AnnotationId.HasValue && request.AnnotationId.Value > 0)
        {
            var existing = await db.BookAnnotations.FirstOrDefaultAsync(a => a.Id == request.AnnotationId.Value, cancellationToken);
            if (existing is null || existing.UserId != CurrentUserId)
            {
                return Forbid();
            }

            annotation = existing;
            annotation.HighlightColor = request.HighlightColor ?? "yellow";
            annotation.PositionJson = request.PositionJson ?? annotation.PositionJson;
            annotation.TextAnchorJson = request.TextAnchorJson ?? annotation.TextAnchorJson;
            annotation.LinkedNoteId = request.LinkedNoteId;
            annotation.LastModifiedUtc = DateTime.UtcNow;
        }
        else
        {
            annotation = new BookAnnotation
            {
                UserId = CurrentUserId,
                DigitalBookId = request.DigitalBookId,
                DocumentVersion = digital.DocumentVersion,
                PageNumber = request.PageNumber,
                SelectedText = request.SelectedText.Trim(),
                HighlightColor = request.HighlightColor ?? "yellow",
                TextAnchorJson = request.TextAnchorJson ?? "{}",
                PositionJson = request.PositionJson ?? "{}",
                LinkedNoteId = request.LinkedNoteId,
                CreatedAtUtc = DateTime.UtcNow,
                LastModifiedUtc = DateTime.UtcNow
            };
            db.BookAnnotations.Add(annotation);
        }

        await db.SaveChangesAsync(cancellationToken);

        await activityLogs.RecordAsync(CurrentUserId,
            request.AnnotationId.HasValue ? "HighlightEdited" : "HighlightAdded", "DigitalReading",
            request.AnnotationId.HasValue ? "You edited a highlight." : $"You added a highlight on page {annotation.PageNumber}.",
            entityType: "Annotation", entityReference: annotation.Id.ToString(),
            safeContext: new { annotation.DigitalBookId, annotation.PageNumber, annotation.DocumentVersion },
            cancellationToken: cancellationToken);

        return Ok(new
        {
            success = true,
            annotationId = annotation.Id,
            pageNumber = annotation.PageNumber,
            highlightColor = annotation.HighlightColor
        });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAnnotation(int id, CancellationToken cancellationToken)
    {
        var annotation = await db.BookAnnotations.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (annotation is null || annotation.UserId != CurrentUserId)
        {
            return Forbid();
        }

        var pageNumber = annotation.PageNumber;
        db.BookAnnotations.Remove(annotation);
        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "HighlightDeleted", "DigitalReading",
            $"You removed a highlight from page {pageNumber}.", entityType: "Annotation", entityReference: id.ToString(),
            cancellationToken: cancellationToken);

        return Ok(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> GetAnnotations(int id, int? version, CancellationToken cancellationToken)
    {
        var digital = await db.DigitalBooks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (digital is null) return NotFound();

        var targetVersion = version ?? digital.DocumentVersion;

        var list = await db.BookAnnotations.AsNoTracking()
            .Where(a => a.DigitalBookId == id && a.UserId == CurrentUserId && a.DocumentVersion == targetVersion)
            .OrderBy(a => a.PageNumber)
            .ThenBy(a => a.CreatedAtUtc)
            .Select(a => new AnnotationItemViewModel
            {
                Id = a.Id,
                PageNumber = a.PageNumber,
                SelectedText = a.SelectedText,
                HighlightColor = a.HighlightColor,
                TextAnchorJson = a.TextAnchorJson,
                PositionJson = a.PositionJson,
                LinkedNoteId = a.LinkedNoteId,
                DocumentVersion = a.DocumentVersion,
                CreatedAtUtc = a.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    #endregion

    #region Bookmarks API

    [HttpPost]
    public async Task<IActionResult> ToggleBookmark([FromBody] ToggleBookmarkRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.DigitalBookId <= 0 || request.PageNumber <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid bookmark request." });
        }

        var existing = await db.BookBookmarks
            .FirstOrDefaultAsync(b => b.DigitalBookId == request.DigitalBookId && b.UserId == CurrentUserId && b.PageNumber == request.PageNumber, cancellationToken);

        if (existing is not null)
        {
            var pageNumber = existing.PageNumber;
            db.BookBookmarks.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            await activityLogs.RecordAsync(CurrentUserId, "BookmarkRemoved", "DigitalReading",
                $"You removed a bookmark from page {pageNumber}.", entityType: "DigitalBook",
                entityReference: request.DigitalBookId.ToString(), cancellationToken: cancellationToken);
            return Ok(new { success = true, isBookmarked = false, pageNumber = request.PageNumber });
        }

        var bookmark = new BookBookmark
        {
            UserId = CurrentUserId,
            DigitalBookId = request.DigitalBookId,
            PageNumber = request.PageNumber,
            Label = string.IsNullOrWhiteSpace(request.Label) ? $"Page {request.PageNumber}" : request.Label.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.BookBookmarks.Add(bookmark);
        await db.SaveChangesAsync(cancellationToken);
        await activityLogs.RecordAsync(CurrentUserId, "BookmarkAdded", "DigitalReading",
            $"You bookmarked page {bookmark.PageNumber}.", entityType: "DigitalBook",
            entityReference: bookmark.DigitalBookId.ToString(), cancellationToken: cancellationToken);

        return Ok(new { success = true, isBookmarked = true, pageNumber = request.PageNumber, bookmarkId = bookmark.Id, label = bookmark.Label });
    }

    #endregion

    #region Reading Progress API

    [HttpPost]
    public async Task<IActionResult> UpdateProgress([FromBody] UpdateProgressRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.DigitalBookId <= 0 || request.LastPageRead <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid progress payload." });
        }

        var digital = await db.DigitalBooks.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DigitalBookId, cancellationToken);
        if (digital is null) return NotFound();

        var totalPages = Math.Max(1, digital.PageCount);
        var page = Math.Clamp(request.LastPageRead, 1, totalPages);
        var pct = Math.Clamp(Math.Round((decimal)page / totalPages * 100m, 2), 0m, 100m);

        var progress = await db.ReadingProgresses
            .FirstOrDefaultAsync(p => p.DigitalBookId == request.DigitalBookId && p.UserId == CurrentUserId, cancellationToken);

        var previousPage = progress?.LastPageRead ?? 0;
        if (progress is null)
        {
            progress = new ReadingProgress
            {
                UserId = CurrentUserId,
                DigitalBookId = request.DigitalBookId,
                LastPageRead = page,
                TotalPages = totalPages,
                PercentComplete = pct,
                LastReadAtUtc = DateTime.UtcNow
            };
            db.ReadingProgresses.Add(progress);
        }
        else
        {
            progress.LastPageRead = page;
            progress.TotalPages = totalPages;
            progress.PercentComplete = pct;
            progress.LastReadAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (page > previousPage && (page % 5 == 0 || page == totalPages))
            await activityLogs.RecordAsync(CurrentUserId, "ReadingProgressUpdated", "DigitalReading",
                $"You read up to page {page}.", entityType: "DigitalBook", entityReference: request.DigitalBookId.ToString(),
                safeContext: new { Page = page, TotalPages = totalPages }, cancellationToken: cancellationToken);

        return Ok(new { success = true, page = page, total = totalPages, percent = pct });
    }

    #endregion

    #region Security & Sanitization Helpers

    public static string SanitizeHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Strip script, style, iframe, object, embed, form tags
        var cleaned = Regex.Replace(input, @"<(script|style|iframe|object|embed|form|link|meta)[^>]*?>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        cleaned = Regex.Replace(cleaned, @"<(script|style|iframe|object|embed|form|link|meta)[^>]*?>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Strip inline javascript/event handlers (on*) and javascript: URIs
        cleaned = Regex.Replace(cleaned, @"\s(on[a-z]+)=[""'][^""']*[""']", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"href=[""']\s*javascript:[^""']*[""']", "href=\"#\"", RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }

    public static string StripHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return Regex.Replace(input, "<.*?>", string.Empty).Trim();
    }

    private static string SanitizeFileName(string fileName)
    {
        var clean = Path.GetFileName(fileName);
        return Regex.Replace(clean, @"[^a-zA-Z0-9_\-\.]", "_");
    }

    private static string EscapeJsonString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    #endregion
}
