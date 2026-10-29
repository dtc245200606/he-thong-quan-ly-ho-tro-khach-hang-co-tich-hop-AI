using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace CustomerSupportAI.Services;

/// <summary>Limits for the public chatbot, bound from the "Chatbot" configuration section.</summary>
public sealed class ChatbotOptions
{
    public int MessagesPerMinutePerSession { get; set; } = 10;
    public int SessionsPerMinutePerIp { get; set; } = 20;
    public int MaxMessageLength { get; set; } = 1000;
    public int MaxToolRounds { get; set; } = 4;
    /// <summary>Messages of earlier turns sent to the provider as context.</summary>
    public int HistoryMessages { get; set; } = 10;
}

public sealed class ChatbotSettingsForm
{
    public static readonly string[] Providers = ["auto", "groq", "gemini", "mock"];

    [Required, RegularExpression("^(auto|groq|gemini|mock)$", ErrorMessage = "Nhà cung cấp không hợp lệ.")]
    public string Provider { get; set; } = "auto";

    public bool FallbackToMock { get; set; } = true;

    // Groq model ids contain a slash, e.g. "openai/gpt-oss-120b".
    [Required, RegularExpression(@"^[a-zA-Z0-9._/-]{1,100}$", ErrorMessage = "Tên model Groq không hợp lệ.")]
    public string GroqModel { get; set; } = ChatbotSettingsStore.DefaultGroqModel;

    [StringLength(500)] public string? GroqApiKey { get; set; }
    public bool RemoveGroqKey { get; set; }
    public bool HasGroqKey { get; set; }
}

public sealed record ChatbotSettings(string Provider, bool FallbackToMock, string GroqModel, string? GroqKey);

/// <summary>
/// Chatbot provider choice and the Groq API key. Same rules as <see cref="AiSettingsStore"/>: the key is encrypted
/// with Data Protection outside wwwroot, a blank form field keeps it, and saved settings win over the environment
/// (Chatbot__Provider, Chatbot__FallbackToMock, Groq__ApiKey / GROQ_API_KEY, Groq__Model).
/// </summary>
public sealed class ChatbotSettingsStore
{
    public const string DefaultGroqModel = "openai/gpt-oss-120b";
    private readonly IConfiguration configuration;
    private readonly IDataProtector protector;
    private readonly string path;
    private readonly object sync = new();

    public ChatbotSettingsStore(IConfiguration configuration, IWebHostEnvironment environment, IDataProtectionProvider protection)
    {
        this.configuration = configuration;
        protector = protection.CreateProtector("CustomerSupport.Chatbot.Settings.v1");
        path = Path.Combine(configuration["AiSettings:StorageDirectory"] ?? Path.Combine(environment.ContentRootPath, "App_Data"), "chatbot-settings.json");
    }

    /// <summary>True when a saved Groq key exists but cannot be decrypted (key ring from another machine or folder).</summary>
    public bool SavedKeyUnreadable { get; private set; }

    public ChatbotSettings Read()
    {
        lock (sync)
        {
            // Files saved by the earlier Claude version have no Groq fields; their provider "claude" falls back to "auto".
            var saved = File.Exists(path) ? JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path)) : null;
            SavedKeyUnreadable = false;
            string? key;
            if (saved?.ProtectedGroqKey is { Length: > 0 } encrypted)
            {
                try { key = protector.Unprotect(encrypted); }
                catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException) { key = null; SavedKeyUnreadable = true; }
            }
            else key = saved?.DisableEnvironmentKey == true ? null
                : configuration["Groq:ApiKey"] is { Length: > 0 } configured ? configured : configuration["GROQ_API_KEY"];
            var provider = saved?.Provider ?? configuration["Chatbot:Provider"] ?? "auto";
            if (!ChatbotSettingsForm.Providers.Contains(provider)) provider = "auto";
            return new(provider,
                saved?.FallbackToMock ?? !string.Equals(configuration["Chatbot:FallbackToMock"], "false", StringComparison.OrdinalIgnoreCase),
                saved?.GroqModel ?? configuration["Groq:Model"] ?? DefaultGroqModel,
                string.IsNullOrWhiteSpace(key) ? null : key);
        }
    }

    public void Save(ChatbotSettingsForm form)
    {
        lock (sync)
        {
            var current = Read();
            var key = form.RemoveGroqKey ? null : string.IsNullOrWhiteSpace(form.GroqApiKey) ? current.GroqKey : form.GroqApiKey.Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new StoredSettings(form.Provider, form.FallbackToMock,
                form.GroqModel.Trim(), key is null ? null : protector.Protect(key), form.RemoveGroqKey)));
            File.Move(temporary, path, true);
        }
    }

    private sealed record StoredSettings(string Provider, bool FallbackToMock, string? GroqModel, string? ProtectedGroqKey, bool DisableEnvironmentKey);
}
