using System.Net;
using System.Net.Mail;
using System.Text;

namespace CustomerSupportAI.Services;

/// <summary>
/// Outgoing email. Uses SMTP when <c>Smtp:Host</c> and <c>Smtp:From</c> are configured.
/// Without SMTP, Development writes each message as a text file to the outbox folder
/// (<c>Mail:OutboxDirectory</c>, default App_Data/mail-outbox) so the flow can be tried locally;
/// other environments report that email is unavailable instead of pretending to send.
/// </summary>
public sealed class EmailSender(IConfiguration configuration, IWebHostEnvironment environment, ILogger<EmailSender> logger)
{
    private string? Host => configuration["Smtp:Host"];
    private string? From => configuration["Smtp:From"];

    public bool UsesSmtp => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
    public bool UsesOutbox => !UsesSmtp && environment.IsDevelopment();
    public bool IsAvailable => UsesSmtp || UsesOutbox;

    public string OutboxDirectory => configuration["Mail:OutboxDirectory"] is { Length: > 0 } custom
        ? custom
        : Path.Combine(environment.ContentRootPath, "App_Data", "mail-outbox");

    public async Task<bool> SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        if (UsesSmtp)
        {
            try
            {
                using var message = new MailMessage(From!, to, subject, body) { BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
                using var client = new SmtpClient(Host!, int.TryParse(configuration["Smtp:Port"], out var port) ? port : 587)
                {
                    EnableSsl = !string.Equals(configuration["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase),
                    Timeout = 15000
                };
                if (!string.IsNullOrWhiteSpace(configuration["Smtp:User"]))
                {
                    client.Credentials = new NetworkCredential(configuration["Smtp:User"], configuration["Smtp:Password"]);
                }
                await client.SendMailAsync(message, cancellationToken);
                return true;
            }
            catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
            {
                // Never log the body: it contains the reset link.
                logger.LogWarning("SMTP delivery failed: {Error}", ex.GetType().Name);
                return false;
            }
        }

        if (!UsesOutbox)
        {
            return false;
        }

        Directory.CreateDirectory(OutboxDirectory);
        var safeName = string.Concat(to.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var file = Path.Combine(OutboxDirectory, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{safeName}.txt");
        await File.WriteAllTextAsync(file, $"To: {to}\nSubject: {subject}\n\n{body}\n", Encoding.UTF8, cancellationToken);
        return true;
    }
}
