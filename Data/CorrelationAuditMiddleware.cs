using System.Diagnostics;
using System.Security.Claims;
using LibraryManagementSystem.Services;

namespace LibraryManagementSystem.Data;

public sealed class CorrelationAuditMiddleware(RequestDelegate next, ILogger<CorrelationAuditMiddleware> logger)
{
    public const string CorrelationItemKey = "Shelfwise.CorrelationId";

    public async Task InvokeAsync(HttpContext context, ActivityLogService activityLogs)
    {
        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.Items[CorrelationItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            try
            {
                await next(context);
                if (context.Response.StatusCode == StatusCodes.Status403Forbidden)
                {
                    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                    await activityLogs.RecordSecurityAsync(userId, "AuthorizationDenied", "Authorization",
                        "A request was denied by authorization policy.", "Failure", "Warning",
                        cancellationToken: context.RequestAborted);
                }
            }
            catch (Exception exception)
            {
                logger.LogError("Unhandled request exception type {ExceptionType} at {RequestPath}",
                    exception.GetType().Name, context.Request.Path.Value);
                await activityLogs.RecordSecurityAsync(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    "UnhandledException", "Application", "A request failed with an unhandled server exception.",
                    "Failure", "Error", cancellationToken: CancellationToken.None);
                throw;
            }
        }
    }
}
