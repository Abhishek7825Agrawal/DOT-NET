namespace LibraryManagementSystem.Models;

public enum PaperStatus
{
    Draft = 0,
    PendingReview = 1,
    Approved = 2,
    CorrectionRequested = 3,
    Rejected = 4,
    Suspended = 5
}

public enum PaperPaymentStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Refunded = 3
}

public enum CopyrightReportStatus
{
    UnderReview = 0,
    UpheldSuspended = 1,
    Dismissed = 2
}
