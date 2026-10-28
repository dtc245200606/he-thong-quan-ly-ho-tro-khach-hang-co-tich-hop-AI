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

    public int CustomerId { get; set; }
    public SupportUser Customer { get; set; } = null!;
    public ICollection<TicketMessage> Messages { get; set; } = new List<TicketMessage>();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}