using System.ComponentModel.DataAnnotations;

namespace CustomerSupportAI.Models;

public static class ChatModes
{
    public const string Bot = "bot";
    public const string Human = "human";
}

public static class ChatStatuses
{
    public const string Active = "active";
    public const string WaitingStaff = "waiting_staff";
    public const string StaffReplied = "staff_replied";
    public const string Closed = "closed";

    public static readonly string[] All = [Active, WaitingStaff, StaffReplied, Closed];
}

public static class ChatSenders
{
    public const string Customer = "customer";
    public const string Bot = "bot";
    public const string Staff = "staff";
    public const string System = "system";
}

/// <summary>
/// A website chat conversation. The browser holds a random token; only its SHA-256 hash is stored, so a
/// leaked database row cannot be replayed. UserId is set when a signed-in customer started the chat.
/// No foreign keys to Users/Tickets: account deletion removes sessions explicitly (see UserAdministrationService).
/// </summary>
public sealed class ChatSession
{
    public int Id { get; set; }

    [Required, MaxLength(32)]
    public string PublicId { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public int? UserId { get; set; }

    [MaxLength(320)]
    public string? ContactEmail { get; set; }

    /// <summary>True once the guest opened the confirmation link sent to <see cref="ContactEmail"/>.</summary>
    public bool EmailVerified { get; set; }

    /// <summary>Confirmation emails sent for this session (capped to stop mail bombing).</summary>
    public int VerificationSends { get; set; }

    [Required, MaxLength(10)]
    public string Mode { get; set; } = ChatModes.Bot;

    [Required, MaxLength(20)]
    public string Status { get; set; } = ChatStatuses.Active;

    public int? TicketId { get; set; }

    [MaxLength(300)]
    public string? HandoffReason { get; set; }

    public bool Urgent { get; set; }

    /// <summary>Newest ticket message already mirrored into this chat (staff replies written in the ticket screen).</summary>
    public int LastTicketMessageId { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ChatMessage
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public ChatSession Session { get; set; } = null!;

    [Required, MaxLength(10)]
    public string Sender { get; set; } = ChatSenders.Customer;

    [Required, MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    /// <summary>Staff-only note. Never returned by the public chat API.</summary>
    public bool IsInternal { get; set; }

    public int? AuthorId { get; set; }

    [MaxLength(100)]
    public string? AuthorName { get; set; }

    /// <summary>Ticket message this chat line was mirrored to/from, to avoid duplicates when syncing.</summary>
    public int? TicketMessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Operational record of one AI call chain. Holds no message text, keys or raw personal data.</summary>
public sealed class AiLog
{
    public int Id { get; set; }
    public int? SessionId { get; set; }

    [Required, MaxLength(20)]
    public string Task { get; set; } = "chat";

    [Required, MaxLength(20)]
    public string Provider { get; set; } = string.Empty;

    public bool FallbackUsed { get; set; }
    public bool Success { get; set; }
    public int DurationMs { get; set; }
    public int RedactedCount { get; set; }
    public int ToolRounds { get; set; }

    /// <summary>Compact list such as "search_faq:ok; create_ticket:rejected".</summary>
    [MaxLength(1000)]
    public string ToolCalls { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Error { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
