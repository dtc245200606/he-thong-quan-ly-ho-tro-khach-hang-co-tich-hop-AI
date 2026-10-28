using System.ComponentModel.DataAnnotations;
using CustomerSupportAI.Models;

namespace CustomerSupportAI.ViewModels;

// "Portal" is the controller that renders the shared user views: "Admin" or "Customers".

public sealed class UserCreateViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên."), StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải có từ {2} đến {1} ký tự.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email."), EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ."), MaxLength(320, ErrorMessage = "Địa chỉ email không được vượt quá 320 ký tự.")]
    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = UserRoles.Customer;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu tạm thời."), MinLength(10, ErrorMessage = "Mật khẩu cần có ít nhất {1} ký tự."), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string Portal { get; set; } = "Admin";
    public List<string> AllowedRoles { get; set; } = [];
}

public sealed class UserEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập họ và tên."), StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải có từ {2} đến {1} ký tự.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email."), EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ."), MaxLength(320, ErrorMessage = "Địa chỉ email không được vượt quá 320 ký tự.")]
    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = UserRoles.Customer;
    public bool IsActive { get; set; } = true;

    [MinLength(10, ErrorMessage = "Mật khẩu cần có ít nhất {1} ký tự."), DataType(DataType.Password)]
    public string? NewPassword { get; set; }

    public string Portal { get; set; } = "Admin";
    public bool IsSelf { get; set; }
    public bool CanChangeRole { get; set; }
}

public sealed class UserDetailsViewModel
{
    public SupportUser User { get; set; } = null!;
    public string Portal { get; set; } = "Admin";
    public bool IsSelf { get; set; }
    public int TicketCount { get; set; }
    public int MessageCount { get; set; }
    public int ConversationCount { get; set; }
    public List<SupportTicket> RecentTickets { get; set; } = [];
}

public sealed class UserListViewModel
{
    public IReadOnlyList<UserListItemViewModel> Users { get; init; } = [];
    public PageInfo Paging { get; init; } = new(1, 20, 0);
    public string Sort { get; init; } = "id_desc";
    public string Search { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int TotalCustomers { get; init; }
}
