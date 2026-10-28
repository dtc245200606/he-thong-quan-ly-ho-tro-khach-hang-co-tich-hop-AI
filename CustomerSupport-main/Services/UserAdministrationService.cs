using System.Security.Claims;
using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.Services;

public sealed record OperationResult(bool Succeeded, string? Error = null, string? Field = null)
{
    public static OperationResult Ok() => new(true);
    public static OperationResult Fail(string error, string? field = null) => new(false, error, field);
}

/// <summary>
/// Single place for account administration rules shared by the Admin and Customers controllers:
/// who may manage whom, last-administrator protection, safe deletion and audit logging.
/// </summary>
public sealed class UserAdministrationService(AppDbContext db, IPasswordHasher<SupportUser> hasher)
{
    public static int ActorId(ClaimsPrincipal actor) => int.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static bool IsAdmin(ClaimsPrincipal actor) => actor.IsInRole(UserRoles.Admin);

    /// <summary>Admins manage everyone; staff manage customers only.</summary>
    public static bool CanManage(ClaimsPrincipal actor, SupportUser target) =>
        IsAdmin(actor) || (actor.IsInRole(UserRoles.Staff) && target.Role == UserRoles.Customer);

    public static IReadOnlyList<string> RolesCreatableBy(ClaimsPrincipal actor) =>
        IsAdmin(actor) ? UserRoles.All : new[] { UserRoles.Customer };

