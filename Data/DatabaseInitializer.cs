using CustomerSupportAI.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace CustomerSupportAI.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await SupportSchema.InitializeAsync(db);

        var email = configuration["BootstrapAdmin:Email"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        // Explicit local setup option, never enabled by default in production.
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password) &&
            configuration.GetValue<bool>("BootstrapAdmin:LocalSetup") &&
            scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>().IsDevelopment() &&
            !await db.Users.AnyAsync(user => user.Role == UserRoles.Admin))
        {
            email = "admin@customersupport.local";
            if (await db.Users.AnyAsync(user => user.NormalizedEmail == email.ToUpperInvariant()))
                email = $"admin-{Guid.NewGuid():N}@customersupport.local";
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "aA1!";
            var root = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
            Directory.CreateDirectory(Path.Combine(root, "App_Data"));
            await File.WriteAllTextAsync(Path.Combine(root, "App_Data", "local-admin.txt"),
                $"Local development administrator\nEmail: {email}\nPassword: {password}\nChange the password after signing in. This file is excluded from Git.\n");
        }
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No bootstrap admin configured. Set BootstrapAdmin__Email and BootstrapAdmin__Password.");
            return;
        }

        var normalizedEmail = email.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail))
        {
            return;
        }

        var admin = new SupportUser
        {
            DisplayName = configuration["BootstrapAdmin:Name"]?.Trim() is { Length: > 0 } name
                ? name
                : "Quản trị hệ thống",
            Email = email,
            NormalizedEmail = normalizedEmail,
            Role = UserRoles.Admin
        };
        admin.PasswordHash = new PasswordHasher<SupportUser>().HashPassword(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        logger.LogInformation("Bootstrap administrator {Email} was created.", email);
    }
}
