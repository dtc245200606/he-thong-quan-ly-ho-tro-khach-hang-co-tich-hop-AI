using System.ComponentModel.DataAnnotations;
using CustomerSupportAI.Models;

namespace CustomerSupportAI.ViewModels;

public sealed class TicketIndexViewModel
{
    public IReadOnlyList<SupportTicket> Tickets { get; init; } = [];
    public PageInfo Paging { get; init; } = new(1, 20, 0);
    public string Sort { get; init; } = "id_desc";
    public string Search { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    /// <summary>Customer the list is narrowed to (staff only), or null.</summary>
    public SupportUser? Customer { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyDictionary<int, int> MessageCounts { get; init; } = new Dictionary<int, int>();
    /// <summary>Ticket ids on this page whose newest message came from the customer.</summary>
    public IReadOnlySet<int> AwaitingTeam { get; init; } = new HashSet<int>();
    public int TotalCount { get; init; }
    public int OpenCount { get; init; }
    public int ResolvedCount { get; init; }
    /// <summary>Open or in-progress tickets waiting for a team reply (staff only).</summary>
    public int AwaitingCount { get; init; }
    public bool IsStaff { get; init; }
    public bool IsFiltered => Search.Length > 0 || Status.Length > 0 || Category.Length > 0 || Customer is not null || From is not null || To is not null;
}

public sealed class TicketCreateViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập tiêu đề yêu cầu."), MaxLength(120, ErrorMessage = "Tiêu đề không được vượt quá 120 ký tự.")]
    public string Subject { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn danh mục.")]
    public string Category { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập nội dung yêu cầu."), StringLength(4000, MinimumLength = 10, ErrorMessage = "Nội dung cần có từ {2} đến {1} ký tự.")]
    public string FirstMessage { get; set; } = string.Empty;
}

public sealed class TicketReplyViewModel
{
    [Required]
    public int TicketId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập nội dung phản hồi."), StringLength(4000, MinimumLength = 1, ErrorMessage = "Phản hồi không được vượt quá {1} ký tự.")]
    public string Body { get; set; } = string.Empty;
}

public sealed class TicketDetailsViewModel
{
    public required SupportTicket Ticket { get; init; }
    public bool IsStaff { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    /// <summary>Whether a Gemini key is configured; the AI panel explains the setup step when it is not.</summary>
    public bool AiConfigured { get; init; }
    public int CustomerTicketCount { get; init; }
}

public sealed class UserListItemViewModel
{
    public required SupportUser User { get; init; }
    public int TicketCount { get; init; }
}

public sealed class AdminUsersViewModel
{
    public IReadOnlyList<UserListItemViewModel> Users { get; init; } = [];
    public PageInfo Paging { get; init; } = new(1, 20, 0);
    public string Sort { get; init; } = "id";
    public CreateStaffViewModel NewStaff { get; init; } = new();
    public string Search { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}