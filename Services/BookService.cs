using System.Data;
using System.Data.Common;
using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class BookService(ApplicationDbContext db)
{
    public IQueryable<Book> Search(string? term, string? category, bool includeInactive = false)
    {
        var query = db.Books.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(book => book.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var value = term.Trim();
            query = query.Where(book => book.Title.Contains(value) || book.Author.Contains(value)
                || (book.Isbn != null && book.Isbn.Contains(value)) || book.Category.Contains(value));
        }
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(book => book.Category == category);
        return query.OrderBy(book => book.Title);
    }

    public Task<Book?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        db.Books.AsNoTracking().Include(b => b.DigitalEdition).FirstOrDefaultAsync(book => book.Id == id, cancellationToken);

    public async Task<(bool Success, string? Error)> CreateAsync(Book book, CancellationToken cancellationToken = default)
    {
        Normalize(book);
        if (book.TotalQuantity < 0) return (false, "Total quantity cannot be negative.");
        if (!IsbnRules.IsValid(book.Isbn)) return (false, "Enter a valid ISBN-10 or ISBN-13.");
        book.AvailableQuantity = book.TotalQuantity;
        try
        {
            if (await IsDuplicateIsbnAsync(book.Isbn, null, cancellationToken))
                return (false, "That ISBN is already registered to an active book.");
            db.Books.Add(book);
            await db.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return (false, "That ISBN is already registered to an active book.");
        }
        catch (DbUpdateException)
        {
            return (false, "The book could not be saved. Refresh the list and try again.");
        }
        catch (DbException)
        {
            return (false, "The database could not complete the save. No book was added.");
        }
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(Book input, CancellationToken cancellationToken = default)
    {
        if (input.RowVersion.Length is not (8 or 16))
            return (false, "This edit form is invalid. Reload the book and try again.");

        try
        {
            var book = await db.Books.FirstOrDefaultAsync(item => item.Id == input.Id, cancellationToken);
            if (book is null) return (false, "Book not found.");
            Normalize(input);
            if (input.TotalQuantity < 0) return (false, "Quantity cannot be negative.");
            if (!IsbnRules.IsValid(input.Isbn)) return (false, "Enter a valid ISBN-10 or ISBN-13.");
            if (await IsDuplicateIsbnAsync(input.Isbn, input.Id, cancellationToken))
                return (false, "That ISBN is already registered to another active book.");

            var checkedOut = book.TotalQuantity - book.AvailableQuantity;
            if (input.TotalQuantity < checkedOut)
                return (false, $"Total quantity cannot be lower than copies currently on loan ({checkedOut} copies currently issued).");

            db.Entry(book).Property(item => item.RowVersion).OriginalValue = input.RowVersion;
            book.Title = input.Title; book.Author = input.Author; book.Isbn = input.Isbn;
            book.Category = input.Category; book.Publisher = input.Publisher;
            book.PublicationYear = input.PublicationYear; book.Description = input.Description;
            book.TotalQuantity = input.TotalQuantity;
            book.AvailableQuantity = input.TotalQuantity - checkedOut;
            book.IsActive = input.IsActive;
            await db.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "This book changed while you were editing it. Reload the book and reapply your changes.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return (false, "That ISBN is already registered. Reload the book list.");
        }
        catch (DbUpdateException)
        {
            return (false, "The book could not be saved because the record changed or violated a database rule. Reload and try again.");
        }
        catch (DbException)
        {
            return (false, "The database could not complete the update. No automatic retry was made.");
        }
    }

    public async Task<(bool Success, string? Error)> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var book = await db.Books.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (book is null) return (false, "Book not found.");

            // Prevent deletion if copies are actively on loan
            var hasActiveLoans = await db.IssueRecords.AnyAsync(
                issue => issue.BookId == id && issue.Status == IssueStatus.Active, cancellationToken);
            if (hasActiveLoans)
                return (false, "Cannot remove book while copies are currently out on loan. Receive all returned copies first.");

            if (await db.IssueRecords.AnyAsync(issue => issue.BookId == id, cancellationToken))
            {
                book.IsActive = false;
            }
            else
            {
                db.Books.Remove(book);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "This book changed during removal. Refresh the list and try again.");
        }
        catch (DbUpdateException)
        {
            return (false, "The book could not be removed because another record references it.");
        }
        catch (DbException)
        {
            return (false, "The database could not complete removal.");
        }
    }

    public async Task<(bool Success, string? Error)> RestoreAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (book is null) return (false, "Book not found.");
            if (book.IsActive) return (false, "Book is already active.");

            if (await IsDuplicateIsbnAsync(book.Isbn, book.Id, cancellationToken))
                return (false, "Cannot restore book: Another active book is currently using this ISBN.");

            book.IsActive = true;
            await db.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (DbUpdateException)
        {
            return (false, "Failed to restore book due to a database conflict.");
        }
    }

    private Task<bool> IsDuplicateIsbnAsync(string? isbn, int? exceptId, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(isbn)
            ? Task.FromResult(false)
            : db.Books.AnyAsync(book => book.IsActive && book.Isbn == isbn && book.Id != exceptId, cancellationToken);

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        var baseEx = exception.GetBaseException();
        if (baseEx is SqlException sql && sql.Number is 2601 or 2627) return true;
        if (baseEx is Microsoft.Data.Sqlite.SqliteException sqlite && sqlite.SqliteErrorCode == 19) return true;
        return false;
    }

    private static void Normalize(Book book)
    {
        book.Title = book.Title.Trim();
        book.Author = book.Author.Trim();
        book.Category = book.Category.Trim();
        book.Isbn = IsbnRules.Normalize(book.Isbn);
        book.Publisher = string.IsNullOrWhiteSpace(book.Publisher) ? null : book.Publisher.Trim();
    }
}
