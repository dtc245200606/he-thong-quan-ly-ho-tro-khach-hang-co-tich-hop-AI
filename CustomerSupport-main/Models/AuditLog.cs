using System.ComponentModel.DataAnnotations;

namespace CustomerSupportAI.Models;

/// <summary>Immutable record of an administrative action (no foreign keys, so it survives user deletion).</summary>
public sealed class AuditLog
{
    public int Id { get; set; }

    public int? ActorId { get; set; }

    [Required, MaxLength(100)]
    public string ActorName { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Action { get; set; } = string.Empty;

    [Required, MaxLength(320)]
    public string Target { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
