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
public sealed class AssistantController(AppDbContext db, AiAssistantService assistant) : Controller
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> Index() => View(new AssistantPageViewModel(
        (await db.AssistantTurns.AsNoTracking().Where(t => t.UserId == UserId).OrderByDescending(t => t.Id).Take(50).ToListAsync()).AsEnumerable().Reverse().ToList(), assistant.IsConfigured));
    [HttpPost, EnableRateLimiting("assistant")]
    public async Task<IActionResult> Ask([FromBody] AssistantMessageRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { reply = "Vui lòng nhập câu hỏi từ 1 đến 2.000 ký tự." });
        var history = (await db.AssistantTurns.AsNoTracking().Where(t => t.UserId == UserId).OrderByDescending(t => t.Id).Take(6).ToListAsync(cancellationToken)).AsEnumerable().Reverse().ToList();
        var allArticles = await db.KnowledgeArticles.AsNoTracking().ToListAsync(cancellationToken);
        var sources = KnowledgeSearch.ForAssistant(allArticles, request.Message, history.LastOrDefault()?.Question);
        var tickets = await db.Tickets.AsNoTracking().Where(t => t.CustomerId == UserId).OrderByDescending(t => t.Id).Take(8)
            .Select(t => new { t.TicketNumber, t.Subject, t.Category, t.Status }).ToListAsync(cancellationToken);
        var context = KnowledgeSearch.IsSupportQuestion(request.Message) ? string.Join("\n", tickets.Select(t => $"{t.TicketNumber}: {t.Subject}; {DisplayText.TicketCategory(t.Category)}; {DisplayText.TicketStatus(t.Status)}")) : "";
        var answer = await assistant.GetReplyAsync(request.Message.Trim(), context, sources, history, cancellationToken);
        // Deterministic access to the customer's own ticket status even without a provider key.
        if (!assistant.IsConfigured &&
            (KnowledgeSearch.Normalize(request.Message).Contains("trang thai") || request.Message.Contains("CS-", StringComparison.OrdinalIgnoreCase)))
        {
            var mentionedCode = request.Message.Contains("CS-", StringComparison.OrdinalIgnoreCase);
            var matches = mentionedCode ? tickets.Where(t => request.Message.Contains(t.TicketNumber, StringComparison.OrdinalIgnoreCase)).ToList() : tickets;
            answer = new(matches.Count == 0 ? "Chưa tìm thấy yêu cầu phù hợp trong các yêu cầu gần đây của bạn. Hãy kiểm tra tại Yêu cầu hỗ trợ hoặc tạo yêu cầu mới."
                : string.Join("\n", matches.Select(t => $"{t.TicketNumber} — {t.Subject}: {DisplayText.TicketStatus(t.Status)}")), "knowledge", false);
        }
        db.AssistantTurns.Add(new AssistantTurn { UserId = UserId, Question = request.Message.Trim(), Reply = answer.Reply, Mode = answer.Mode });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { reply = answer.Reply, mode = answer.Mode, notice = answer.Notice,
            sources = sources.Select(a => new { title = a.Title, url = Url.Action("Details", "Knowledge", new { id = a.Id }) }) });
    }
    [HttpPost]
    public async Task<IActionResult> Clear()
    {
        await db.AssistantTurns.Where(t => t.UserId == UserId).ExecuteDeleteAsync();
        TempData["Success"] = "Đã bắt đầu hội thoại mới.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, Authorize(Roles = UserRoles.Customer), EnableRateLimiting("assistant")]
    public async Task<IActionResult> Handoff()
    {
        var turns = (await db.AssistantTurns.AsNoTracking().Where(t => t.UserId == UserId).OrderByDescending(t => t.Id).Take(3).ToListAsync()).AsEnumerable().Reverse().ToList();
        if (turns.Count == 0) return RedirectToAction("Create", "Tickets");
        var transcript = string.Join("\n\n", turns.Select(t => $"Khách hàng: {t.Question}\nTrợ lý ({t.Mode}): {t.Reply}"));
        var ticket = new SupportTicket { CustomerId = UserId, Subject = "Hỗ trợ tiếp: " + turns.Last().Question[..Math.Min(95, turns.Last().Question.Length)],
            Category = "Other", TicketNumber = $"CS-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant() };
        var body = "Tôi cần nhân viên hỗ trợ thêm. Cuộc trao đổi gần đây:\n" + transcript;
        ticket.Messages.Add(new TicketMessage { AuthorId = UserId, Body = body[..Math.Min(4000, body.Length)] });
        db.Tickets.Add(ticket); await db.SaveChangesAsync();
        TempData["Success"] = "Đã chuyển cuộc trao đổi thành yêu cầu hỗ trợ cho nhân viên.";
        return RedirectToAction("Details", "Tickets", new { id = ticket.Id });
    }
}
