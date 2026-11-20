using LibraryManagementSystem.Models;
using LibraryManagementSystem.Services;
using LibraryManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagementSystem.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminCategoriesController(CategoryService categories, ActivityLogService activityLogs,
    Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var list = await categories.GetCategoriesWithCountsAsync(cancellationToken);
        return View(list);
    }

    public IActionResult Create() => View("Form", new CategoryFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Form", model);

        var result = await categories.CreateAsync(new Category
        {
            Name = model.Name,
            Description = model.Description
        }, cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(nameof(model.Name), result.Error!);
            return View("Form", model);
        }

        await activityLogs.RecordAsync(userManager.GetUserId(User), "CategoryCreated", "Catalogue",
            $"You created category '{model.Name}'.", entityType: "Category", entityReference: model.Name,
            securityEvent: true, cancellationToken: cancellationToken);
        TempData["Success"] = $"Category '{model.Name}' created successfully.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var category = await categories.GetAsync(id, cancellationToken);
        if (category is null) return NotFound();

        return View("Form", new CategoryFormViewModel
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("Form", model);

        var result = await categories.UpdateAsync(new Category
        {
            Id = model.Id,
            Name = model.Name,
            Description = model.Description
        }, cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(nameof(model.Name), result.Error!);
            return View("Form", model);
        }

        await activityLogs.RecordAsync(userManager.GetUserId(User), "CategoryUpdated", "Catalogue",
            $"You updated category '{model.Name}'.", entityType: "Category", entityReference: model.Id.ToString(),
            securityEvent: true, cancellationToken: cancellationToken);
        TempData["Success"] = "Category updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await categories.DeleteAsync(id, cancellationToken);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
        }
        else
        {
            await activityLogs.RecordAsync(userManager.GetUserId(User), "CategoryDeleted", "Catalogue",
                "You deleted a catalogue category.", entityType: "Category", entityReference: id.ToString(),
                securityEvent: true, severity: "Warning", cancellationToken: cancellationToken);
            TempData["Success"] = "Category deleted.";
        }
        return RedirectToAction(nameof(Index));
    }
}

