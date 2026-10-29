using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.Data;

/// <summary>
/// Database-level value rules, the equivalent of CHECK constraints. SQLite cannot add CHECK constraints to an
/// existing table without rebuilding it, so they are BEFORE INSERT/UPDATE triggers: additive, idempotent, and they
/// never touch rows that already exist. The application validates the same rules first; these are the last line
/// against bad writes from scripts, tools or future code.
/// </summary>
public static class IntegrityRules
{
    private static readonly (string Name, string Table, string Columns, string Condition, string Message)[] Rules =
    [
        ("Users_Role", "Users", "Role", "NEW.Role NOT IN ('Admin','Staff','Customer')", "Users.Role không hợp lệ"),
        ("Tickets_Status", "Tickets", "Status", "NEW.Status NOT IN ('Open','In progress','Resolved','Closed')", "Tickets.Status không hợp lệ"),
        ("Tickets_Priority", "Tickets", "Priority", "NEW.Priority IS NOT NULL AND NEW.Priority NOT IN ('Thấp','Trung bình','Cao','Khẩn cấp')", "Tickets.Priority không hợp lệ"),
        ("Tickets_Channel", "Tickets", "Channel", "NEW.Channel NOT IN ('web','chat')", "Tickets.Channel không hợp lệ"),
        ("Tickets_Assignee", "Tickets", "AssigneeId",
            "NEW.AssigneeId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM Users WHERE Id = NEW.AssigneeId AND Role IN ('Admin','Staff'))",
            "Tickets.AssigneeId phải là nhân viên hoặc quản trị viên"),
        ("TicketMessages_Body", "TicketMessages", "Body", "length(trim(NEW.Body)) = 0", "TicketMessages.Body không được rỗng"),
        ("ChatSessions_Mode", "ChatSessions", "Mode", "NEW.Mode NOT IN ('bot','human')", "ChatSessions.Mode không hợp lệ"),
        ("ChatSessions_Status", "ChatSessions", "Status", "NEW.Status NOT IN ('active','waiting_staff','staff_replied','closed')", "ChatSessions.Status không hợp lệ"),
        ("ChatMessages_Sender", "ChatMessages", "Sender", "NEW.Sender NOT IN ('customer','bot','staff','system')", "ChatMessages.Sender không hợp lệ")
    ];

    public static async Task ApplyAsync(AppDbContext db)
    {
        foreach (var rule in Rules)
        {
            foreach (var (kind, timing) in new[] { ("ins", "BEFORE INSERT"), ("upd", $"BEFORE UPDATE OF {rule.Columns}") })
            {
                // Names and conditions are constants above, never user input.
#pragma warning disable EF1003
                await db.Database.ExecuteSqlRawAsync(
                    $"CREATE TRIGGER IF NOT EXISTS trg_{rule.Name}_{kind} {timing} ON {rule.Table} " +
                    $"WHEN {rule.Condition} BEGIN SELECT RAISE(ABORT, '{rule.Message}'); END;");
#pragma warning restore EF1003
            }
        }
    }
}
