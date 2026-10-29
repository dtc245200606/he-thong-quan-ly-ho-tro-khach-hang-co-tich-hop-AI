using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CustomerSupportAI.Services;

/// <summary>A system prompt loaded once from the Prompts folder (chatbot_system.txt by default).</summary>
public sealed class ChatbotPrompt
{
    public string Text { get; }
    public ChatbotPrompt(string text) => Text = text;
    public static ChatbotPrompt Load(string contentRoot, string fileName = "chatbot_system.txt")
    {
        foreach (var root in new[] { contentRoot, AppContext.BaseDirectory })
        {
            var path = Path.Combine(root, "Prompts", fileName);
            if (File.Exists(path)) return new(File.ReadAllText(path).Trim());
        }
        throw new InvalidOperationException($"Missing Prompts/{fileName} (system prompt).");
    }
}

/// <summary>What the chat widget receives after a customer message.</summary>
public sealed record ChatTurnResult(IReadOnlyList<ChatMessage> Messages, bool NeedsEmail);

/// <summary>
/// Orchestrates the website chatbot: sessions, the AI tool loop, the four business tools, handoff to staff and
/// mirroring between the chat and the linked ticket. The AI never touches the database: it can only ask for one of
/// the tools below, and every tool checks its arguments and the caller's right to the data before acting.
/// </summary>
public sealed class ChatbotService(
    AppDbContext db,
    IEnumerable<IChatProvider> providers,
    ChatbotSettingsStore settings,
    IOptions<ChatbotOptions> options,
    IPasswordHasher<SupportUser> hasher,
    ChatbotPrompt prompt,
    IOptions<SlaOptions> sla,
    EmailSender emailSender,
    IDataProtectionProvider protection,
    IConfiguration configuration,
    LoginThrottle throttle,
    IHttpContextAccessor httpContext,
    ILogger<ChatbotService> logger)
{
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    private const int MaxVerificationSendsPerSession = 3;
    private readonly ITimeLimitedDataProtector verifier = protection.CreateProtector("CustomerSupport.ChatEmail.v1").ToTimeLimitedDataProtector();
    public static readonly string[] Categories = ["Product support", "Billing", "Warranty & repair", "Account access", "Other"];
    // Keep Vietnamese readable in tool results (model input only, never rendered as HTML).
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private const string NotFoundMessage = "Không tìm thấy yêu cầu phù hợp với mã và email đã cung cấp. Vui lòng kiểm tra lại mã yêu cầu.";
    private ChatbotOptions Limits => options.Value;

    public static readonly IReadOnlyList<ChatToolDefinition> Tools =
    [
        new("search_faq", "Tìm trong kho hướng dẫn (FAQ) đang hoạt động. Dùng cho mọi câu hỏi về sản phẩm, dịch vụ, tài khoản, thanh toán, bảo hành và quy trình hỗ trợ.",
            Props(("query", "string", "Câu hỏi hoặc từ khóa cần tìm, tiếng Việt.")), ["query"]),
        new("get_ticket_status", "Tra cứu trạng thái một yêu cầu hỗ trợ của chính khách hàng đang chat. Hệ thống tự xác minh quyền sở hữu bằng email của phiên.",
            Props(("ticket_id", "string", "Mã yêu cầu dạng CS-...")), ["ticket_id"]),
        new("create_ticket", "Tạo yêu cầu hỗ trợ mới từ nội dung hội thoại. Cần email của khách (hệ thống tự kiểm tra).",
            Props(("title", "string", "Tiêu đề ngắn, 5-120 ký tự."), ("description", "string", "Mô tả đầy đủ vấn đề, 10-4000 ký tự."),
                ("category", "string", "Một trong: Product support, Billing, Warranty & repair, Account access, Other."),
                ("priority", "string", "Một trong: Thấp, Trung bình, Cao, Khẩn cấp.")), ["title", "description", "category", "priority"]),
        new("handoff_to_human", "Chuyển cuộc trò chuyện cho nhân viên hỗ trợ. Hệ thống tạo hoặc dùng lại yêu cầu đang mở. Cần email của khách.",
            Props(("reason", "string", "Lý do chuyển, 3-300 ký tự."), ("urgent", "boolean", "true nếu khẩn cấp hoặc khách rất bức xúc.")), ["reason", "urgent"])
    ];

    // ---------------------------------------------------------------- sessions

    public async Task<(ChatSession Session, string Token)> CreateSessionAsync(int? userId, CancellationToken ct)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var session = new ChatSession
        {
            PublicId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            TokenHash = Hash(token),
            UserId = userId
        };
        if (userId is { } id)
            session.ContactEmail = await db.Users.Where(user => user.Id == id).Select(user => user.Email).SingleOrDefaultAsync(ct);
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return (session, token);
    }

    /// <summary>The session only when the token matches and it belongs to the same signed-in user (or both are anonymous).</summary>
    public async Task<ChatSession?> FindSessionAsync(string publicId, string? token, int? userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || publicId.Length != 32) return null;
        var session = await db.ChatSessions.SingleOrDefaultAsync(item => item.PublicId == publicId, ct);
        if (session is null) return null;
        var valid = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(session.TokenHash), Encoding.ASCII.GetBytes(Hash(token)));
        return valid && session.UserId == userId ? session : null;
    }

    /// <summary>Sets the guest's contact email. Signed-in sessions use the account email; a session with a ticket keeps its email.</summary>
    public async Task<string?> UpdateContactAsync(ChatSession session, string email, CancellationToken ct)
    {
        email = email.Trim();
        if (!PiiRedactor.IsValidEmail(email)) return "Email không hợp lệ.";
        if (session.UserId is not null) return "Phiên đăng nhập dùng email của tài khoản.";
        if (session.TicketId is not null && !string.Equals(session.ContactEmail, email, StringComparison.OrdinalIgnoreCase))
            return "Phiên này đã gắn với một yêu cầu hỗ trợ nên không đổi được email.";
        await SetContactEmailAsync(session, email, ct);
        return null;
    }

    /// <summary>Signed-in customers are verified by their account; guests by the emailed confirmation link.</summary>
    public static bool IsVerified(ChatSession session) => session.UserId is not null || session.EmailVerified;

    /// <summary>Stores a guest email as unverified and emails a confirmation link (re-sent when the same unverified email is entered again).</summary>
    private async Task SetContactEmailAsync(ChatSession session, string email, CancellationToken ct)
    {
        var same = string.Equals(session.ContactEmail, email, StringComparison.OrdinalIgnoreCase);
        if (same && session.EmailVerified) return;
        session.ContactEmail = email;
        session.EmailVerified = false;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        Add(session, ChatSenders.System, await SendVerificationAsync(session, email)
            ? $"Đã gửi liên kết xác nhận tới {Mask(email)}. Vui lòng mở email và bấm liên kết (hiệu lực 24 giờ) để tra cứu, tạo yêu cầu hoặc gặp nhân viên."
            : "Chưa gửi được email xác nhận (đã gửi quá số lần cho phép hoặc hệ thống chưa cấu hình email). Bạn có thể đăng nhập tài khoản để tiếp tục, hoặc thử lại sau.");
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> SendVerificationAsync(ChatSession session, string email)
    {
        var context = httpContext.HttpContext;
        // Per IP: at most 5 confirmation emails per 15 minutes, on top of the per-session cap.
        var throttleKey = context is null ? null : LoginThrottle.Key(context, "chat-email-verify");
        if (session.VerificationSends >= MaxVerificationSendsPerSession || !emailSender.IsAvailable ||
            (throttleKey is not null && throttle.IsBlocked(throttleKey)))
            return false;
        if (throttleKey is not null) throttle.RecordFailure(throttleKey);
        session.VerificationSends++;
        var token = verifier.Protect($"{session.Id}|{email}", VerificationLifetime);
        var configured = configuration["App:BaseUrl"];
        var baseUrl = !string.IsNullOrWhiteSpace(configured) ? configured.TrimEnd('/')
            : context is null ? "http://localhost" : $"{context.Request.Scheme}://{context.Request.Host}";
        var body = "Xin chào,\n\nBạn vừa nhập email này trong cửa sổ trò chuyện hỗ trợ của CustomerSupport.\n" +
            $"Mở liên kết sau trong vòng 24 giờ để xác nhận email và tiếp tục cuộc trò chuyện:\n\n{baseUrl}/Chat/Verify?token={Uri.EscapeDataString(token)}\n\n" +
            "Nếu không phải bạn, hãy bỏ qua email này. Chúng tôi không bao giờ hỏi mật khẩu hay mã OTP.\n\nCustomerSupport";
        // Sent in the background like password-reset mail, so a slow SMTP server does not stall the chat.
        _ = Task.Run(async () =>
        {
            if (!await emailSender.SendAsync(email, "Xác nhận email cho cuộc trò chuyện hỗ trợ", body, CancellationToken.None))
                logger.LogWarning("Chat confirmation email could not be delivered.");
        });
        return true;
    }

    /// <summary>Confirms the email of the session named in the token, if the session still uses that email.</summary>
    public async Task<bool> VerifyEmailAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        string[] parts;
        try { parts = verifier.Unprotect(token, out _).Split('|', 2); }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
        if (parts.Length != 2 || !int.TryParse(parts[0], out var sessionId)) return false;
        var session = await db.ChatSessions.SingleOrDefaultAsync(item => item.Id == sessionId, ct);
        if (session is null || !string.Equals(session.ContactEmail, parts[1], StringComparison.OrdinalIgnoreCase)) return false;
        if (!session.EmailVerified)
        {
            session.EmailVerified = true;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            Add(session, ChatSenders.System, "Email đã được xác nhận. Bạn có thể tiếp tục yêu cầu của mình trong khung chat.");
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    public static string? Mask(string? email)
    {
        if (string.IsNullOrEmpty(email)) return null;
        var at = email.IndexOf('@');
        return at <= 0 ? null : email[..Math.Min(2, at)] + "***" + email[at..];
    }

    /// <summary>Public (non-internal) messages after <paramref name="afterId"/>, after pulling new ticket replies into the chat.</summary>
    public async Task<IReadOnlyList<ChatMessage>> PublicMessagesAsync(ChatSession session, int afterId, CancellationToken ct)
    {
        await MirrorTicketAsync(session, ct);
        return await db.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == session.Id && message.Id > afterId && !message.IsInternal)
            .OrderBy(message => message.Id).Take(200).ToListAsync(ct);
    }

    // ---------------------------------------------------------------- customer message

    public async Task<ChatTurnResult> HandleCustomerMessageAsync(ChatSession session, string text, CancellationToken ct)
    {
        text = text.Trim();
        var firstNewId = await NextMessageIdAsync(session, ct);
        await MirrorTicketAsync(session, ct);
        var customerMessage = Add(session, ChatSenders.Customer, text);
        if (session.UserId is null && session.TicketId is null && PiiRedactor.FindEmail(text) is { } email)
            await SetContactEmailAsync(session, email, ct);

        var ticket = session.TicketId is { } ticketId ? await db.Tickets.SingleOrDefaultAsync(item => item.Id == ticketId, ct) : null;
        if (session.Mode == ChatModes.Human && ticket is { Status: "Closed" })
        {
            session.Mode = ChatModes.Bot;
            session.Status = ChatStatuses.Active;
            Add(session, ChatSenders.System, $"Yêu cầu {ticket.TicketNumber} đã đóng. Trợ lý tự động tiếp tục hỗ trợ bạn; bạn có thể yêu cầu gặp nhân viên lần nữa nếu cần.");
        }

        var needsEmail = false;
        if (session.Mode == ChatModes.Human)
        {
            // Staff are handling the conversation: store the message on the ticket, never answer automatically.
            if (ticket is not null)
            {
                var ticketMessage = new TicketMessage { TicketId = ticket.Id, AuthorId = ticket.CustomerId, Body = Clip(text, 4000) };
                db.TicketMessages.Add(ticketMessage);
                ticket.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                customerMessage.TicketMessageId = ticketMessage.Id;
                session.LastTicketMessageId = Math.Max(session.LastTicketMessageId, ticketMessage.Id);
            }
            session.Status = ChatStatuses.WaitingStaff;
        }
        else
        {
            if (session.Status == ChatStatuses.Closed) session.Status = ChatStatuses.Active;
            await db.SaveChangesAsync(ct);
            needsEmail = await RunBotAsync(session, customerMessage, ct);
        }
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var created = await db.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == session.Id && message.Id >= firstNewId && !message.IsInternal)
            .OrderBy(message => message.Id).ToListAsync(ct);
        return new(created, needsEmail);
    }

    /// <summary>Handoff without the AI (the widget's "Gặp nhân viên" button), so it works even when every provider is down.</summary>
    public async Task<ChatTurnResult> RequestHandoffAsync(ChatSession session, CancellationToken ct)
    {
        var firstNewId = await NextMessageIdAsync(session, ct);
        var outcome = await HandoffAsync(session, "Khách hàng bấm nút gặp nhân viên trong cửa sổ chat.", false, ct);
        if (!outcome.Ok)
            Add(session, ChatSenders.Bot, outcome.Error switch
            {
                "email_required" => "Vui lòng nhập email liên hệ để nhân viên có thể phản hồi bạn, sau đó bấm \"Gặp nhân viên\" lần nữa.",
                "email_unverified" => "Vui lòng mở email và bấm liên kết xác nhận chúng tôi vừa gửi, sau đó bấm \"Gặp nhân viên\" lần nữa.",
                _ => outcome.Message
            });
        await db.SaveChangesAsync(ct);
        var created = await db.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == session.Id && message.Id >= firstNewId && !message.IsInternal)
            .OrderBy(message => message.Id).ToListAsync(ct);
        return new(created, outcome.Error == "email_required");
    }

    private async Task<bool> RunBotAsync(ChatSession session, ChatMessage current, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var current0 = PiiRedactor.Redact(current.Body);
        var redacted = current0.Count;
        var transcript = await HistoryAsync(session, current.Id, ct);
        if (transcript.Count > 0 && transcript[^1] is ChatUserItem pending)
            transcript[^1] = new ChatUserItem(pending.Text + "\n" + current0.Text);
        else
            transcript.Add(new ChatUserItem(current0.Text));

        var saved = settings.Read();
        var primary = Resolve(saved);
        var log = new AiLog { SessionId = session.Id, Provider = primary?.Name ?? "none" };
        var turn = new ToolLoopTrace();
        Task<ToolLoopOutcome> Run(IChatProvider provider) => ChatToolLoop.RunAsync(provider, prompt.Text, transcript, Tools, Limits.MaxToolRounds,
            "Tôi chưa thể hoàn tất yêu cầu này một cách tự động. Bạn vui lòng mô tả ngắn gọn hơn hoặc nhắn \"gặp nhân viên\" để được hỗ trợ trực tiếp.",
            async call => { var r = await ExecuteToolAsync(session, call, ct); return new(r.Ok, r.Json, r.Error); }, turn, logger, ct);
        var outcome = primary is null
            ? new ToolLoopOutcome(null, "config", "Chưa cấu hình nhà cung cấp AI cho chatbot.")
            : await Run(primary);

        if (outcome.Failure is not null && saved.FallbackToMock && primary?.Name != "mock")
        {
            logger.LogWarning("Chatbot provider {Provider} failed ({Failure}); using mock fallback.", primary?.Name ?? "none", outcome.Failure);
            log.FallbackUsed = true;
            log.Error = Clip($"{primary?.Name ?? "none"}: {outcome.Error}", 300);
            outcome = await Run(providers.First(provider => provider.Name == "mock"));
        }
        var dataSaved = turn.Executions.Any(e => e.Result.Ok && e.Tool is "create_ticket" or "handoff_to_human");
        var needsEmail = turn.Executions.Any(e => e.Result.Error == "email_required");

        string reply;
        if (outcome.Failure is not null)
        {
            reply = "Xin lỗi, trợ lý tự động tạm thời không khả dụng. Bạn có thể thử lại sau ít phút hoặc bấm \"Gặp nhân viên\" để được nhân viên hỗ trợ.";
            if (log.Error.Length == 0) log.Error = Clip(outcome.Error ?? outcome.Failure, 300);
        }
        else
        {
            var guarded = ChatReplyGuard.Check(outcome.Text!, dataSaved);
            reply = guarded.Text;
            if (guarded.Blocked) log.Error = Clip((log.Error.Length > 0 ? log.Error + "; " : "") + "reply replaced by safety check", 300);
        }
        var botMessage = Add(session, ChatSenders.Bot, Clip(reply, 4000));
        botMessage.AuthorName = outcome.Failure is null ? (log.FallbackUsed ? "mock-fallback" : primary!.Name) : "error";

        log.Success = outcome.Failure is null;
        log.ToolRounds = turn.Rounds;
        log.ToolCalls = Clip(string.Join("; ", turn.Calls), 1000);
        log.RedactedCount = redacted + turn.Redacted;
        log.DurationMs = (int)watch.ElapsedMilliseconds;
        db.AiLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return needsEmail;
    }

    private IChatProvider? Resolve(ChatbotSettings saved)
    {
        IChatProvider? Named(string name) => providers.FirstOrDefault(provider => provider.Name == name && provider.IsConfigured);
        return saved.Provider == "auto" ? Named("groq") ?? Named("gemini") ?? Named("mock") : Named(saved.Provider);
    }

    /// <summary>Earlier public messages of this session as alternating, redacted user/assistant turns.</summary>
    private async Task<List<ChatItem>> HistoryAsync(ChatSession session, int beforeId, CancellationToken ct)
    {
        var rows = (await db.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == session.Id && message.Id < beforeId && !message.IsInternal && message.Sender != ChatSenders.System)
            .OrderByDescending(message => message.Id).Take(Limits.HistoryMessages).ToListAsync(ct)).AsEnumerable().Reverse();
        var items = new List<ChatItem>();
        foreach (var row in rows)
        {
            var isUser = row.Sender == ChatSenders.Customer;
            var body = PiiRedactor.Redact(row.Sender == ChatSenders.Staff ? "[Nhân viên hỗ trợ] " + row.Body : row.Body).Text;
            if (items.Count == 0 && !isUser) continue;
            if (items.Count > 0 && (items[^1] is ChatUserItem) == isUser)
                items[^1] = isUser ? new ChatUserItem(((ChatUserItem)items[^1]).Text + "\n" + body)
                    : new ChatAssistantItem(((ChatAssistantItem)items[^1]).Text + "\n" + body, []);
            else
                items.Add(isUser ? new ChatUserItem(body) : new ChatAssistantItem(body, []));
        }
        return items;
    }

    // ---------------------------------------------------------------- tools

    public sealed record ToolResult(bool Ok, string Json, string? Error, string Message);

    private static ToolResult Result(bool ok, string? error, string message, object? data = null)
    {
        var payload = new Dictionary<string, object?> { ["ok"] = ok, ["message"] = message };
        if (error is not null) payload["error"] = error;
        if (data is not null)
            foreach (var property in JsonSerializer.SerializeToElement(data).EnumerateObject())
                payload[property.Name] = property.Value;
        return new(ok, JsonSerializer.Serialize(payload, JsonOptions), error, message);
    }

    private static ToolResult Invalid(string message) => Result(false, "invalid_argument", message);

    private static ToolResult EmailRequired() =>
        Result(false, "email_required", "Cần email liên hệ của khách hàng. Hãy xin khách cung cấp email (không cần mật khẩu).");

    private static ToolResult EmailUnverified() =>
        Result(false, "email_unverified", "Email của khách chưa được xác nhận. Hệ thống đã gửi liên kết xác nhận tới email đó; hãy nhờ khách mở email, bấm liên kết rồi gửi lại yêu cầu. Không hỏi mã hay mật khẩu.");

    /// <summary>Validates the arguments and the caller's rights, then runs one tool. Never throws for bad input.</summary>
    public async Task<ToolResult> ExecuteToolAsync(ChatSession session, ChatToolCall call, CancellationToken ct)
    {
        if (call.Arguments.ValueKind != JsonValueKind.Object) return Invalid("Tham số công cụ phải là một đối tượng JSON.");
        try
        {
            return call.Name switch
            {
                "search_faq" => await SearchFaqAsync(Str(call.Arguments, "query"), ct),
                "get_ticket_status" => await TicketStatusAsync(session, Str(call.Arguments, "ticket_id"), ct),
                "create_ticket" => await CreateTicketAsync(session, Str(call.Arguments, "title"), Str(call.Arguments, "description"),
                    Str(call.Arguments, "category"), Str(call.Arguments, "priority"), ct),
                "handoff_to_human" => await HandoffAsync(session, Str(call.Arguments, "reason"),
                    call.Arguments.TryGetProperty("urgent", out var urgent) && urgent.ValueKind == JsonValueKind.True, ct),
                _ => Result(false, "unknown_tool", "Công cụ không tồn tại hoặc không được phép.")
            };
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Chat tool {Tool} failed to save", call.Name);
            db.ChangeTracker.Clear();
            db.Attach(session);
            return Result(false, "save_failed", "Hệ thống chưa lưu được dữ liệu. Không được báo thành công; hãy đề nghị khách thử lại hoặc gặp nhân viên.");
        }
    }

    private async Task<ToolResult> SearchFaqAsync(string query, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 500) return Invalid("query phải dài từ 2 đến 500 ký tự.");
        var articles = await db.KnowledgeArticles.AsNoTracking().ToListAsync(ct);
        var matches = KnowledgeSearch.Find(articles, query);
        return Result(true, null, matches.Count == 0 ? "Không có bài hướng dẫn phù hợp." : $"Tìm thấy {matches.Count} bài hướng dẫn.", new
        {
            results = matches.Select(article => new
            {
                title = article.Title,
                category = article.Category,
                content = Clip(article.Body, 1500),
                url = $"/Knowledge/Details/{article.Id}"
            })
        });
    }

    private async Task<ToolResult> TicketStatusAsync(ChatSession session, string ticketNumber, CancellationToken ct)
    {
        ticketNumber = ticketNumber.Trim().ToUpperInvariant();
        if (ticketNumber.Length is < 4 or > 24 || !ticketNumber.StartsWith("CS-", StringComparison.Ordinal))
            return Invalid("ticket_id phải là mã yêu cầu dạng CS-...");
        if (session.UserId is null && string.IsNullOrEmpty(session.ContactEmail)) return EmailRequired();
        if (!IsVerified(session)) return EmailUnverified();
        var ticket = await OwnedTickets(session).AsNoTracking().SingleOrDefaultAsync(item => item.TicketNumber == ticketNumber, ct);
        // Same answer for "does not exist" and "belongs to someone else": the code alone reveals nothing.
        if (ticket is null) return Result(false, "not_found", NotFoundMessage);
        return Result(true, null, "Đã tìm thấy yêu cầu của khách hàng.", new
        {
            ticket_number = ticket.TicketNumber,
            subject = ticket.Subject,
            status = ticket.Status,
            status_text = DisplayText.TicketStatus(ticket.Status),
            category_text = DisplayText.TicketCategory(ticket.Category),
            updated_at = ticket.UpdatedAt.ToOffset(VietnamOffset).ToString("HH:mm dd/MM/yyyy")
        });
    }

    private async Task<ToolResult> CreateTicketAsync(ChatSession session, string title, string description, string category, string priority, CancellationToken ct)
    {
        title = title.Trim();
        description = description.Trim();
        var normalizedCategory = Categories.FirstOrDefault(item => string.Equals(item, category.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? Categories.FirstOrDefault(item => string.Equals(DisplayText.TicketCategory(item), category.Trim(), StringComparison.OrdinalIgnoreCase));
        var normalizedPriority = TicketAiService.Priorities.FirstOrDefault(item => string.Equals(item, priority.Trim(), StringComparison.OrdinalIgnoreCase));
        if (title.Length is < 5 or > 120) return Invalid("title phải dài từ 5 đến 120 ký tự.");
        if (description.Length is < 10 or > 4000) return Invalid("description phải dài từ 10 đến 4000 ký tự.");
        if (normalizedCategory is null) return Invalid("category không hợp lệ. Chọn một trong: " + string.Join(", ", Categories) + ".");
        if (normalizedPriority is null) return Invalid("priority không hợp lệ. Chọn một trong: " + string.Join(", ", TicketAiService.Priorities) + ".");

        // One ticket per chat: a repeated call returns the open ticket instead of creating a duplicate.
        if (await OpenSessionTicketAsync(session, ct) is { } existing)
            return Result(true, null, $"Phiên này đã có yêu cầu {existing.TicketNumber}; không tạo trùng.",
                new { ticket_number = existing.TicketNumber, reused = true });

        var customer = await ResolveCustomerAsync(session, ct);
        if (customer.Error is not null) return customer.Error;
        var ticket = NewTicket(customer.Id, title, normalizedCategory, normalizedPriority,
            description + "\n\n(Tạo từ cửa sổ chat của trợ lý tự động.)");
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        Link(session, ticket);
        await db.SaveChangesAsync(ct);
        return Result(true, null, $"Đã tạo yêu cầu {ticket.TicketNumber}.",
            new { ticket_number = ticket.TicketNumber, reused = false, status_text = DisplayText.TicketStatus(ticket.Status) });
    }

    private async Task<ToolResult> HandoffAsync(ChatSession session, string reason, bool urgent, CancellationToken ct)
    {
        reason = reason.Trim();
        if (reason.Length is < 3 or > 300) return Invalid("reason phải dài từ 3 đến 300 ký tự.");
        if (session.Mode == ChatModes.Human && await OpenSessionTicketAsync(session, ct) is { } handled)
            return Result(true, null, $"Cuộc trò chuyện đã được chuyển cho nhân viên (yêu cầu {handled.TicketNumber}).",
                new { ticket_number = handled.TicketNumber, reused = true });

        var transcript = await TranscriptAsync(session, ct);
        var ticket = await OpenSessionTicketAsync(session, ct);
        if (ticket is null)
        {
            var customer = await ResolveCustomerAsync(session, ct);
            if (customer.Error is not null) return customer.Error;
            ticket = NewTicket(customer.Id, Clip("Chuyển nhân viên: " + reason, 120), "Other", urgent ? "Khẩn cấp" : "Trung bình",
                $"Khách hàng cần nhân viên hỗ trợ qua chat.\nLý do: {reason}\n\nHội thoại gần đây:\n{transcript}");
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync(ct);
            Link(session, ticket);
        }
        else
        {
            var message = new TicketMessage { TicketId = ticket.Id, AuthorId = ticket.CustomerId,
                Body = Clip($"Khách hàng yêu cầu chuyển nhân viên qua chat.\nLý do: {reason}\n\nHội thoại gần đây:\n{transcript}", 4000) };
            db.TicketMessages.Add(message);
            if (urgent && ticket.Priority != "Khẩn cấp")
            {
                ticket.Priority = "Khẩn cấp";
                ticket.SlaDueAt = SlaPolicy.DueAt(ticket.CreatedAt, ticket.Priority, sla.Value);
            }
            if (ticket.Status == "Resolved") ticket.Status = "Open";
            ticket.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            session.LastTicketMessageId = Math.Max(session.LastTicketMessageId, message.Id);
        }
        session.Mode = ChatModes.Human;
        session.Status = ChatStatuses.WaitingStaff;
        session.HandoffReason = Clip(reason, 300);
        session.Urgent |= urgent;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        Add(session, ChatSenders.System,
            $"Cuộc trò chuyện đã được chuyển cho nhân viên hỗ trợ (yêu cầu {ticket.TicketNumber}). Nhân viên sẽ trả lời ngay trong khung chat này.");
        await db.SaveChangesAsync(ct);
        return Result(true, null, $"Đã chuyển cho nhân viên với yêu cầu {ticket.TicketNumber}.",
            new { ticket_number = ticket.TicketNumber, urgent });
    }

    private IQueryable<SupportTicket> OwnedTickets(ChatSession session)
    {
        if (session.UserId is { } userId) return db.Tickets.Where(ticket => ticket.CustomerId == userId);
        var normalized = (session.ContactEmail ?? "").ToUpperInvariant();
        return db.Tickets.Where(ticket => normalized.Length > 0 && ticket.Customer.NormalizedEmail == normalized);
    }

    private async Task<SupportTicket?> OpenSessionTicketAsync(ChatSession session, CancellationToken ct) =>
        session.TicketId is { } id
            ? await db.Tickets.SingleOrDefaultAsync(ticket => ticket.Id == id && ticket.Status != "Closed", ct)
            : null;

    /// <summary>
    /// The customer account the chat ticket belongs to: the signed-in customer, or the account with the session email.
    /// A guest email without an account gets a new customer account with an unusable random password; the person
    /// can claim it later with "Quên mật khẩu". Staff/admin or locked accounts are refused.
    /// </summary>
    private async Task<(int Id, ToolResult? Error)> ResolveCustomerAsync(ChatSession session, CancellationToken ct)
    {
        if (session.UserId is { } userId)
        {
            var self = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, ct);
            return self is { Role: UserRoles.Customer, IsActive: true }
                ? (self.Id, null)
                : (0, Result(false, "not_allowed", "Chỉ tài khoản khách hàng mới tạo được yêu cầu qua chat."));
        }
        if (!PiiRedactor.IsValidEmail(session.ContactEmail)) return (0, EmailRequired());
        if (!session.EmailVerified) return (0, EmailUnverified());
        var email = session.ContactEmail!.Trim();
        var normalized = email.ToUpperInvariant();
        var existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.NormalizedEmail == normalized, ct);
        if (existing is not null)
            return existing is { Role: UserRoles.Customer, IsActive: true }
                ? (existing.Id, null)
                : (0, Result(false, "not_allowed", "Không thể tạo yêu cầu với email này qua chat. Vui lòng đăng nhập hoặc dùng email khác."));
        var localPart = email.Split('@')[0];
        var created = new SupportUser
        {
            DisplayName = Clip(localPart.Length > 0 ? localPart : "Khách hàng chat", 100),
            Email = email,
            NormalizedEmail = normalized,
            Role = UserRoles.Customer
        };
        created.PasswordHash = hasher.HashPassword(created, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        db.Users.Add(created);
        db.AuditLogs.Add(new AuditLog { ActorName = "Chatbot", Action = "chat.customer-create", Target = Clip(email, 320),
            Detail = "Tạo tài khoản khách hàng từ email trong cửa sổ chat (chưa đặt mật khẩu)." });
        await db.SaveChangesAsync(ct);
        return (created.Id, null);
    }

    private SupportTicket NewTicket(int customerId, string subject, string category, string priority, string body)
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = new SupportTicket
        {
            CreatedAt = now,
            UpdatedAt = now,
            SlaDueAt = SlaPolicy.DueAt(now, priority, sla.Value),
            TicketNumber = $"CS-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            Subject = Clip(subject, 120),
            Category = category,
            Priority = priority,
            Channel = "chat",
            Status = "Open",
            CustomerId = customerId
        };
        ticket.Messages.Add(new TicketMessage { AuthorId = customerId, Body = Clip(body, 4000) });
        return ticket;
    }

    private static void Link(ChatSession session, SupportTicket ticket)
    {
        session.TicketId = ticket.Id;
        session.LastTicketMessageId = Math.Max(session.LastTicketMessageId, ticket.Messages.Max(message => message.Id));
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<string> TranscriptAsync(ChatSession session, CancellationToken ct)
    {
        var rows = (await db.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == session.Id && !message.IsInternal && message.Sender != ChatSenders.System)
            .OrderByDescending(message => message.Id).Take(12).ToListAsync(ct)).AsEnumerable().Reverse();
        var text = string.Join("\n", rows.Select(row =>
            $"{(row.Sender == ChatSenders.Customer ? "Khách hàng" : row.Sender == ChatSenders.Staff ? "Nhân viên" : "Trợ lý")}: {Clip(row.Body, 400)}"));
        return Clip(text.Length == 0 ? "(chưa có)" : text, 3200);
    }

    // ---------------------------------------------------------------- staff side

    /// <summary>Staff reply from the chat console. Public replies go to the customer (and the linked ticket); notes stay internal.</summary>
    public async Task<string?> StaffReplyAsync(ChatSession session, SupportUser staff, string body, bool isInternal, CancellationToken ct)
    {
        body = body.Trim();
        if (body.Length is < 1 or > 4000) return "Nội dung phải từ 1 đến 4.000 ký tự.";
        var message = Add(session, ChatSenders.Staff, body);
        message.AuthorId = staff.Id;
        message.AuthorName = Clip(staff.DisplayName, 100);
        message.IsInternal = isInternal;
        if (!isInternal)
        {
            await MirrorTicketAsync(session, ct);
            var ticket = await OpenSessionTicketAsync(session, ct);
            if (ticket is not null)
            {
                var ticketMessage = new TicketMessage { TicketId = ticket.Id, AuthorId = staff.Id, Body = body };
                db.TicketMessages.Add(ticketMessage);
                if (ticket.Status == "Open") ticket.Status = "In progress";
                ticket.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                message.TicketMessageId = ticketMessage.Id;
                session.LastTicketMessageId = Math.Max(session.LastTicketMessageId, ticketMessage.Id);
            }
            // Staff took over: the bot stays silent from now on.
            session.Mode = ChatModes.Human;
            session.Status = ChatStatuses.StaffReplied;
        }
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> SetStatusAsync(ChatSession session, string status, CancellationToken ct)
    {
        if (!ChatStatuses.All.Contains(status)) return "Trạng thái không hợp lệ.";
        session.Status = status;
        if (status == ChatStatuses.Closed) Add(session, ChatSenders.System, "Nhân viên đã kết thúc cuộc trò chuyện. Cảm ơn bạn đã liên hệ.");
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Copies ticket replies written outside the chat (e.g. on the ticket page) into the conversation.</summary>
    public async Task MirrorTicketAsync(ChatSession session, CancellationToken ct)
    {
        if (session.TicketId is not { } ticketId) return;
        var fresh = await db.TicketMessages.AsNoTracking().Include(message => message.Author)
            .Where(message => message.TicketId == ticketId && message.Id > session.LastTicketMessageId)
            .OrderBy(message => message.Id).ToListAsync(ct);
        if (fresh.Count == 0) return;
        var ids = fresh.Select(message => (int?)message.Id).ToList();
        var known = await db.ChatMessages.Where(message => message.SessionId == session.Id && ids.Contains(message.TicketMessageId))
            .Select(message => message.TicketMessageId).ToListAsync(ct);
        foreach (var message in fresh.Where(message => !known.Contains(message.Id)))
        {
            var staff = message.Author.Role != UserRoles.Customer;
            var mirrored = Add(session, staff ? ChatSenders.Staff : ChatSenders.Customer, Clip(message.Body, 4000));
            mirrored.TicketMessageId = message.Id;
            mirrored.AuthorId = message.AuthorId;
            mirrored.AuthorName = Clip(message.Author.DisplayName, 100);
            if (staff)
            {
                session.Mode = ChatModes.Human;
                session.Status = ChatStatuses.StaffReplied;
            }
        }
        session.LastTicketMessageId = fresh[^1].Id;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- helpers

    private ChatMessage Add(ChatSession session, string sender, string body)
    {
        var message = new ChatMessage { SessionId = session.Id, Sender = sender, Body = body };
        db.ChatMessages.Add(message);
        return message;
    }

    private async Task<int> NextMessageIdAsync(ChatSession session, CancellationToken ct) =>
        (await db.ChatMessages.Where(message => message.SessionId == session.Id).MaxAsync(message => (int?)message.Id, ct) ?? 0) + 1;

    internal static IReadOnlyDictionary<string, JsonElement> Props(params (string Name, string Type, string Description)[] fields) =>
        fields.ToDictionary(field => field.Name, field => JsonSerializer.SerializeToElement(new { type = field.Type, description = field.Description }));

    private static string Str(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Clip(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
