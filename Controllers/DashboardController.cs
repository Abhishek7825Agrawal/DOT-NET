using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagementSystem.Controllers;

[Authorize]
public sealed class DashboardController(DashboardService dashboards) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.IsInRole("Admin"))
            return View("Admin", await dashboards.GetAdminAsync(cancellationToken));
        if (User.IsInRole("Staff"))
            return View("Staff", await dashboards.GetAdminAsync(cancellationToken));
        return View("Member", await dashboards.GetMemberAsync(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value, cancellationToken));
    }
}
