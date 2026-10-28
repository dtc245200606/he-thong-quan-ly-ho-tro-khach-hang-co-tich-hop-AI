using System.Security.Cryptography;
using System.Text;
using CustomerSupportAI.Models;
using Microsoft.AspNetCore.DataProtection;

namespace CustomerSupportAI.Services;

/// <summary>
/// Stateless password-reset tokens: a Data Protection payload of user id + password stamp, valid for
/// <see cref="Lifetime"/>. Because the stamp is derived from the current password hash, a token stops
/// working as soon as the password changes, so each link can be used once. No database table needed.
/// </summary>
public sealed class PasswordResetTokens(IDataProtectionProvider provider)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector protector =
        provider.CreateProtector("CustomerSupport.PasswordReset.v1").ToTimeLimitedDataProtector();

    public string Create(SupportUser user) => protector.Protect($"{user.Id}|{Stamp(user.PasswordHash)}", Lifetime);

    /// <summary>The user id the token was issued for, or null when it is malformed, tampered with or expired.</summary>
    public (int UserId, string Stamp)? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var parts = protector.Unprotect(token, out _).Split('|');
            return parts.Length == 2 && int.TryParse(parts[0], out var id) ? (id, parts[1]) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Short fingerprint of a password hash; also stored in the sign-in cookie to end other sessions after a password change.</summary>
    public static string Stamp(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)))[..16];
}
