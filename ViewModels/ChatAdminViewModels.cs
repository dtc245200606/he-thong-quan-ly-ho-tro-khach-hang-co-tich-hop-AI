namespace CustomerSupportAI.ViewModels;

public sealed record ChatSessionRow(int Id, string Contact, string Mode, string Status, string StatusText, bool Urgent,
    string? TicketNumber, string LastMessage, string LastSender, string LastActivity, int MessageCount);

public sealed record ChatAdminIndexViewModel(IReadOnlyList<ChatSessionRow> Sessions, string Status, IReadOnlyDictionary<string, int> Counts);
