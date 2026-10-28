using System.ComponentModel.DataAnnotations;

namespace CustomerSupportAI.Models;

public sealed class KnowledgeArticle
{
    public int Id { get; set; }
    [Required, StringLength(160)] public string Title { get; set; } = "";
    [Required, StringLength(60)] public string Category { get; set; } = "Hướng dẫn";
    [Required, StringLength(6000)] public string Body { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssistantTurn
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Question { get; set; } = "";
    public string Reply { get; set; } = "";
    public string Mode { get; set; } = "knowledge";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
