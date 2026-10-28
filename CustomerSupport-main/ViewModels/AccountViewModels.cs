using System.ComponentModel.DataAnnotations;

namespace CustomerSupportAI.ViewModels;

public sealed class LoginViewModel
{
    // Email, phone number or a plain username such as "admin", so no EmailAddress check.
    [Required(ErrorMessage = "Vui lòng nhập email hoặc số điện thoại."), MaxLength(320, ErrorMessage = "Thông tin đăng nhập không được vượt quá 320 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên."), StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải có từ {2} đến {1} ký tự.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email."), EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ."), MaxLength(320, ErrorMessage = "Địa chỉ email không được vượt quá 320 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20, ErrorMessage = "Số điện thoại không hợp lệ."), DataType(DataType.PhoneNumber)]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."), MinLength(10, ErrorMessage = "Mật khẩu cần có ít nhất {1} ký tự."), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu."), Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp."), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class CreateStaffViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên."), StringLength(100, MinimumLength = 2, ErrorMessage = "Họ và tên phải có từ {2} đến {1} ký tự.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email."), EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ."), MaxLength(320, ErrorMessage = "Địa chỉ email không được vượt quá 320 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu tạm thời."), MinLength(10, ErrorMessage = "Mật khẩu cần có ít nhất {1} ký tự."), DataType(DataType.Password)]
    public string TemporaryPassword { get; set; } = string.Empty;
}

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập email hoặc số điện thoại."), MaxLength(320, ErrorMessage = "Thông tin không được vượt quá 320 ký tự.")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới."), MinLength(10, ErrorMessage = "Mật khẩu cần có ít nhất {1} ký tự."), DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới."), Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp."), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại."), DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới."), MinLength(10, ErrorMessage = "Mật khẩu cần có ít nhất {1} ký tự."), DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới."), Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp."), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}