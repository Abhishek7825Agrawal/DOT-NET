namespace LibraryManagementSystem.ViewModels;

public sealed class ErrorViewModel
{
    public int StatusCode { get; set; } = 500;
    public string? RequestId { get; set; }
    public string? OriginalPath { get; set; }
    public bool ShowRequestId => !string.IsNullOrWhiteSpace(RequestId);

    public string Title { get; set; } = "Something went wrong";
    public string Subtitle { get; set; } = "Unexpected Error";
    public string Description { get; set; } = "An unexpected error occurred while processing your request.";
}
