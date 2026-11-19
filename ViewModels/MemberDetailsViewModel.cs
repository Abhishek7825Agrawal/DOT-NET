namespace LibraryManagementSystem.ViewModels;

using LibraryManagementSystem.Models;

public sealed class MemberDetailsViewModel
{
    public Member Member { get; set; } = null!;
    public int TotalLoans { get; set; }
    public int ActiveLoansCount { get; set; }
    public int ReturnedBooksCount { get; set; }
    public int OverdueLoansCount { get; set; }
    public decimal OutstandingFines { get; set; }
    public decimal TotalFinesPaid { get; set; }

    public IReadOnlyList<IssueRecord> CirculationHistory { get; set; } = [];
    public IReadOnlyList<FinePayment> PaymentHistory { get; set; } = [];
    public DateTime Today { get; set; }
}
