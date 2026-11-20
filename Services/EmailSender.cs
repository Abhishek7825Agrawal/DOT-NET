using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace LibraryManagementSystem.Services;

public sealed class EmailSettings
{
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Shelfwise Library";
    public string? PublicBaseUrl { get; set; }
    public int ResetTokenLifetimeMinutes { get; set; } = 60;
}

public sealed record EmailSendResult(bool AcceptedByProvider, string? FailureReason = null);

public interface IShelfwiseEmailSender
{
    Task<EmailSendResult> SendPasswordResetLinkAsync(
        string recipient,
        string displayName,
        string resetUrl,
        CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailSender(IOptions<EmailSettings> options, ILogger<SmtpEmailSender> logger)
    : IShelfwiseEmailSender
{
    private readonly EmailSettings settings = options.Value;

    public async Task<EmailSendResult> SendPasswordResetLinkAsync(
        string recipient,
        string displayName,
        string resetUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.FromAddress))
            return new EmailSendResult(false, "Email delivery is not configured.");

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(settings.FromAddress, settings.FromName),
                Subject = "Reset your Shelfwise password",
                Body = $"Hello {displayName},\r\n\r\nUse the secure link below to reset your Shelfwise password. The link expires soon and can only be used once.\r\n\r\n{resetUrl}\r\n\r\nIf you did not request this, you can ignore this message.\r\n",
                IsBodyHtml = false
            };
            message.To.Add(new MailAddress(recipient));

            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };
            if (!string.IsNullOrWhiteSpace(settings.Username))
                client.Credentials = new NetworkCredential(settings.Username, settings.Password ?? string.Empty);

            await client.SendMailAsync(message, cancellationToken);
            logger.LogInformation("Password reset email accepted by configured SMTP provider.");
            return new EmailSendResult(true);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Password reset email was not accepted; exception type {ExceptionType}.",
                exception.GetType().Name);
            return new EmailSendResult(false, "The configured email provider did not accept the message.");
        }
    }
}
