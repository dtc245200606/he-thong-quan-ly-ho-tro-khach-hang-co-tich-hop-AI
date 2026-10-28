using System.Security.Claims;
using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using CustomerSupportAI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.Controllers;

[Authorize]
public sealed class TicketsController(AppDbContext db, TicketAiService ticketAi, UserAdministrationService admin) : Controller
{
    private static readonly string[] Categories =
    [
        "Product support",
        "Billing",
        "Warranty & repair",
        "Account access",
        "Other"
    ];

    private static readonly string[] Statuses = ["Open", "In progress", "Resolved", "Closed"];

    private const int PageSize = 20;

    /// <summary>Pseudo-status for the filter: open or in-progress tickets whose newest message came from the customer.</summary>
    public const string AwaitingFilter = "Awaiting";

    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<IActionResult> Index(string? search, string? status, string? category, int? customerId,
        DateOnly? from, DateOnly? to, string? sort, int page = 1)
    {
        var staff = IsStaff();
        var tickets = VisibleTickets();
        var totalCount = await tickets.CountAsync();
        var openCount = await tickets.CountAsync(ticket => ticket.Status == "Open" || ticket.Status == "In progress");
        var resolvedCount = await tickets.CountAsync(ticket => ticket.Status == "Resolved");
        var awaitingCount = staff ? await AwaitingTeam(tickets).CountAsync() : 0;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            tickets = tickets.Where(ticket =>
                ticket.Subject.Contains(term) || ticket.TicketNumber.Contains(term));
        }

        if (staff && status == AwaitingFilter)
        {
            tickets = AwaitingTeam(tickets);
        }
        else if (!string.IsNullOrWhiteSpace(status) && Statuses.Contains(status, StringComparer.Ordinal))
        {
            tickets = tickets.Where(ticket => ticket.Status == status);
        }
        else
        {
            status = null;
        }

        category = category is not null && Categories.Contains(category, StringComparer.Ordinal) ? category : null;
        if (category is not null)
        {
            tickets = tickets.Where(ticket => ticket.Category == category);
        }

