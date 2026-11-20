using System.Security.Claims;
using LibraryManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Data;

public sealed class ActiveMemberMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ApplicationDbContext db,
        SignInManager<ApplicationUser> signInManager)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isActive = userId is not null && await db.Users.AsNoTracking()
                .AnyAsync(user => user.Id == userId && user.IsActive, context.RequestAborted);
            if (isActive && context.User.IsInRole("Member"))
            {
                isActive = await db.Members.AsNoTracking()
                    .AnyAsync(member => member.UserId == userId && member.IsActive, context.RequestAborted);
            }
            if (!isActive)
            {
                await signInManager.SignOutAsync();
                context.Response.Redirect("/Account/Login?reason=inactive");
                return;
            }
        }
        await next(context);
    }
}
