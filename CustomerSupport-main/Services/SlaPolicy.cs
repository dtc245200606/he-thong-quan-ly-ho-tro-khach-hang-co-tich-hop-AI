namespace CustomerSupportAI.Services;

/// <summary>First-response targets per priority, bound from the "Sla" configuration section (hours).</summary>
public sealed class SlaOptions
{
    public Dictionary<string, int> FirstResponseHours { get; set; } = new()
    {
        ["Khẩn cấp"] = 4,
        ["Cao"] = 8,
        ["Trung bình"] = 24,
        ["Thấp"] = 72
    };
}

public enum SlaState { None, Pending, Overdue, Met, Missed }

/// <summary>
/// First-response SLA: a ticket is due a first staff reply within the hours set for its priority, counted from
/// creation (calendar hours, no business-hours calendar). Tickets without a priority have no SLA.
/// </summary>
public static class SlaPolicy
{
    public const string DefaultPriority = "Trung bình";

    public static DateTimeOffset? DueAt(DateTimeOffset createdAt, string? priority, SlaOptions options) =>
        priority is not null && options.FirstResponseHours.TryGetValue(priority, out var hours) && hours > 0
            ? createdAt.AddHours(hours)
            : null;

    /// <param name="firstStaffReplyAt">Time of the first message written by staff/admin, if any.</param>
    public static SlaState State(DateTimeOffset? dueAt, string status, DateTimeOffset? firstStaffReplyAt, DateTimeOffset now)
    {
        if (dueAt is null) return SlaState.None;
        if (firstStaffReplyAt is { } replied) return replied <= dueAt ? SlaState.Met : SlaState.Missed;
        if (status is "Resolved" or "Closed") return SlaState.None;
        return now > dueAt ? SlaState.Overdue : SlaState.Pending;
    }
}