        SupportUser? customer = null;
        if (staff && customerId is { } cid)
        {
            customer = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == cid && user.Role == UserRoles.Customer);
            if (customer is not null)
            {
                tickets = tickets.Where(ticket => ticket.CustomerId == cid);
            }
        }

        if (from is not null && to is not null && from > to)
        {
            (from, to) = (to, from);
        }

        if (from is not null || to is not null)
        {
            // SQLite cannot compare DateTimeOffset, so the range (Vietnam dates) is resolved to ticket ids in memory.
            var lower = from?.ToDateTime(TimeOnly.MinValue);
            var upper = to?.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var ids = (await tickets.Select(ticket => new { ticket.Id, ticket.CreatedAt }).ToListAsync())
                .Where(row =>
                {
                    var day = row.CreatedAt.ToOffset(VietnamOffset).DateTime;
                    return (lower is null || day >= lower) && (upper is null || day < upper);
                })
                .Select(row => row.Id)
                .ToList();
            tickets = tickets.Where(ticket => ids.Contains(ticket.Id));
        }

        // SQLite cannot order by DateTimeOffset, so creation order (Id) stands in for time.
        sort = sort is "id" or "subject" or "subject_desc" or "status" or "status_desc" or "customer" or "customer_desc"
            ? sort : "id_desc";
        tickets = sort switch
        {
            "id" => tickets.OrderBy(ticket => ticket.Id),
            "subject" => tickets.OrderBy(ticket => ticket.Subject).ThenByDescending(ticket => ticket.Id),
            "subject_desc" => tickets.OrderByDescending(ticket => ticket.Subject).ThenByDescending(ticket => ticket.Id),
            "status" => tickets.OrderBy(ticket => ticket.Status).ThenByDescending(ticket => ticket.Id),
            "status_desc" => tickets.OrderByDescending(ticket => ticket.Status).ThenByDescending(ticket => ticket.Id),
            "customer" => tickets.OrderBy(ticket => ticket.Customer.DisplayName).ThenByDescending(ticket => ticket.Id),
            "customer_desc" => tickets.OrderByDescending(ticket => ticket.Customer.DisplayName).ThenByDescending(ticket => ticket.Id),
            _ => tickets.OrderByDescending(ticket => ticket.Id)
        };

        var paging = PageInfo.For(page, PageSize, await tickets.CountAsync());
        var items = await tickets
            .Include(ticket => ticket.Customer)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .ToListAsync();

        // Per row: message count, and whether the newest message is the customer's (the team owes a reply).
        var pageIds = items.Select(ticket => ticket.Id).ToList();
        var threads = await db.TicketMessages
            .Where(message => pageIds.Contains(message.TicketId))
            .GroupBy(message => message.TicketId)
            .Select(group => new { TicketId = group.Key, Count = group.Count(), LastId = group.Max(message => message.Id) })
            .ToListAsync();
        var lastIds = threads.Select(thread => thread.LastId).ToList();
        var lastByCustomer = await db.TicketMessages
            .Where(message => lastIds.Contains(message.Id) && message.Author.Role == UserRoles.Customer)
            .Select(message => message.TicketId)
            .ToListAsync();

        return View(new TicketIndexViewModel
        {
            Tickets = items,
            MessageCounts = threads.ToDictionary(thread => thread.TicketId, thread => thread.Count),
            AwaitingTeam = lastByCustomer.ToHashSet(),
            Paging = paging,
            Sort = sort,
            Search = search?.Trim() ?? string.Empty,
            Status = status ?? string.Empty,
            Category = category ?? string.Empty,
            Customer = customer,
            From = from,
            To = to,
            Categories = Categories,
            TotalCount = totalCount,
            OpenCount = openCount,
            ResolvedCount = resolvedCount,
            AwaitingCount = awaitingCount,
            IsStaff = staff
        });
    }

    [Authorize(Roles = UserRoles.Customer)]
    [HttpGet]
    public IActionResult Create() => View(new TicketCreateViewModel());

    [Authorize(Roles = UserRoles.Customer)]
    [HttpPost]
    public async Task<IActionResult> Create(TicketCreateViewModel model)
    {
        if (!Categories.Contains(model.Category, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(model.Category), "Vui lòng chọn danh mục hợp lệ.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var customerId = CurrentUserId();
        var ticket = new SupportTicket
        {
            TicketNumber = $"CS-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            Subject = model.Subject.Trim(),
            Category = model.Category,
            Status = "Open",
            CustomerId = customerId
        };
        ticket.Messages.Add(new TicketMessage
        {
            AuthorId = customerId,
            Body = model.FirstMessage.Trim()
        });

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        TempData["Success"] = $"Đã tạo yêu cầu {ticket.TicketNumber}.";
        return RedirectToAction(nameof(Details), new { id = ticket.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var ticket = await VisibleTickets()
            .Include(item => item.Customer)
            .Include(item => item.Messages.OrderBy(message => message.Id))
                .ThenInclude(message => message.Author)
            .SingleOrDefaultAsync(item => item.Id == id);

        if (ticket is null)
        {
            return NotFound();
        }

        return View(new TicketDetailsViewModel
        {
            Ticket = ticket,
            IsStaff = IsStaff(),
            Categories = Categories,
            AiConfigured = ticketAi.IsConfigured,
            CustomerTicketCount = IsStaff() ? await db.Tickets.CountAsync(item => item.CustomerId == ticket.CustomerId) : 0
        });
    }

    [HttpPost]
    public async Task<IActionResult> Reply(TicketReplyViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Vui lòng nhập phản hồi không quá 4.000 ký tự.";
            return RedirectToAction(nameof(Details), new { id = model.TicketId });
        }

        var ticket = await VisibleTickets().SingleOrDefaultAsync(item => item.Id == model.TicketId);
        if (ticket is null)
        {
            return NotFound();
        }

        if (ticket.Status == "Closed")
        {
            TempData["Error"] = "Yêu cầu đã đóng nên không thể nhận thêm phản hồi.";
            return RedirectToAction(nameof(Details), new { id = ticket.Id });
        }

        db.TicketMessages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorId = CurrentUserId(),
            Body = model.Body.Trim()
        });
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        if (IsStaff() && ticket.Status == "Open")
        {
            ticket.Status = "In progress";
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "Đã gửi phản hồi.";
        return RedirectToAction(nameof(Details), new { id = ticket.Id });
    }

    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Staff)]
    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        if (!Statuses.Contains(status, StringComparer.Ordinal))
        {
            return BadRequest();
        }

        var ticket = await db.Tickets.SingleOrDefaultAsync(item => item.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        ticket.Status = status;
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = "Đã cập nhật trạng thái yêu cầu.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Staff)]
    [HttpPost]
    public async Task<IActionResult> UpdateCategory(int id, string category)
    {
        if (!Categories.Contains(category, StringComparer.Ordinal))
        {
            return BadRequest();
        }

        var ticket = await db.Tickets.SingleOrDefaultAsync(item => item.Id == id);
        if (ticket is null)
        {
            return NotFound();
        }

        if (ticket.Category != category)
        {
            var previous = ticket.Category;
            ticket.Category = category;
            ticket.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await admin.AuditAsync(User, "ticket.category", ticket.TicketNumber,
                $"{DisplayText.TicketCategory(previous)} → {DisplayText.TicketCategory(category)}");
        }

        TempData["Success"] = "Đã cập nhật danh mục yêu cầu.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Staff AI tools: task = classify | draft | summary. Returns a suggestion as JSON and saves nothing
    /// to the ticket; staff review it and apply it through the normal forms.
    /// </summary>
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Staff)]
    [HttpPost, EnableRateLimiting("assistant")]
    public async Task<IActionResult> Ai(int id, string task, CancellationToken cancellationToken)
    {
        if (task is not ("classify" or "draft" or "summary"))
        {
            return BadRequest(new { error = "Tác vụ AI không hợp lệ." });
        }

        var ticket = await db.Tickets.AsNoTracking()
            .Include(item => item.Messages).ThenInclude(message => message.Author)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (ticket is null)
        {
            return NotFound(new { error = "Không tìm thấy yêu cầu." });
        }

        object? value = null;
        string? error;
        var truncated = false;
        if (task == "classify")
        {
            var result = await ticketAi.ClassifyAsync(ticket, Categories, cancellationToken);
            error = result.Error;
            if (result.Value is { } c)
            {
                value = new { category = c.Category, categoryText = DisplayText.TicketCategory(c.Category), priority = c.Priority, reason = c.Reason, same = c.Category == ticket.Category };
            }
        }
        else if (task == "draft")
        {
            var lastCustomerMessage = ticket.Messages.Where(message => message.Author.Role == UserRoles.Customer)
                .OrderByDescending(message => message.Id).Select(message => message.Body).FirstOrDefault() ?? string.Empty;
            var articles = KnowledgeSearch.Find(await db.KnowledgeArticles.AsNoTracking().ToListAsync(cancellationToken),
                ticket.Subject + " " + lastCustomerMessage);
            var result = await ticketAi.DraftReplyAsync(ticket, articles, User.Identity?.Name ?? "Đội ngũ hỗ trợ", cancellationToken);
            (error, truncated) = (result.Error, result.Truncated);
            if (result.Value is { } text)
            {
                value = new { text, sources = articles.Select(a => new { title = a.Title, url = Url.Action("Details", "Knowledge", new { id = a.Id }) }) };
            }
        }
        else
        {
            var result = await ticketAi.SummarizeAsync(ticket, cancellationToken);
            (error, value, truncated) = (result.Error, result.Value, result.Truncated);
        }

        if (value is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error });
        }

        await admin.AuditAsync(User, "ai.ticket", ticket.TicketNumber, task switch
        {
            "classify" => "AI phân loại yêu cầu",
            "draft" => "AI soạn nháp phản hồi",
            _ => "AI tóm tắt lịch sử"
        });
        return Ok(new { result = value, truncated, generatedAt = DateTimeOffset.UtcNow.ToOffset(VietnamOffset).ToString("HH:mm dd/MM/yyyy") });
    }

    /// <summary>Open or in-progress tickets whose newest message was written by the customer.</summary>
    private static IQueryable<SupportTicket> AwaitingTeam(IQueryable<SupportTicket> tickets) => tickets.Where(ticket =>
        (ticket.Status == "Open" || ticket.Status == "In progress") &&
        ticket.Messages.OrderByDescending(message => message.Id).Select(message => message.Author.Role).FirstOrDefault() == UserRoles.Customer);

    private IQueryable<SupportTicket> VisibleTickets()
    {
        var tickets = db.Tickets.AsQueryable();
        return IsStaff()
            ? tickets
            : tickets.Where(ticket => ticket.CustomerId == CurrentUserId());
    }

    private bool IsStaff() => User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.Staff);

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}