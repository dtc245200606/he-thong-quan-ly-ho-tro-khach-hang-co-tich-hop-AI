using System.ComponentModel.DataAnnotations;

namespace CustomerSupportAI.Models;

public sealed class SupportTicket
{
    public int Id { get; set; }

    [Required, MaxLength(24)]
    public string TicketNumber { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Subject { get; set; } = string.Empty;

    [Required, MaxLength(60)]
    public string Category { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Open";

    /// <summary>One of TicketAiService.Priorities; null for tickets created before priorities existed.</summary>
    [MaxLength(20)]
    public string? Priority { get; set; }

    /// <summary>Intake channel: "web" (portal form) or "chat" (website chatbot).</summary>
    [Required, MaxLength(10)]
    public string Channel { get; set; } = "web";

    /// <summary>First-response deadline from SlaPolicy; null when the ticket has no priority.</summary>
    public DateTimeOffset? SlaDueAt { get; set; }

    /// <summary>Staff member or admin responsible for the ticket; null when unassigned (set to null if that account is deleted).</summary>
    public int? AssigneeId { get; set; }
    public SupportUser? Assignee { get; set; }

    public int CustomerId { get; set; }
    public SupportUser Customer { get; set; } = null!;
    public ICollection<TicketMessage> Messages { get; set; } = new List<TicketMessage>();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}