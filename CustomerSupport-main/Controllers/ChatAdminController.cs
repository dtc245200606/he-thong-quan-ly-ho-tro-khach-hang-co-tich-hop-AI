using System.Security.Claims;
using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using CustomerSupportAI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.Controllers;

public sealed record ChatStaffReplyRequest(string? Body, bool Internal);
public sealed record ChatStatusRequest(string? Status);

/// <summary>
/// Staff console for chatbot conversations: list, read, reply publicly or add internal notes. Pages live under
/// /ChatAdmin, JSON under /api/chat/admin. Staff and admins only; AI tool logs are shown to admins only.
/// </summary>
[Authorize(Roles = UserRoles.Admin + "," + UserRoles.Staff)]
public sealed class ChatAdminController(AppDbContext db, ChatbotService chat) : Controller
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    [HttpGet("/ChatAdmin")]
    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        status = ChatStatuses.All.Contains(status) || status == "bot" ? status : null;
        return View(new ChatAdminIndexViewModel(await ListAsync(status, ct), status ?? "", await CountsAsync(ct)));
    }

    [HttpGet("/ChatAdmin/Details/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var session = await db.ChatSessions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
        return session is null ? NotFound() : View(session);
    }

    [HttpGet("/api/chat/admin/sessions")]
    public async Task<IActionResult> Sessions(string? status, CancellationToken ct)
    {
        if (status is { Length: > 0 } && !ChatStatuses.All.Contains(status) && status != "bot")
            return BadRequest(new { error = "Trạng thái không hợp lệ." });
        return Ok(new { sessions = await ListAsync(status, ct), counts = await CountsAsync(ct) });
    }

    [HttpGet("/api/chat/admin/sessions/{id:int}")]
    public async Task<IActionResult> Session(int id, int after, CancellationToken ct)
    {
        var session = await db.ChatSessions.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (session is null) return NotFound(new { error = "Không tìm thấy hội thoại." });
        await chat.MirrorTicketAsync(session, ct);
        var messages = await db.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == id && message.Id > after)
            .OrderBy(message => message.Id).Take(500).ToListAsync(ct);
        var ticket = session.TicketId is { } ticketId
            ? await db.Tickets.AsNoTracking().Where(item => item.Id == ticketId)
                .Select(item => new
                {
                    item.Id, item.TicketNumber, item.Status, item.Priority, item.Subject, item.SlaDueAt,
                    FirstStaffReply = item.Messages.Where(message => message.Author.Role != UserRoles.Customer)
                        .OrderBy(message => message.Id).Select(message => (DateTimeOffset?)message.CreatedAt).FirstOrDefault()
                }).SingleOrDefaultAsync(ct)
            : null;
        var sla = ticket is null ? SlaState.None : SlaPolicy.State(ticket.SlaDueAt, ticket.Status, ticket.FirstStaffReply, DateTimeOffset.UtcNow);
        var customer = session.UserId is { } userId
            ? await db.Users.AsNoTracking().Where(user => user.Id == userId).Select(user => user.DisplayName).SingleOrDefaultAsync(ct)
            : null;
        object? logs = null;
        if (User.IsInRole(UserRoles.Admin))
        {
            logs = (await db.AiLogs.AsNoTracking().Where(log => log.SessionId == id).OrderByDescending(log => log.Id).Take(50).ToListAsync(ct))
                .Select(log => new
                {
                    time = log.CreatedAt.ToOffset(VietnamOffset).ToString("HH:mm:ss dd/MM"),
                    provider = log.Provider, fallback = log.FallbackUsed, success = log.Success, durationMs = log.DurationMs,
                    redacted = log.RedactedCount, rounds = log.ToolRounds, toolCalls = log.ToolCalls, error = log.Error
                });
        }
        return Ok(new
        {
            id = session.Id,
            mode = session.Mode,
            status = session.Status,
            statusText = DisplayText.ChatStatus(session.Status, session.Mode),
            urgent = session.Urgent,
            handoffReason = session.HandoffReason,
            email = session.ContactEmail,
            emailVerified = ChatbotService.IsVerified(session),
            customer,
            signedIn = session.UserId is not null,
            ticket = ticket is null ? null : new
            {
                id = ticket.Id, number = ticket.TicketNumber, subject = ticket.Subject,
                status = DisplayText.TicketStatus(ticket.Status), priority = ticket.Priority,
                sla = sla switch
                {
                    SlaState.Overdue => "Quá hạn SLA phản hồi đầu",
                    SlaState.Missed => "Phản hồi đầu trễ SLA",
                    SlaState.Met => "Đạt SLA phản hồi đầu",
                    SlaState.Pending => "Hạn phản hồi đầu " + ticket.SlaDueAt!.Value.ToOffset(VietnamOffset).ToString("HH:mm dd/MM"),
                    _ => null
                },
                url = Url.Action("Details", "Tickets", new { id = ticket.Id })
            },
            messages = messages.Select(message => new
            {
                id = message.Id, sender = message.Sender, body = message.Body, isInternal = message.IsInternal,
                author = message.AuthorName, time = message.CreatedAt.ToOffset(VietnamOffset).ToString("HH:mm dd/MM")
            }),
            logs
        });
    }

    [HttpPost("/api/chat/admin/sessions/{id:int}/replies")]
    public async Task<IActionResult> Reply(int id, [FromBody] ChatStaffReplyRequest request, CancellationToken ct)
    {
        var session = await db.ChatSessions.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (session is null) return NotFound(new { error = "Không tìm thấy hội thoại." });
        var staffId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var staff = await db.Users.AsNoTracking().SingleAsync(user => user.Id == staffId, ct);
        if (await chat.StaffReplyAsync(session, staff, request.Body ?? "", request.Internal, ct) is { } error)
            return BadRequest(new { error });
        return Ok(new { ok = true });
    }

    [HttpPost("/api/chat/admin/sessions/{id:int}/status")]
    public async Task<IActionResult> Status(int id, [FromBody] ChatStatusRequest request, CancellationToken ct)
    {
        var session = await db.ChatSessions.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (session is null) return NotFound(new { error = "Không tìm thấy hội thoại." });
        if (await chat.SetStatusAsync(session, request.Status ?? "", ct) is { } error) return BadRequest(new { error });
        return Ok(new { ok = true });
    }

    private async Task<IReadOnlyList<ChatSessionRow>> ListAsync(string? status, CancellationToken ct)
    {
        var sessions = db.ChatSessions.AsNoTracking();
        sessions = status switch
        {
            "bot" => sessions.Where(session => session.Mode == ChatModes.Bot && session.Status != ChatStatuses.Closed),
            { Length: > 0 } => sessions.Where(session => session.Status == status),
            _ => sessions
        };
        // SQLite cannot order by DateTimeOffset: the newest message id stands in for "last activity".
        var rows = await sessions
            .Select(session => new
            {
                Session = session,
                LastId = session.Messages.Where(message => !message.IsInternal).Max(message => (int?)message.Id) ?? 0,
                Count = session.Messages.Count(message => !message.IsInternal)
            })
            .Where(row => row.Count > 0)
            .OrderByDescending(row => row.LastId).Take(100).ToListAsync(ct);
        var lastIds = rows.Select(row => row.LastId).ToList();
        var previews = await db.ChatMessages.AsNoTracking().Where(message => lastIds.Contains(message.Id))
            .ToDictionaryAsync(message => message.Id, ct);
        var ticketIds = rows.Where(row => row.Session.TicketId != null).Select(row => row.Session.TicketId!.Value).ToList();
        var tickets = await db.Tickets.AsNoTracking().Where(ticket => ticketIds.Contains(ticket.Id))
            .ToDictionaryAsync(ticket => ticket.Id, ticket => ticket.TicketNumber, ct);
        return rows.Select(row =>
        {
            previews.TryGetValue(row.LastId, out var last);
            return new ChatSessionRow(row.Session.Id, row.Session.ContactEmail ?? "Khách chưa để lại email", row.Session.Mode, row.Session.Status,
                DisplayText.ChatStatus(row.Session.Status, row.Session.Mode), row.Session.Urgent,
                row.Session.TicketId is { } tid && tickets.TryGetValue(tid, out var number) ? number : null,
                last is null ? "" : (last.Body.Length > 90 ? last.Body[..90] + "…" : last.Body),
                last?.Sender ?? "", (last?.CreatedAt ?? row.Session.CreatedAt).ToOffset(VietnamOffset).ToString("HH:mm dd/MM"), row.Count);
        }).ToList();
    }

    private async Task<IReadOnlyDictionary<string, int>> CountsAsync(CancellationToken ct)
    {
        var counts = await db.ChatSessions.AsNoTracking().Where(session => session.Messages.Any())
            .GroupBy(session => session.Status).Select(group => new { group.Key, Count = group.Count() }).ToListAsync(ct);
        return counts.ToDictionary(item => item.Key, item => item.Count);
    }
}
