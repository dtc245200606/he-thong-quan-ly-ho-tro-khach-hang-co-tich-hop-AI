using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CustomerSupportAI.Services;

/// <summary>A CRM record the answer was built from, shown to staff next to the reply.</summary>
public sealed record CrmSource(string Title, string? Url);

public sealed record CrmAnswer(string Reply, bool Success, string? Provider, IReadOnlyList<CrmSource> Sources, string? Notice = null);

/// <summary>System prompt of the staff CRM assistant (Prompts/crm_assistant_system.txt).</summary>
public sealed record CrmPrompt(string Text);

/// <summary>
/// Read-only CRM assistant for staff and admins on the Assistant page. The model can only call the six query tools
/// below; each runs a real database query, returns a small redacted slice (no emails or phone numbers), and none can
/// write. Answers therefore rest on CRM data, and any change the model proposes is left to staff in the normal screens.
/// Uses the same provider choice as the chatbot (Groq, then Gemini); there is no mock fallback because invented
/// statistics would be worse than an honest "AI unavailable".
/// </summary>
public sealed class CrmAssistantService(
    AppDbContext db,
    IEnumerable<IChatProvider> providers,
    ChatbotSettingsStore settings,
    IOptions<ChatbotOptions> options,
    CrmPrompt prompt,
    ILogger<CrmAssistantService> logger)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly string[] OpenStatuses = ["Open", "In progress"];
    private static readonly string[] Statuses = ["Open", "In progress", "Resolved", "Closed"];

    public static readonly IReadOnlyList<ChatToolDefinition> Tools =
    [
        new("crm_overview", "Số liệu tổng hợp: số yêu cầu theo trạng thái/ưu tiên/kênh, yêu cầu mới, chưa phân công, quá hạn SLA, thời gian phản hồi đầu trung bình, số khách hàng.",
            ChatbotService.Props(("days", "integer", "Số ngày gần đây để tính yêu cầu mới và thời gian phản hồi, 1-365, mặc định 30.")), []),
        new("list_tickets", "Liệt kê tối đa 20 yêu cầu, mới nhất trước, theo bộ lọc.",
            ChatbotService.Props(("status", "string", "Open, In progress, Resolved, Closed hoặc open (= Open + In progress). Bỏ trống = tất cả."),
                ("priority", "string", "Thấp, Trung bình, Cao hoặc Khẩn cấp. Bỏ trống = tất cả."),
                ("assignee", "string", "me (của tôi), none (chưa phân công) hoặc một phần tên nhân viên. Bỏ trống = tất cả."),
                ("overdue_only", "boolean", "true để chỉ lấy yêu cầu đang quá hạn SLA phản hồi đầu."),
                ("limit", "integer", "Số dòng, 1-20, mặc định 10.")), []),
        new("ticket_details", "Chi tiết một yêu cầu và 6 tin nhắn gần nhất.",
            ChatbotService.Props(("ticket_number", "string", "Mã yêu cầu dạng CS-...")), ["ticket_number"]),
        new("search_customers", "Tìm khách hàng theo tên, email hoặc số điện thoại (tối đa 10 kết quả).",
            ChatbotService.Props(("query", "string", "Từ khóa, 2-100 ký tự.")), ["query"]),
        new("customer_profile", "Hồ sơ khách hàng và 10 yêu cầu gần nhất của họ.",
            ChatbotService.Props(("customer_id", "integer", "Mã khách hàng (id) từ search_customers.")), ["customer_id"]),
        new("staff_workload", "Số yêu cầu đang mở và đang quá hạn SLA của từng nhân viên đang hoạt động.", ChatbotService.Props(), [])
    ];

    // Same order as the chatbot: the saved choice if it is a real provider, otherwise Groq then Gemini.
    private IReadOnlyList<IChatProvider> Candidates()
    {
        var configured = providers.Where(provider => provider.Name is "groq" or "gemini" && provider.IsConfigured).ToList();
        var preferred = settings.Read().Provider;
        return configured.OrderBy(provider => provider.Name == preferred ? 0 : provider.Name == "groq" ? 1 : 2).ToList();
    }

    public string? ProviderName => Candidates().FirstOrDefault()?.Name;

    public async Task<CrmAnswer> AskAsync(ClaimsPrincipal user, string question, IReadOnlyList<AssistantTurn> history, CancellationToken ct)
    {
        // Permission is enforced here as well as on the controller: only staff and admins may query CRM data.
        if (!(user.IsInRole(UserRoles.Staff) || user.IsInRole(UserRoles.Admin)) ||
            !int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return new("Bạn không có quyền dùng trợ lý CRM.", false, null, []);
        var candidates = Candidates();
        if (candidates.Count == 0)
            return new("Trợ lý CRM cần một nhà cung cấp AI thật (Groq hoặc Gemini). Quản trị viên hãy nhập API key tại Cấu hình AI.", false, null, [],
                "Chưa cấu hình AI cho trợ lý CRM.");

        var watch = Stopwatch.StartNew();
        var transcript = new List<ChatItem>();
        foreach (var turn in history.TakeLast(6))
        {
            transcript.Add(new ChatUserItem(PiiRedactor.Redact(turn.Question).Text));
            transcript.Add(new ChatAssistantItem(PiiRedactor.Redact(turn.Reply).Text, []));
        }
        var asked = PiiRedactor.Redact(question);
        transcript.Add(new ChatUserItem(asked.Text));

        var sources = new List<CrmSource>();
        var trace = new ToolLoopTrace();
        ToolLoopOutcome outcome = new(null, "config", "");
        IChatProvider? used = null;
        string? firstError = null;
        foreach (var provider in candidates)
        {
            used = provider;
            outcome = await ChatToolLoop.RunAsync(provider, prompt.Text, transcript, Tools, options.Value.MaxToolRounds,
                "Câu hỏi cần quá nhiều bước tra cứu. Hãy hỏi cụ thể hơn (ví dụ một mã yêu cầu hoặc một khách hàng).",
                call => ExecuteAsync(call, actorId, sources, ct), trace, logger, ct);
            if (outcome.Failure is null) break;
            firstError ??= $"{provider.Name}: {outcome.Error}";
            logger.LogWarning("CRM assistant provider {Provider} failed ({Failure}).", provider.Name, outcome.Failure);
        }

        db.AiLogs.Add(new AiLog
        {
            Task = "crm", Provider = used?.Name ?? "none", Success = outcome.Failure is null,
            FallbackUsed = firstError is not null && outcome.Failure is null, DurationMs = (int)watch.ElapsedMilliseconds,
            RedactedCount = asked.Count + trace.Redacted, ToolRounds = trace.Rounds,
            ToolCalls = Clip(string.Join("; ", trace.Calls), 1000), Error = Clip(firstError ?? "", 300)
        });
        // Audit who queried what kind of CRM data (tool names only, never the question or the data).
        db.AuditLogs.Add(new AuditLog
        {
            ActorId = actorId, ActorName = Clip(user.Identity?.Name ?? "?", 100), Action = "ai.crm", Target = "Trợ lý CRM",
            Detail = Clip(trace.Calls.Count == 0 ? "Không tra cứu dữ liệu" : string.Join("; ", trace.Calls), 500)
        });
        await db.SaveChangesAsync(ct);

        if (outcome.Failure is not null)
            return new("Trợ lý CRM tạm thời không trả lời được. Vui lòng thử lại sau ít phút.", false, used?.Name, [],
                outcome.Error ?? "Nhà cung cấp AI gặp lỗi.");
        return new(outcome.Text!, true, used!.Name, sources.DistinctBy(source => source.Title).Take(8).ToList(),
            firstError is null ? null : $"Nhà cung cấp chính lỗi, đã dùng {used.Name}.");
    }

    // ------------------------------------------------------------------ tools (read-only)

    private async Task<ToolExecution> ExecuteAsync(ChatToolCall call, int actorId, List<CrmSource> sources, CancellationToken ct)
    {
        if (call.Arguments.ValueKind != JsonValueKind.Object) return Fail("invalid_argument", "Tham số phải là đối tượng JSON.");
        var args = call.Arguments;
        return call.Name switch
        {
            "crm_overview" => await OverviewAsync(Int(args, "days", 30, 1, 365), sources, ct),
            "list_tickets" => await ListTicketsAsync(Str(args, "status"), Str(args, "priority"), Str(args, "assignee"),
                args.TryGetProperty("overdue_only", out var overdue) && overdue.ValueKind == JsonValueKind.True, Int(args, "limit", 10, 1, 20), actorId, sources, ct),
            "ticket_details" => await TicketDetailsAsync(Str(args, "ticket_number"), sources, ct),
            "search_customers" => await SearchCustomersAsync(Str(args, "query"), sources, ct),
            "customer_profile" => args.TryGetProperty("customer_id", out var id) && id.TryGetInt32(out var customerId)
                ? await CustomerProfileAsync(customerId, sources, ct) : Fail("invalid_argument", "customer_id phải là số nguyên."),
            "staff_workload" => await WorkloadAsync(sources, ct),
            _ => Fail("unknown_tool", "Công cụ không tồn tại. Trợ lý CRM chỉ được đọc dữ liệu.")
        };
    }

    private async Task<ToolExecution> OverviewAsync(int days, List<CrmSource> sources, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-days);
        // SQLite cannot compare DateTimeOffset, so the date window is applied in memory on a narrow projection.
        var tickets = await db.Tickets.AsNoTracking()
            .Select(ticket => new { ticket.Id, ticket.Status, ticket.Priority, ticket.Channel, ticket.CreatedAt, ticket.AssigneeId, ticket.SlaDueAt })
            .ToListAsync(ct);
        var firstReplies = await FirstStaffRepliesAsync(ct);
        var recent = tickets.Where(ticket => ticket.CreatedAt >= since).ToList();
        var responseHours = recent.Where(ticket => firstReplies.ContainsKey(ticket.Id))
            .Select(ticket => (firstReplies[ticket.Id] - ticket.CreatedAt).TotalHours).ToList();
        var customers = await db.Users.AsNoTracking().Where(user => user.Role == UserRoles.Customer).Select(user => user.CreatedAt).ToListAsync(ct);
        sources.Add(new($"Thống kê yêu cầu {days} ngày gần đây", "/Dashboard"));
        return Ok(new
        {
            period_days = days,
            tickets_total = tickets.Count,
            by_status = tickets.GroupBy(ticket => DisplayText.TicketStatus(ticket.Status)).ToDictionary(group => group.Key, group => group.Count()),
            by_priority = tickets.GroupBy(ticket => ticket.Priority ?? "Chưa đặt").ToDictionary(group => group.Key, group => group.Count()),
            by_channel = tickets.GroupBy(ticket => ticket.Channel == "chat" ? "Chatbot" : "Web").ToDictionary(group => group.Key, group => group.Count()),
            created_in_period = recent.Count,
            open_unassigned = tickets.Count(ticket => OpenStatuses.Contains(ticket.Status) && ticket.AssigneeId == null),
            open_overdue_sla = tickets.Count(ticket => SlaPolicy.State(ticket.SlaDueAt, ticket.Status,
                firstReplies.TryGetValue(ticket.Id, out var at) ? at : null, now) == SlaState.Overdue),
            avg_first_response_hours_in_period = responseHours.Count == 0 ? (double?)null : Math.Round(responseHours.Average(), 1),
            answered_in_period = responseHours.Count,
            customers_total = customers.Count,
            new_customers_in_period = customers.Count(created => created >= since)
        });
    }

    private async Task<ToolExecution> ListTicketsAsync(string status, string priority, string assignee, bool overdueOnly, int limit,
        int actorId, List<CrmSource> sources, CancellationToken ct)
    {
        var query = db.Tickets.AsNoTracking().AsQueryable();
        if (status.Equals("open", StringComparison.OrdinalIgnoreCase)) query = query.Where(ticket => ticket.Status == "Open" || ticket.Status == "In progress");
        else if (status.Length > 0)
        {
            var code = Statuses.FirstOrDefault(item => item.Equals(status, StringComparison.OrdinalIgnoreCase) ||
                DisplayText.TicketStatus(item).Equals(status, StringComparison.OrdinalIgnoreCase));
            if (code is null) return Fail("invalid_argument", "status không hợp lệ.");
            query = query.Where(ticket => ticket.Status == code);
        }
        if (priority.Length > 0)
        {
            var level = TicketAiService.Priorities.FirstOrDefault(item => item.Equals(priority, StringComparison.OrdinalIgnoreCase));
            if (level is null) return Fail("invalid_argument", "priority không hợp lệ.");
            query = query.Where(ticket => ticket.Priority == level);
        }
        if (assignee.Equals("me", StringComparison.OrdinalIgnoreCase)) query = query.Where(ticket => ticket.AssigneeId == actorId);
        else if (assignee.Equals("none", StringComparison.OrdinalIgnoreCase)) query = query.Where(ticket => ticket.AssigneeId == null);
        else if (assignee.Length > 0) query = query.Where(ticket => ticket.Assignee != null && ticket.Assignee.DisplayName.Contains(assignee));

        var rows = await query.OrderByDescending(ticket => ticket.Id).Take(overdueOnly ? 500 : limit)
            .Select(ticket => new
            {
                ticket.Id, ticket.TicketNumber, ticket.Subject, ticket.Status, ticket.Priority, ticket.CreatedAt, ticket.SlaDueAt,
                Customer = ticket.Customer.DisplayName, Assignee = ticket.Assignee == null ? null : ticket.Assignee.DisplayName
            }).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var firstReplies = await FirstStaffRepliesAsync(ct);
        var picked = rows.Select(row => new { Row = row, Sla = SlaPolicy.State(row.SlaDueAt, row.Status, firstReplies.TryGetValue(row.Id, out var at) ? at : null, now) })
            .Where(item => !overdueOnly || item.Sla == SlaState.Overdue).Take(limit).ToList();
        sources.Add(new("Danh sách yêu cầu theo bộ lọc", "/Tickets" + (assignee.Equals("me", StringComparison.OrdinalIgnoreCase) ? "?assignee=me"
            : assignee.Equals("none", StringComparison.OrdinalIgnoreCase) ? "?assignee=none" : "")));
        return Ok(new
        {
            count = picked.Count,
            tickets = picked.Select(item => new
            {
                ticket_number = item.Row.TicketNumber, subject = item.Row.Subject, status = DisplayText.TicketStatus(item.Row.Status),
                priority = item.Row.Priority ?? "Chưa đặt", customer = item.Row.Customer, assignee = item.Row.Assignee ?? "Chưa phân công",
                created = item.Row.CreatedAt.ToOffset(VietnamOffset).ToString("dd/MM/yyyy"), sla = SlaText(item.Sla)
            })
        });
    }

    private async Task<ToolExecution> TicketDetailsAsync(string number, List<CrmSource> sources, CancellationToken ct)
    {
        number = number.Trim().ToUpperInvariant();
        if (!number.StartsWith("CS-", StringComparison.Ordinal) || number.Length > 24) return Fail("invalid_argument", "ticket_number phải có dạng CS-...");
        var ticket = await db.Tickets.AsNoTracking().Include(item => item.Customer).Include(item => item.Assignee)
            .SingleOrDefaultAsync(item => item.TicketNumber == number, ct);
        if (ticket is null) return Fail("not_found", "Không có yêu cầu với mã này.");
        var messages = await db.TicketMessages.AsNoTracking().Include(message => message.Author)
            .Where(message => message.TicketId == ticket.Id).OrderBy(message => message.Id).ToListAsync(ct);
        var firstStaff = messages.FirstOrDefault(message => message.Author.Role != UserRoles.Customer)?.CreatedAt;
        sources.Add(new($"Yêu cầu {ticket.TicketNumber}", $"/Tickets/Details/{ticket.Id}"));
        return Ok(new
        {
            ticket_number = ticket.TicketNumber, subject = ticket.Subject, status = DisplayText.TicketStatus(ticket.Status),
            category = DisplayText.TicketCategory(ticket.Category), priority = ticket.Priority ?? "Chưa đặt",
            channel = ticket.Channel == "chat" ? "Chatbot" : "Web", customer = ticket.Customer.DisplayName, customer_id = ticket.CustomerId,
            assignee = ticket.Assignee?.DisplayName ?? "Chưa phân công",
            created = ticket.CreatedAt.ToOffset(VietnamOffset).ToString("HH:mm dd/MM/yyyy"),
            sla = SlaText(SlaPolicy.State(ticket.SlaDueAt, ticket.Status, firstStaff, DateTimeOffset.UtcNow)),
            message_count = messages.Count,
            recent_messages = messages.TakeLast(6).Select(message => new
            {
                from = message.Author.Role == UserRoles.Customer ? "Khách hàng" : "Nhân viên",
                at = message.CreatedAt.ToOffset(VietnamOffset).ToString("HH:mm dd/MM"),
                text = Clip(message.Body, 300)
            })
        });
    }

    private async Task<ToolExecution> SearchCustomersAsync(string query, List<CrmSource> sources, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 100) return Fail("invalid_argument", "query phải dài 2-100 ký tự.");
        var upper = query.ToUpperInvariant();
        var phone = PhoneNumber.Normalize(query);
        var rows = await db.Users.AsNoTracking()
            .Where(user => user.Role == UserRoles.Customer &&
                (user.DisplayName.Contains(query) || user.NormalizedEmail.Contains(upper) || (phone != null && user.PhoneNumber == phone)))
            .OrderBy(user => user.DisplayName).Take(10)
            .Select(user => new
            {
                user.Id, user.DisplayName, user.Email, user.IsActive,
                Tickets = db.Tickets.Count(ticket => ticket.CustomerId == user.Id),
                Open = db.Tickets.Count(ticket => ticket.CustomerId == user.Id && (ticket.Status == "Open" || ticket.Status == "In progress"))
            }).ToListAsync(ct);
        sources.Add(new($"Tìm khách hàng \"{Clip(query, 40)}\"", "/Customers?search=" + Uri.EscapeDataString(query)));
        return Ok(new
        {
            count = rows.Count,
            customers = rows.Select(row => new
            {
                customer_id = row.Id, name = row.DisplayName, email = ChatbotService.Mask(row.Email),
                active = row.IsActive, tickets = row.Tickets, open_tickets = row.Open
            })
        });
    }

    private async Task<ToolExecution> CustomerProfileAsync(int customerId, List<CrmSource> sources, CancellationToken ct)
    {
        var customer = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == customerId && user.Role == UserRoles.Customer, ct);
        if (customer is null) return Fail("not_found", "Không có khách hàng với mã này.");
        var tickets = await db.Tickets.AsNoTracking().Where(ticket => ticket.CustomerId == customerId).OrderByDescending(ticket => ticket.Id)
            .Select(ticket => new { ticket.TicketNumber, ticket.Subject, ticket.Status, ticket.Priority, ticket.Category, ticket.CreatedAt,
                Assignee = ticket.Assignee == null ? null : ticket.Assignee.DisplayName })
            .ToListAsync(ct);
        sources.Add(new($"Hồ sơ khách hàng {customer.DisplayName}", $"/Customers/Details/{customer.Id}"));
        return Ok(new
        {
            customer_id = customer.Id, name = customer.DisplayName, email = ChatbotService.Mask(customer.Email), active = customer.IsActive,
            customer_since = customer.CreatedAt.ToOffset(VietnamOffset).ToString("dd/MM/yyyy"),
            tickets_total = tickets.Count,
            tickets_open = tickets.Count(ticket => OpenStatuses.Contains(ticket.Status)),
            recent_tickets = tickets.Take(10).Select(ticket => new
            {
                ticket_number = ticket.TicketNumber, subject = ticket.Subject, status = DisplayText.TicketStatus(ticket.Status),
                priority = ticket.Priority ?? "Chưa đặt", category = DisplayText.TicketCategory(ticket.Category),
                assignee = ticket.Assignee ?? "Chưa phân công", created = ticket.CreatedAt.ToOffset(VietnamOffset).ToString("dd/MM/yyyy")
            })
        });
    }

    private async Task<ToolExecution> WorkloadAsync(List<CrmSource> sources, CancellationToken ct)
    {
        var staff = await db.Users.AsNoTracking()
            .Where(user => user.IsActive && (user.Role == UserRoles.Staff || user.Role == UserRoles.Admin))
            .Select(user => new { user.Id, user.DisplayName, user.Role }).ToListAsync(ct);
        var open = await db.Tickets.AsNoTracking().Where(ticket => ticket.AssigneeId != null && (ticket.Status == "Open" || ticket.Status == "In progress"))
            .Select(ticket => new { ticket.Id, ticket.AssigneeId, ticket.Status, ticket.SlaDueAt }).ToListAsync(ct);
        var firstReplies = await FirstStaffRepliesAsync(ct);
        var now = DateTimeOffset.UtcNow;
        sources.Add(new("Khối lượng việc của nhân viên", "/Tickets?assignee=none"));
        return Ok(new
        {
            staff = staff.Select(member => new
            {
                name = member.DisplayName, role = DisplayText.Role(member.Role),
                open_assigned = open.Count(ticket => ticket.AssigneeId == member.Id),
                overdue_sla = open.Count(ticket => ticket.AssigneeId == member.Id &&
                    SlaPolicy.State(ticket.SlaDueAt, ticket.Status, firstReplies.TryGetValue(ticket.Id, out var at) ? at : null, now) == SlaState.Overdue)
            }).OrderBy(member => member.open_assigned),
            open_unassigned = await db.Tickets.CountAsync(ticket => ticket.AssigneeId == null && (ticket.Status == "Open" || ticket.Status == "In progress"), ct)
        });
    }

    /// <summary>Ticket id → time of the first staff/admin message (min message id per ticket, then its timestamp).</summary>
    private async Task<Dictionary<int, DateTimeOffset>> FirstStaffRepliesAsync(CancellationToken ct)
    {
        var firstIds = await db.TicketMessages.AsNoTracking().Where(message => message.Author.Role != UserRoles.Customer)
            .GroupBy(message => message.TicketId).Select(group => group.Min(message => message.Id)).ToListAsync(ct);
        return await db.TicketMessages.AsNoTracking().Where(message => firstIds.Contains(message.Id))
            .ToDictionaryAsync(message => message.TicketId, message => message.CreatedAt, ct);
    }

    private static string SlaText(SlaState state) => state switch
    {
        SlaState.Overdue => "Quá hạn SLA phản hồi đầu",
        SlaState.Pending => "Còn hạn SLA",
        SlaState.Met => "Đạt SLA",
        SlaState.Missed => "Trễ SLA",
        _ => "Không áp dụng"
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static ToolExecution Ok(object data) => new(true, JsonSerializer.Serialize(data, JsonOptions), null);
    private static ToolExecution Fail(string error, string message) =>
        new(false, JsonSerializer.Serialize(new { ok = false, error, message }, JsonOptions), error);
    private static string Str(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";
    private static int Int(JsonElement args, string name, int fallback, int min, int max) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? Math.Clamp(number, min, max) : fallback;
    private static string Clip(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}
