using CustomerSupportAI.Models;
namespace CustomerSupportAI.ViewModels;
public sealed class DashboardViewModel
{
    public int Total { get; set; }
    public int Open { get; set; }
    public int Resolved { get; set; }
    public int Conversations { get; set; }
    /// <summary>Open or in-progress tickets whose newest message is the customer's (staff only).</summary>
    public int AwaitingTeam { get; set; }
    /// <summary>Open or in-progress tickets with no team message yet (staff only).</summary>
    public int Unanswered { get; set; }
    /// <summary>Mean time to first team reply for tickets created in the selected period; null when none.</summary>
    public TimeSpan? AvgFirstResponse { get; set; }
    public bool IsStaff { get; set; }
    public List<SupportTicket> Recent { get; set; } = [];
    public Dictionary<string, int> Categories { get; set; } = [];
    /// <summary>Open or in-progress tickets, oldest first (staff only).</summary>
    public List<SupportTicket> Queue { get; set; } = [];
    public Dictionary<string, int> StatusCounts { get; set; } = [];
    public List<TicketMessage> Activity { get; set; } = [];
    /// <summary>Selected period in days (7, 30 or 90) for the trend chart and the "new tickets" KPI.</summary>
    public int Range { get; set; } = 30;
    public int BucketDays { get; set; } = 1;
    public List<TrendPoint> Trend { get; set; } = [];
    public int NewInRange { get; set; }
    public int NewPrevious { get; set; }
}
public sealed record TrendPoint(DateTime Start, int Count);
public sealed record KnowledgeViewModel(IReadOnlyList<KnowledgeArticle> Articles, string Search, string Category);
public sealed record AssistantPageViewModel(IReadOnlyList<AssistantTurn> History, bool IsConfigured);
public sealed record ActivityViewModel(IReadOnlyList<TicketMessage> Messages);
public sealed record AuditLogViewModel(IReadOnlyList<AuditLog> Entries, PageInfo Paging, string Action);
