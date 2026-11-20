using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using LibraryManagementSystem.ViewModels;
using LibraryManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace LibraryManagementSystem.Controllers;

public sealed class AccountController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IShelfwiseEmailSender emailSender,
    ActivityLogService activityLogs,
    IWebHostEnvironment environment,
    IConfiguration configuration) : Controller
{
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null, string? reason = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        if (reason == "inactive") ModelState.AddModelError(string.Empty, "This member account is inactive.");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        // Resolve by normalized email first. The string overload of PasswordSignInAsync
        // treats its first argument as a username, which can reject a valid email account
        // when a legacy user's UserName differs from Email.
        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null || string.IsNullOrWhiteSpace(user.UserName))
        {
            await activityLogs.RecordSecurityAsync(null, "LoginFailed", "Authentication",
                "A sign-in attempt did not match an account.", "Failure", "Warning", cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, "Email or password is incorrect.");
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await activityLogs.RecordSecurityAsync(user.Id, "LoginLocked", "Authentication",
                "Sign-in was blocked because the account is locked.", "Failure", "Warning", "User", user.Id, cancellationToken);
            ModelState.AddModelError(string.Empty, "This account is locked due to multiple failed login attempts or administrator deactivation.");
            return View(model);
        }
        if (!result.Succeeded)
        {
            await activityLogs.RecordSecurityAsync(user.Id, "LoginFailed", "Authentication",
                "A sign-in attempt failed.", "Failure", "Warning", "User", user.Id, cancellationToken);
            ModelState.AddModelError(string.Empty, "Email or password is incorrect.");
            return View(model);
        }

        var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (member is { IsActive: false })
        {
            await signInManager.SignOutAsync();
            await activityLogs.RecordSecurityAsync(user.Id, "LoginBlockedInactive", "Authentication",
                "Sign-in was blocked for an inactive account.", "Failure", "Warning", "User", user.Id, cancellationToken);
            ModelState.AddModelError(string.Empty, "This member account has been deactivated. Please contact the library administrator.");
            return View(model);
        }
        if (!user.IsActive)
        {
            await signInManager.SignOutAsync();
            await activityLogs.RecordSecurityAsync(user.Id, "LoginBlockedInactive", "Authentication",
                "Sign-in was blocked for an inactive account.", "Failure", "Warning", "User", user.Id, cancellationToken);
            ModelState.AddModelError(string.Empty, "Email or password is incorrect.");
            return View(model);
        }
        await activityLogs.RecordAsync(user.Id, "LoginSucceeded", "Authentication", "You logged in.",
            entityType: "User", entityReference: user.Id, securityEvent: true, cancellationToken: cancellationToken);
        if (Url.IsLocalUrl(model.ReturnUrl)) return LocalRedirect(model.ReturnUrl!);
        return RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is not null && (await userManager.IsInRoleAsync(user, "Member") || await userManager.IsInRoleAsync(user, "Staff")))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var resetUrl = BuildSecureResetUrl(user.Email!, token);
            EmailSendResult delivery;
            if (resetUrl is null)
                delivery = new EmailSendResult(false, "A secure public base URL is not configured.");
            else
                delivery = await emailSender.SendPasswordResetLinkAsync(user.Email!, user.UserName ?? "Shelfwise member", resetUrl, cancellationToken);

            await activityLogs.RecordSecurityAsync(user.Id, "PasswordResetRequested", "PasswordRecovery",
                "A password reset link was requested.", delivery.AcceptedByProvider ? "AcceptedByProvider" : "DeliveryUnavailable",
                delivery.AcceptedByProvider ? "Information" : "Warning", "User", user.Id, cancellationToken);
            await activityLogs.RecordAsync(user.Id, "PasswordResetRequested", "PasswordRecovery",
                "You requested a password reset link.", "Requested", "User", user.Id, cancellationToken: cancellationToken);
        }
        else
        {
            // Deliberately omit the supplied email to avoid storing unnecessary personal data.
            await activityLogs.RecordSecurityAsync(null, "PasswordResetRequested", "PasswordRecovery",
                "A password reset request did not match an eligible account.", "NoMatchingAccount", "Information",
                cancellationToken: cancellationToken);
        }

        return View("ForgotPasswordConfirmation");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return View("ResetPasswordInvalid");
        return View(new ResetPasswordViewModel { Email = email });
    }

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null || !(await userManager.IsInRoleAsync(user, "Member") || await userManager.IsInRoleAsync(user, "Staff")))
        {
            await activityLogs.RecordSecurityAsync(null, "PasswordResetFailed", "PasswordRecovery",
                "A password reset link could not be validated.", "Failure", "Warning", cancellationToken: cancellationToken);
            return View("ResetPasswordInvalid");
        }

        var result = await userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
        if (!result.Succeeded)
        {
            await activityLogs.RecordSecurityAsync(user.Id, "PasswordResetFailed", "PasswordRecovery",
                "A password reset attempt failed validation.", "Failure", "Warning", "User", user.Id, cancellationToken);
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Code == "InvalidToken"
                    ? "This reset link is invalid or expired. Request a new link to continue."
                    : error.Description);
            return View(model);
        }

        await userManager.UpdateSecurityStampAsync(user);
        await activityLogs.RecordAsync(user.Id, "PasswordResetCompleted", "PasswordRecovery",
            "Your password was reset successfully.", entityType: "User", entityReference: user.Id,
            securityEvent: true, cancellationToken: cancellationToken);
        await signInManager.SignOutAsync();
        return View("ResetPasswordConfirmation");
    }

    [AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            PhoneNumber = model.PhoneNumber?.Trim()
        };
        var created = await userManager.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var error in created.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        var roleResult = await userManager.AddToRoleAsync(user, "Member");
        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        db.Members.Add(new Member
        {
            UserId = user.Id,
            FullName = model.FullName.Trim(),
            PhoneNumber = model.PhoneNumber?.Trim(),
            Address = model.Address?.Trim(),
            RegisteredAtUtc = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await activityLogs.RecordAsync(user.Id, "AccountRegistered", "AccountManagement",
            "You registered a member account.", entityType: "User", entityReference: user.Id,
            securityEvent: true, cancellationToken: cancellationToken);
        if (User.IsInRole("Admin"))
            return RedirectToAction("Index", "AdminMembers");
        await signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Dashboard");
    }

    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost, Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction("Login");

        var result = await userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await userManager.UpdateSecurityStampAsync(user);
        await signInManager.RefreshSignInAsync(user);
        await activityLogs.RecordAsync(user.Id, "PasswordChanged", "PasswordRecovery",
            "You changed your password.", entityType: "User", entityReference: user.Id,
            securityEvent: true);
        TempData["Success"] = "Your password has been changed successfully.";
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = userManager.GetUserId(User);
        await activityLogs.RecordAsync(userId, "Logout", "Authentication", "You logged out.",
            entityType: "User", entityReference: userId, securityEvent: true);
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> SetupPassword(string? userId, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
        {
            TempData["Error"] = "Invalid or expired activation link.";
            return RedirectToAction(nameof(Login));
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            TempData["Error"] = "User account not found.";
            return RedirectToAction(nameof(Login));
        }

        var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);

        var model = new SetupPasswordViewModel
        {
            UserId = userId,
            Token = token,
            Email = user.Email ?? string.Empty,
            FullName = member?.FullName ?? user.UserName ?? "Member"
        };

        return View(model);
    }

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> SetupPassword(SetupPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            ModelState.AddModelError(string.Empty, $"Validation error: {errors}");
            return View(model);
        }

        var user = await userManager.FindByIdAsync(model.UserId);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "User account not found.");
            return View(model);
        }

        var resetResult = await userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
        if (!resetResult.Succeeded)
        {
            foreach (var error in resetResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);

        TempData["Success"] = "Your account has been activated and your password set successfully. You can now log in.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    private string? BuildSecureResetUrl(string email, string token)
    {
        var configuredBaseUrl = configuration["Email:PublicBaseUrl"];
        Uri? baseUri = null;
        if (Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredUri))
            baseUri = configuredUri;
        else if (environment.IsDevelopment() && Request.Host.HasValue
                 && (Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                     || System.Net.IPAddress.TryParse(Request.Host.Host, out var ip) && System.Net.IPAddress.IsLoopback(ip)))
            baseUri = new Uri($"{Request.Scheme}://{Request.Host}");

        if (baseUri is null || (!environment.IsDevelopment() && baseUri.Scheme != Uri.UriSchemeHttps))
            return null;

        var path = Url.Action(nameof(ResetPassword), "Account", new { email }) ?? "/Account/ResetPassword";
        var absoluteUrl = new UriBuilder(baseUri) { Path = path.Split('?', 2)[0], Query = path.Contains('?') ? path.Split('?', 2)[1] : string.Empty }.Uri;
        return absoluteUrl.ToString() + "#token=" + Uri.EscapeDataString(token);
    }
}
