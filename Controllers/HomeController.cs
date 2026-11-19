using System.Diagnostics;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagementSystem.Controllers;

public sealed class HomeController(ILogger<HomeController> logger) : Controller
{
    [AllowAnonymous]
    public IActionResult Index() => User.Identity?.IsAuthenticated == true
        ? RedirectToAction("Index", "Dashboard")
        : View();

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? statusCode = null)
    {
        var code = statusCode ?? (Response.StatusCode >= 400 ? Response.StatusCode : 500);
        Response.StatusCode = code;

        var reExecuteFeature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        var originalPath = reExecuteFeature?.OriginalPath ?? HttpContext.Request.Path.Value;

        var exceptionHandlerFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (exceptionHandlerFeature?.Error is not null)
        {
            code = 500;
            Response.StatusCode = 500;
            logger.LogError(exceptionHandlerFeature.Error, "Unhandled application exception on {Path}: {Message}", 
                exceptionHandlerFeature.Path, exceptionHandlerFeature.Error.Message);
        }

        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        var model = new ErrorViewModel
        {
            StatusCode = code,
            RequestId = requestId,
            OriginalPath = originalPath
        };

        switch (code)
        {
            case 404:
                model.Title = "Lost in the Stacks";
                model.Subtitle = "Page Not Found";
                model.Description = "The volume, shelf, or catalogue record you were looking for doesn't exist or may have been relocated.";
                break;
            case 403:
                model.Title = "Restricted Archive";
                model.Subtitle = "Access Denied";
                model.Description = "You do not have the required permissions to access this administrative resource. Please sign in with an authorized account.";
                break;
            case 400:
                model.Title = "Invalid Request";
                model.Subtitle = "Bad Request";
                model.Description = "The library desk was unable to process your request due to malformed or invalid syntax.";
                break;
            default:
                model.Title = "A Shelf Disturbance";
                model.Subtitle = "Unexpected Application Error";
                model.Description = "An unexpected error occurred while processing your request. System telemetry has logged this incident code.";
                break;
        }

        return View(model);
    }
}
