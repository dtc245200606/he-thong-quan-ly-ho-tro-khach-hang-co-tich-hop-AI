using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
namespace CustomerSupportAI.Services;
public sealed class AiSettingsForm
{
    [Required, RegularExpression(@"^[a-zA-Z0-9._-]{1,100}$", ErrorMessage = "Tên model không hợp lệ.")]
    public string Model { get; set; } = "gemini-3.8-flash";
    [StringLength(500)] public string? ApiKey { get; set; }
    public bool RemoveKey { get; set; }
    public bool HasKey { get; set; }
}
public sealed class AiSettingsStore
{
    private readonly IConfiguration configuration;
    private readonly IDataProtector protector;
    private readonly string path;
    private readonly object sync = new();
    public AiSettingsStore(IConfiguration configuration, IWebHostEnvironment environment, IDataProtectionProvider protection)
    {
        this.configuration = configuration;
        protector = protection.CreateProtector("CustomerSupport.Gemini.Settings.v1");
        path = Path.Combine(configuration["AiSettings:StorageDirectory"] ?? Path.Combine(environment.ContentRootPath, "App_Data"), "gemini-settings.json");
    }
    public (string Model, string? Key) Read()
    {
        lock (sync)
        {
            var saved = File.Exists(path) ? JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path)) : null;
            var key = saved?.ProtectedKey is { Length: > 0 } encrypted ? protector.Unprotect(encrypted)
                : saved?.DisableEnvironmentKey == true ? null : configuration["Gemini:ApiKey"] ?? configuration["GEMINI_API_KEY"] ?? configuration["GOOGLE_API_KEY"];
            return (saved?.Model ?? configuration["Gemini:Model"] ?? "gemini-3.8-flash", key);
        }
    }
    public void Save(AiSettingsForm form)
    {
        lock (sync)
        {
            var current = Read();
            var key = form.RemoveKey ? null : string.IsNullOrWhiteSpace(form.ApiKey) ? current.Key : form.ApiKey.Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new StoredSettings(form.Model.Trim(), key is null ? null : protector.Protect(key), form.RemoveKey)));
            File.Move(temporary, path, true);
        }
    }
    private sealed record StoredSettings(string Model, string? ProtectedKey, bool DisableEnvironmentKey);
}
