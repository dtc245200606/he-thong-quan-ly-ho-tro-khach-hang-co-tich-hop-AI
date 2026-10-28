using CustomerSupportAI.Models;

namespace CustomerSupportAI.ViewModels;

public static class DisplayText
{
    public static string Role(string? role) => role switch
    {
        UserRoles.Admin => "Quản trị viên",
        UserRoles.Staff => "Nhân viên hỗ trợ",
        UserRoles.Customer => "Khách hàng",
        _ => "Người dùng"
    };

    public static string TicketStatus(string status) => status switch
    {
        "Open" => "Mới tiếp nhận",
        "In progress" => "Đang xử lý",
        "Resolved" => "Đã giải quyết",
        "Closed" => "Đã đóng",
        _ => status
    };

    /// <summary>Percentage for inline CSS. Invariant culture: vi-VN would print a decimal comma.</summary>
    public static string CssPercent(double value) =>
        Math.Clamp(value, 0, 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>Badge CSS class for a ticket status (see .badge-* in site.css).</summary>
    public static string TicketStatusBadge(string status) => status switch
    {
        "Open" => "badge badge-info",
        "In progress" => "badge badge-warning",
        "Resolved" => "badge badge-success",
        _ => "badge badge-neutral"
    };

    public static string RoleBadge(string role) => role switch
    {
        UserRoles.Admin => "badge badge-plain badge-brand",
        UserRoles.Staff => "badge badge-plain badge-sky",
        _ => "badge badge-plain badge-neutral"
    };

    public static string TicketCategory(string category) => category switch
    {
        "Product support" => "Hỗ trợ sản phẩm",
        "Billing" => "Hóa đơn và thanh toán",
        "Warranty & repair" => "Bảo hành và sửa chữa",
        "Account access" => "Truy cập tài khoản",
        "Other" => "Khác",
        _ => category
    };
}