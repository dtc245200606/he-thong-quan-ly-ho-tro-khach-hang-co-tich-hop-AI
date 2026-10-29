using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using CustomerSupportAI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.Controllers;

/// <summary>Customer management for staff (and admins). Only accounts with the Customer role are reachable here.</summary>
[Authorize(Roles = UserRoles.Admin + "," + UserRoles.Staff)]
public sealed class CustomersController(AppDbContext db, UserAdministrationService admin) : Controller
{
    private const string Portal = "Customers";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? status, string? sort, int page = 1)
    {
        var query = db.Users.AsNoTracking().Where(user => user.Role == UserRoles.Customer);
        if (status == "active")
        {
            query = query.Where(user => user.IsActive);
        }
        else if (status == "locked")
        {
            query = query.Where(user => !user.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // Phone numbers are stored canonically ("0912345678"); normalise "+84 912 345 678" style input to match.
            var phone = PhoneNumber.Normalize(term) ?? term;
            query = query.Where(user => user.DisplayName.Contains(term) || user.Email.Contains(term)
                || (user.PhoneNumber != null && user.PhoneNumber.Contains(phone)));
        }

        sort = sort is "id" or "name" or "name_desc" or "email" or "email_desc" ? sort : "id_desc";
        query = UserSorting.Apply(query, sort);
        var paging = PageInfo.For(page, 20, await query.CountAsync());
        var users = await query.Skip(paging.Skip).Take(paging.PageSize).ToListAsync();
        var ids = users.Select(user => user.Id).ToList();
        var ticketCounts = await db.Tickets
            .Where(ticket => ids.Contains(ticket.CustomerId))
            .GroupBy(ticket => ticket.CustomerId)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.UserId, item => item.Count);

        return View(new UserListViewModel
        {
            Users = users.Select(user => new UserListItemViewModel
            {
                User = user,
                TicketCount = ticketCounts.GetValueOrDefault(user.Id)
            }).ToList(),
            Paging = paging,
            Sort = sort,
            Search = search?.Trim() ?? string.Empty,
            Status = status ?? string.Empty,
            TotalCustomers = await db.Users.CountAsync(user => user.Role == UserRoles.Customer)
        });
    }

    [HttpGet]
    public IActionResult Create() => View("UserCreate", NewCreateModel());

    [HttpPost]
    public async Task<IActionResult> Create(UserCreateViewModel model)
    {
        model.Portal = Portal;
        model.Role = UserRoles.Customer;
        model.AllowedRoles = [UserRoles.Customer];
        if (!ModelState.IsValid)
        {
            return View("UserCreate", model);
        }

        var result = await admin.CreateAsync(User, model.DisplayName, model.Email, UserRoles.Customer, model.Password);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("UserCreate", model);
        }

        TempData["Success"] = $"Đã tạo tài khoản khách hàng {model.Email.Trim()}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var customer = await FindCustomerAsync(id, tracking: false);
        return customer is null
            ? NotFound()
            : View("UserDetails", await admin.BuildDetailsAsync(User, customer, Portal));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var customer = await FindCustomerAsync(id, tracking: false);
        return customer is null
            ? NotFound()
            : View("UserEdit", UserAdministrationService.ToEditModel(User, customer, Portal));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, UserEditViewModel model)
    {
        var customer = await FindCustomerAsync(id, tracking: true);
        if (customer is null)
        {
            return NotFound();
        }

        UserAdministrationService.Decorate(model, User, customer, Portal);
        if (!ModelState.IsValid)
        {
            return View("UserEdit", model);
        }

        var result = await admin.UpdateAsync(
            User, customer, model.DisplayName, model.Email, UserRoles.Customer, model.IsActive, model.NewPassword);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("UserEdit", model);
        }

        TempData["Success"] = "Đã cập nhật thông tin khách hàng.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        var customer = await FindCustomerAsync(id, tracking: true);
        if (customer is null)
        {
            return NotFound();
        }

        var result = await admin.SetActiveAsync(User, customer, active);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? (active ? "Đã mở khóa tài khoản." : "Đã khóa tài khoản.") : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await FindCustomerAsync(id, tracking: true);
        if (customer is null)
        {
            return NotFound();
        }

        var email = customer.Email;
        var result = await admin.DeleteAsync(User, customer);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = $"Đã xóa khách hàng {email} cùng các yêu cầu liên quan.";
        return RedirectToAction(nameof(Index));
    }

    private UserCreateViewModel NewCreateModel() => new()
    {
        Portal = Portal,
        Role = UserRoles.Customer,
        AllowedRoles = [UserRoles.Customer]
    };

    private Task<SupportUser?> FindCustomerAsync(int id, bool tracking)
    {
        var query = tracking ? db.Users.AsQueryable() : db.Users.AsNoTracking();
        return query.SingleOrDefaultAsync(user => user.Id == id && user.Role == UserRoles.Customer);
    }
}