    public static UserEditViewModel ToEditModel(ClaimsPrincipal actor, SupportUser user, string portal)
    {
        var model = new UserEditViewModel
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role,
            IsActive = user.IsActive
        };
        Decorate(model, actor, user, portal);
        return model;
    }

    /// <summary>Fills the server-controlled display flags (never trusted from the form).</summary>
    public static void Decorate(UserEditViewModel model, ClaimsPrincipal actor, SupportUser user, string portal)
    {
        model.Id = user.Id;
        model.Portal = portal;
        model.IsSelf = ActorId(actor) == user.Id;
        model.CanChangeRole = IsAdmin(actor) && !model.IsSelf;
    }

    public async Task AuditAsync(ClaimsPrincipal actor, string action, string target, string detail = "")
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorId = ActorId(actor),
            ActorName = Truncate(actor.Identity?.Name ?? "?", 100),
            Action = action,
            Target = Truncate(target, 320),
            Detail = Truncate(detail, 500)
        });
        await db.SaveChangesAsync();
    }

    public async Task<OperationResult> CreateAsync(
        ClaimsPrincipal actor, string displayName, string email, string role, string password)
    {
        if (!UserRoles.All.Contains(role))
        {
            return OperationResult.Fail("Vai trò không hợp lệ.", "Role");
        }

        if (!RolesCreatableBy(actor).Contains(role))
        {
            return OperationResult.Fail("Bạn không có quyền tạo loại tài khoản này.", "Role");
        }

        var trimmedEmail = email.Trim();
        var normalized = trimmedEmail.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalized))
        {
            return OperationResult.Fail("Email này đã được đăng ký.", "Email");
        }

        var created = new SupportUser
        {
            DisplayName = displayName.Trim(),
            Email = trimmedEmail,
            NormalizedEmail = normalized,
            Role = role
        };
        created.PasswordHash = hasher.HashPassword(created, password);
        db.Users.Add(created);
        await db.SaveChangesAsync();
        await AuditAsync(actor, "user.create", created.Email, $"Vai trò: {DisplayText.Role(role)}");
        return OperationResult.Ok();
    }

    public async Task<OperationResult> UpdateAsync(
        ClaimsPrincipal actor, SupportUser target, string displayName, string email,
        string? role, bool isActive, string? newPassword)
    {
        if (!CanManage(actor, target))
        {
            return OperationResult.Fail("Bạn không có quyền quản lý tài khoản này.");
        }

        var isSelf = ActorId(actor) == target.Id;
        string newRole = IsAdmin(actor) && !isSelf && !string.IsNullOrWhiteSpace(role) ? role : target.Role;
        if (!UserRoles.All.Contains(newRole))
        {
            return OperationResult.Fail("Vai trò không hợp lệ.", "Role");
        }

        var newActive = isSelf ? target.IsActive : isActive;
        var trimmedEmail = email.Trim();
        var normalized = trimmedEmail.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.Id != target.Id && user.NormalizedEmail == normalized))
        {
            return OperationResult.Fail("Email này đã được đăng ký.", "Email");
        }

        if (target.Role == UserRoles.Admin && target.IsActive
            && (newRole != UserRoles.Admin || !newActive)
            && await IsLastActiveAdminAsync(target))
        {
            return OperationResult.Fail("Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.");
        }

        var changes = new List<string>();
        var trimmedName = displayName.Trim();
        if (target.DisplayName != trimmedName) changes.Add("họ tên");
        if (!string.Equals(target.Email, trimmedEmail, StringComparison.Ordinal)) changes.Add("email");
        if (target.Role != newRole) changes.Add($"vai trò → {DisplayText.Role(newRole)}");
        if (target.IsActive != newActive) changes.Add(newActive ? "mở khóa" : "khóa tài khoản");

        target.DisplayName = trimmedName;
        target.Email = trimmedEmail;
        target.NormalizedEmail = normalized;
        target.Role = newRole;
        target.IsActive = newActive;
        if (!string.IsNullOrEmpty(newPassword))
        {
            target.PasswordHash = hasher.HashPassword(target, newPassword);
            changes.Add("đặt lại mật khẩu");
        }

        await db.SaveChangesAsync();
        await AuditAsync(actor, "user.update", target.Email,
            changes.Count == 0 ? "Không có thay đổi" : string.Join(", ", changes));
        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetActiveAsync(ClaimsPrincipal actor, SupportUser target, bool active)
    {
        if (!CanManage(actor, target))
        {
            return OperationResult.Fail("Bạn không có quyền quản lý tài khoản này.");
        }

        if (ActorId(actor) == target.Id)
        {
            return OperationResult.Fail("Bạn không thể tự khóa tài khoản của mình.");
        }

        if (!active && target.Role == UserRoles.Admin && target.IsActive && await IsLastActiveAdminAsync(target))
        {
            return OperationResult.Fail("Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.");
        }

        if (target.IsActive == active)
        {
            return OperationResult.Ok();
        }

        target.IsActive = active;
        await db.SaveChangesAsync();
        await AuditAsync(actor, active ? "user.unlock" : "user.lock", target.Email);
        return OperationResult.Ok();
    }

    /// <summary>
    /// Permanently deletes an account together with its own tickets and assistant history.
    /// Refused when the account wrote replies on other people's tickets (lock it instead), because
    /// removing those replies would silently rewrite customers' conversations.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(ClaimsPrincipal actor, SupportUser target)
    {
        if (!CanManage(actor, target))
        {
            return OperationResult.Fail("Bạn không có quyền quản lý tài khoản này.");
        }

        if (ActorId(actor) == target.Id)
        {
            return OperationResult.Fail("Bạn không thể tự xóa tài khoản của mình.");
        }

        if (target.Role == UserRoles.Admin && target.IsActive && await IsLastActiveAdminAsync(target))
        {
            return OperationResult.Fail("Không thể xóa quản trị viên đang hoạt động cuối cùng.");
        }

        var id = target.Id;
        var email = target.Email;
        var role = target.Role;
        if (await db.TicketMessages.AnyAsync(message => message.AuthorId == id && message.Ticket.CustomerId != id))
        {
            return OperationResult.Fail(
                "Tài khoản này đã phản hồi trong yêu cầu của người khác nên không thể xóa. Hãy khóa tài khoản thay vì xóa.");
        }

        int ticketsDeleted;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var ownTicketIds = db.Tickets.Where(ticket => ticket.CustomerId == id).Select(ticket => ticket.Id);
            await db.TicketMessages.Where(message => ownTicketIds.Contains(message.TicketId)).ExecuteDeleteAsync();
            ticketsDeleted = await db.Tickets.Where(ticket => ticket.CustomerId == id).ExecuteDeleteAsync();
            await db.AssistantTurns.Where(turn => turn.UserId == id).ExecuteDeleteAsync();
            await db.Users.Where(user => user.Id == id).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        db.ChangeTracker.Clear();
        await AuditAsync(actor, "user.delete", email,
            $"Vai trò: {DisplayText.Role(role)}; đã xóa {ticketsDeleted} yêu cầu liên quan");
        return OperationResult.Ok();
    }

    public async Task<UserDetailsViewModel> BuildDetailsAsync(ClaimsPrincipal actor, SupportUser target, string portal)
    {
        var id = target.Id;
        return new UserDetailsViewModel
        {
            User = target,
            Portal = portal,
            IsSelf = ActorId(actor) == id,
            TicketCount = await db.Tickets.CountAsync(ticket => ticket.CustomerId == id),
            MessageCount = await db.TicketMessages.CountAsync(message => message.AuthorId == id),
            ConversationCount = await db.AssistantTurns.CountAsync(turn => turn.UserId == id),
            RecentTickets = await db.Tickets.AsNoTracking()
                .Where(ticket => ticket.CustomerId == id)
                .OrderByDescending(ticket => ticket.Id)
                .Take(10)
                .ToListAsync()
        };
    }

    private async Task<bool> IsLastActiveAdminAsync(SupportUser user) =>
        !await db.Users.AnyAsync(other => other.Id != user.Id && other.Role == UserRoles.Admin && other.IsActive);

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
