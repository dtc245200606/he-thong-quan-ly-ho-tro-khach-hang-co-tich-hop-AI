using System.ComponentModel.DataAnnotations;

namespace CustomerSupportAI.Models;

public sealed class TicketMessage
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public SupportTicket Ticket { get; set; } = null!;
    public int AuthorId { get; set; }
    public SupportUser Author { get; set; } = null!;

    [Required, MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}