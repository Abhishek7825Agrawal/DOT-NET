using System.ComponentModel.DataAnnotations;

namespace LibraryManagementSystem.Models;

public enum FinePaymentMethod
{
    Cash = 0,
    BankTransfer = 1,
    Card = 2,
    Other = 3
}

public sealed class FinePayment
{
    public int Id { get; set; }
    public int IssueRecordId { get; set; }
    public IssueRecord IssueRecord { get; set; } = null!;

    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal Amount { get; set; }

    public DateTimeOffset PaymentDateUtc { get; set; }
    public FinePaymentMethod PaymentMethod { get; set; }

    [StringLength(120)]
    public string? ReferenceNumber { get; set; }
}
