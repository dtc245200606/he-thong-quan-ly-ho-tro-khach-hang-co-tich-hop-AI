using System.Security.Claims;
using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using CustomerSupportAI.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.Controllers;

public sealed class AccountController(
    AppDbContext db,
    IPasswordHasher<SupportUser> passwordHasher,
    LoginThrottle throttle,
    PasswordResetTokens resetTokens,
    EmailSender email,
    IConfiguration configuration,
    ILogger<AccountController> logger) : Controller
{
    /// <summary>Claim holding <see cref="PasswordResetTokens.Stamp"/>; a password change invalidates other sessions.</summary>
    public const string PasswordStampClaim = "cs:pwd";

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    /// <summary>
    /// Single sign-in screen for every role: the account found by email or phone number
    /// decides whether the user lands in the customer or the staff workspace.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var normalizedEmail = model.Email.Trim().ToUpperInvariant();
        var phone = PhoneNumber.Normalize(model.Email);
        var throttleKey = LoginThrottle.Key(HttpContext, phone ?? normalizedEmail);
        if (throttle.IsBlocked(throttleKey))
        {
            ModelState.AddModelError(string.Empty, "Bạn đã đăng nhập sai quá nhiều lần. Vui lòng thử lại sau 15 phút.");
            return View(model);
        }

        var user = await db.Users.FirstOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail)
            ?? (phone is null ? null : await db.Users.FirstOrDefaultAsync(item => item.PhoneNumber == phone));
        var verificationResult = user is null
            ? PasswordVerificationResult.Failed
            : passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);
        if (user is null || verificationResult == PasswordVerificationResult.Failed)
        {
            throttle.RecordFailure(throttleKey);
            ModelState.AddModelError(string.Empty, "Email/số điện thoại hoặc mật khẩu không chính xác.");
            return View(model);
        }

        throttle.Reset(throttleKey);
        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");
            return View(model);
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, model.Password);
            await db.SaveChangesAsync();
        }

        await SignInAsync(user, model.RememberMe);
        if (Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl!);
        }

        // Staff and admins start from the ticket queue, customers from their own overview.
        return user.Role is UserRoles.Admin or UserRoles.Staff
            ? RedirectToAction("Index", "Tickets")
            : RedirectToAction("Index", "Dashboard");
    }

    /// <summary>Old staff portal address; everyone now signs in on the shared screen.</summary>
    [HttpGet]
    public IActionResult StaffLogin(string? returnUrl = null) => RedirectToAction(nameof(Login), new { returnUrl });

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Tickets");
        }

        return View(new RegisterViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        var normalizedEmail = email.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail))
        {
            ModelState.AddModelError(nameof(model.Email), "Email này đã được đăng ký.");
            return View(model);
        }

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(model.PhoneNumber))
        {
            phone = PhoneNumber.Normalize(model.PhoneNumber);
            if (phone is null)
            {
                ModelState.AddModelError(nameof(model.PhoneNumber), "Số điện thoại không hợp lệ (ví dụ 0912345678).");
                return View(model);
            }
            if (await db.Users.AnyAsync(user => user.PhoneNumber == phone))
            {
                ModelState.AddModelError(nameof(model.PhoneNumber), "Số điện thoại này đã được đăng ký.");
                return View(model);
            }
        }

        var user = new SupportUser
        {
            DisplayName = model.DisplayName.Trim(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            PhoneNumber = phone,
            Role = UserRoles.Customer
        };
        user.PasswordHash = passwordHasher.HashPassword(user, model.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await SignInAsync(user, isPersistent: false);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost, Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return View(await db.Users.AsNoTracking().SingleAsync(u => u.Id == id));
    }

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.SingleAsync(item => item.Id == userId);
        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.CurrentPassword)
            == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Mật khẩu hiện tại không chính xác.");
            return View(model);
        }

        user.PasswordHash = passwordHasher.HashPassword(user, model.NewPassword);
        await db.SaveChangesAsync();
        // Re-issue this session's cookie with the new password stamp; other sessions are signed out.
        var current = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await SignInAsync(user, current.Properties?.IsPersistent == true);
        TempData["Success"] = "Đã cập nhật mật khẩu. Các phiên đăng nhập khác đã được đăng xuất.";
        return RedirectToAction("Index", "Tickets");
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        ViewData["EmailAvailable"] = email.IsAvailable;
        ViewData["UsesOutbox"] = email.UsesOutbox;
        return View(new ForgotPasswordViewModel());
    }

    /// <summary>
    /// Sends a reset link to the account found by email or phone number. The response is the same whether
    /// or not the account exists, and the email is sent in the background so timing reveals nothing either.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        ViewData["EmailAvailable"] = email.IsAvailable;
        ViewData["UsesOutbox"] = email.UsesOutbox;
        if (!email.IsAvailable)
        {
            ModelState.AddModelError(string.Empty, "Hệ thống chưa cấu hình gửi email. Vui lòng liên hệ quản trị viên hoặc nhân viên hỗ trợ để được đặt lại mật khẩu.");
            return View(model);
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Every request counts, so one IP can request at most 5 links per 15 minutes.
        var throttleKey = LoginThrottle.Key(HttpContext, "password-reset");
        if (throttle.IsBlocked(throttleKey))
        {
            ModelState.AddModelError(string.Empty, "Bạn đã yêu cầu đặt lại mật khẩu quá nhiều lần. Vui lòng thử lại sau 15 phút.");
            return View(model);
        }

        throttle.RecordFailure(throttleKey);
        var normalizedEmail = model.Email.Trim().ToUpperInvariant();
        var phone = PhoneNumber.Normalize(model.Email);
        var user = await db.Users.FirstOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail)
            ?? (phone is null ? null : await db.Users.FirstOrDefaultAsync(item => item.PhoneNumber == phone));

        // Locked accounts get no link: resetting the password must not bypass a lock.
        if (user is { IsActive: true })
        {
            var link = ResetLink(resetTokens.Create(user));
            var body = $"Xin chào {user.DisplayName},\n\n" +
                "Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản CustomerSupport của bạn.\n" +
                $"Mở liên kết sau trong vòng {PasswordResetTokens.Lifetime.TotalMinutes:0} phút để đặt mật khẩu mới (chỉ dùng được một lần):\n\n{link}\n\n" +
                "Nếu bạn không yêu cầu, hãy bỏ qua email này; mật khẩu hiện tại vẫn giữ nguyên.\n" +
                "Không chia sẻ liên kết này cho bất kỳ ai, kể cả nhân viên hỗ trợ.\n\nCustomerSupport";
            db.AuditLogs.Add(new AuditLog { ActorId = user.Id, ActorName = user.DisplayName, Action = "user.password-reset-request", Target = user.Email, Detail = "Yêu cầu liên kết đặt lại mật khẩu" });
            await db.SaveChangesAsync();
            var to = user.Email;
            _ = Task.Run(async () =>
            {
                if (!await email.SendAsync(to, "Đặt lại mật khẩu CustomerSupport", body, CancellationToken.None))
                {
                    logger.LogWarning("Password reset email could not be delivered.");
                }
            });
        }

        return RedirectToAction(nameof(ForgotPasswordSent));
    }

    [HttpGet]
    public IActionResult ForgotPasswordSent()
    {
        ViewData["UsesOutbox"] = email.UsesOutbox;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string? token)
    {
        var user = await UserForTokenAsync(token);
        ViewData["TokenValid"] = user is not null;
        return View(new ResetPasswordViewModel { Token = token ?? string.Empty });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        var user = await UserForTokenAsync(model.Token);
        ViewData["TokenValid"] = user is not null;
        if (user is null || !ModelState.IsValid)
        {
            return View(model);
        }

        user.PasswordHash = passwordHasher.HashPassword(user, model.NewPassword);
        db.AuditLogs.Add(new AuditLog { ActorId = user.Id, ActorName = user.DisplayName, Action = "user.password-reset", Target = user.Email, Detail = "Đặt lại mật khẩu qua liên kết email" });
        await db.SaveChangesAsync();
        // Clear failed sign-ins for this account from this device so the new password works at once.
        throttle.Reset(LoginThrottle.Key(HttpContext, user.NormalizedEmail));
        if (User.Identity?.IsAuthenticated == true)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        TempData["Success"] = "Đã đặt lại mật khẩu. Vui lòng đăng nhập bằng mật khẩu mới.";
        return RedirectToAction(nameof(Login));
    }

    /// <summary>The active account a reset token belongs to, provided the password has not changed since it was issued.</summary>
    private async Task<SupportUser?> UserForTokenAsync(string? token)
    {
        if (resetTokens.Read(token) is not { } claim)
        {
            return null;
        }

        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == claim.UserId);
        return user is { IsActive: true } && PasswordResetTokens.Stamp(user.PasswordHash) == claim.Stamp ? user : null;
    }

    /// <summary>Absolute reset link. Prefer the configured App:BaseUrl so a forged Host header cannot redirect the link.</summary>
    private string ResetLink(string token)
    {
        var path = Url.Action(nameof(ResetPassword), new { token })!;
        var configured = configuration["App:BaseUrl"];
        return string.IsNullOrWhiteSpace(configured)
            ? $"{Request.Scheme}://{Request.Host}{path}"
            : configured.TrimEnd('/') + path;
    }

    private Task SignInAsync(SupportUser user, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(PasswordStampClaim, PasswordResetTokens.Stamp(user.PasswordHash))
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties
        {
            IsPersistent = isPersistent,
            AllowRefresh = true
        };

        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
    }
}
