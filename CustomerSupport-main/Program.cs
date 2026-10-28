using System.Globalization;
using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Claims;

var contentRoot = Directory.GetCurrentDirectory();
var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
if (!File.Exists(Path.Combine(contentRoot, "CustomerSupportAI.csproj")) &&
    File.Exists(Path.Combine(projectRoot, "CustomerSupportAI.csproj")))
{
    contentRoot = projectRoot;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "customer-support.db")}";

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var vietnameseCulture = new CultureInfo("vi-VN");
    options.DefaultRequestCulture = new RequestCulture(vietnameseCulture);
    options.SupportedCultures = [vietnameseCulture];
    options.SupportedUICultures = [vietnameseCulture];
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "CustomerSupportAI.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Re-check the account on every request so locking, deleting or changing the role of a user
        // takes effect immediately instead of waiting for the cookie to expire.
        options.Events.OnValidatePrincipal = async context =>
        {
            var roleClaim = context.Principal?.FindFirstValue(ClaimTypes.Role);
            var valid = false;
            if (int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var current = await db.Users.AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => new { user.IsActive, user.Role, user.PasswordHash })
                    .SingleOrDefaultAsync();
                // Cookies issued before the stamp claim existed carry none and stay valid until they expire.
                var stamp = context.Principal?.FindFirstValue(CustomerSupportAI.Controllers.AccountController.PasswordStampClaim);
                valid = current is not null && current.IsActive
                    && string.Equals(current.Role, roleClaim, StringComparison.Ordinal)
                    && (stamp is null || stamp == PasswordResetTokens.Stamp(current.PasswordHash));
            }

            if (!valid)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IPasswordHasher<SupportUser>, PasswordHasher<SupportUser>>();
builder.Services.AddSingleton<AiSettingsStore>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddScoped<UserAdministrationService>();
builder.Services.AddHttpClient<AiAssistantService>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<TicketAiService>();
builder.Services.AddSingleton<PasswordResetTokens>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("assistant", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration, app.Logger);

// One-off maintenance commands: add or remove the fictional demo data, then exit without serving.
if (args.Contains("--seed-demo") || args.Contains("--remove-demo"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var appData = Path.Combine(app.Environment.ContentRootPath, "App_Data");
    Console.WriteLine(args.Contains("--remove-demo")
        ? await DemoData.RemoveAsync(db, appData)
        : await DemoData.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<SupportUser>>(), appData));
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
