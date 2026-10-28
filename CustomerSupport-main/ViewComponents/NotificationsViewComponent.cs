using System.Security.Claims;
using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.ViewComponents;

/// <summary>
/// Header notification menu: the latest ticket messages written by someone else
/// in tickets the signed-in user is allowed to see. There is no read/unread state.
/// </summary>
public sealed class NotificationsViewComponent(AppDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var principal = (ClaimsPrincipal)User;
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            return Content(string.Empty);
        }

        var staff = principal.IsInRole(UserRoles.Admin) || principal.IsInRole(UserRoles.Staff);
        var messages = await db.TicketMessages.AsNoTracking()
            .Where(m => m.AuthorId != id && (staff || m.Ticket.CustomerId == id))
            .Include(m => m.Author).Include(m => m.Ticket)
            .OrderByDescending(m => m.Id).Take(6).ToListAsync();
        return View(messages);
    }
}
