using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Member")]
public sealed class ProfileController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User)!;
        var member = await db.Members.AsNoTracking().Include(item => item.User)
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (member is null) return NotFound();
        return View(new ProfileViewModel
        {
            FullName = member.FullName, Email = member.User.Email ?? string.Empty,
            PhoneNumber = member.PhoneNumber, Address = member.Address, RegisteredAtUtc = member.RegisteredAtUtc
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(ProfileViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var userId = userManager.GetUserId(User)!;
        var member = await db.Members.Include(item => item.User)
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (member is null) return NotFound();
        if (!string.Equals(member.User.Email, model.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Email), "Changing your email is not available here. Contact the library administrator.");
            return View(model);
        }
        member.FullName = model.FullName.Trim();
        member.PhoneNumber = model.PhoneNumber?.Trim();
        member.User.PhoneNumber = member.PhoneNumber;
        member.Address = model.Address?.Trim();
        await db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(Index));
    }
}
