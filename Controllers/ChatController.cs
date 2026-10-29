using System.Security.Claims;
using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CustomerSupportAI.Controllers;

// Emptiness and length are checked in the actions (limits come from configuration).
public sealed record ChatSendRequest(string? Message);
public sealed record ChatContactRequest(string? Email);

/// <summary>
/// Public chat API used by wwwroot/js/chat-widget.js. No sign-in needed for FAQ questions; every call after
/// session creation must present the session token (X-Chat-Token), which is checked against the stored hash and
/// the signed-in user. Private data (tickets) is gated inside the tools, never here.
/// </summary>
[ApiController]
[Route("api/chat/sessions")]
public sealed class ChatController(AppDbContext db, ChatbotService chat, IOptions<ChatbotOptions> options) : ControllerBase
{
    public const string TokenHeader = "X-Chat-Token";
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Only customers bind a chat to their account; staff and guests chat anonymously.</summary>
    private int? CustomerId => User.IsInRole(UserRoles.Customer) && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpPost, EnableRateLimiting("chat-session")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var (session, token) = await chat.CreateSessionAsync(CustomerId, ct);
        return Ok(new { id = session.PublicId, token, state = await StateAsync(session, ct), messages = Array.Empty<object>() });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, int after, CancellationToken ct)
    {
        if (await FindAsync(id, ct) is not { } session) return SessionNotFound();
        var messages = await chat.PublicMessagesAsync(session, Math.Max(0, after), ct);
        return Ok(new { id = session.PublicId, state = await StateAsync(session, ct), messages = messages.Select(Dto) });
    }

    [HttpPut("{id}/contact"), EnableRateLimiting("chat-message")]
    public async Task<IActionResult> Contact(string id, ChatContactRequest request, CancellationToken ct)
    {
        if (await FindAsync(id, ct) is not { } session) return SessionNotFound();
        if (await chat.UpdateContactAsync(session, request.Email ?? "", ct) is { } error) return BadRequest(new { error });
        return Ok(new { state = await StateAsync(session, ct) });
    }

    [HttpPost("{id}/messages"), EnableRateLimiting("chat-message")]
    public async Task<IActionResult> Send(string id, ChatSendRequest request, CancellationToken ct)
    {
        var text = request.Message?.Trim() ?? "";
        var max = options.Value.MaxMessageLength;
        if (text.Length == 0 || text.Length > max)
            return BadRequest(new { error = $"Tin nhắn phải từ 1 đến {max:N0} ký tự." });
        if (await FindAsync(id, ct) is not { } session) return SessionNotFound();
        var result = await chat.HandleCustomerMessageAsync(session, text, ct);
        return Ok(new { state = await StateAsync(session, ct), needsEmail = result.NeedsEmail, messages = result.Messages.Select(Dto) });
    }

    /// <summary>Handoff button: runs the handoff tool directly, without the AI.</summary>
    [HttpPost("{id}/handoff"), EnableRateLimiting("chat-message")]
    public async Task<IActionResult> Handoff(string id, CancellationToken ct)
    {
        if (await FindAsync(id, ct) is not { } session) return SessionNotFound();
        var result = await chat.RequestHandoffAsync(session, ct);
        return Ok(new { state = await StateAsync(session, ct), needsEmail = result.NeedsEmail, messages = result.Messages.Select(Dto) });
    }

    private Task<ChatSession?> FindAsync(string id, CancellationToken ct) =>
        chat.FindSessionAsync(id, Request.Headers[TokenHeader].ToString(), CustomerId, ct);

    // Same answer for unknown id, wrong token or another user's session.
    private NotFoundObjectResult SessionNotFound() => NotFound(new { error = "Phiên trò chuyện không tồn tại hoặc đã hết hạn." });

    private async Task<object> StateAsync(ChatSession session, CancellationToken ct)
    {
        var ticketNumber = session.TicketId is { } ticketId
            ? await db.Tickets.Where(ticket => ticket.Id == ticketId).Select(ticket => ticket.TicketNumber).SingleOrDefaultAsync(ct)
            : null;
        return new
        {
            mode = session.Mode,
            status = session.Status,
            signedIn = session.UserId is not null,
            email = ChatbotService.Mask(session.ContactEmail),
            emailVerified = ChatbotService.IsVerified(session),
            ticketNumber
        };
    }

    private static object Dto(ChatMessage message) => new
    {
        id = message.Id,
        sender = message.Sender,
        body = message.Body,
        author = message.Sender == ChatSenders.Staff ? message.AuthorName : null,
        provider = message.Sender == ChatSenders.Bot ? message.AuthorName : null,
        time = message.CreatedAt.ToOffset(VietnamOffset).ToString("HH:mm")
    };
}

/// <summary>Landing page of the confirmation link emailed from the chat widget.</summary>
public sealed class ChatVerifyController(ChatbotService chat) : Controller
{
    [HttpGet("/Chat/Verify")]
    public async Task<IActionResult> Index(string? token, CancellationToken ct) =>
        View(await chat.VerifyEmailAsync(token, ct));
}
