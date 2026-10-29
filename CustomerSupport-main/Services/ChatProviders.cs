using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CustomerSupportAI.Services;

/// <summary>A backend tool the model may request. Parameters use JSON Schema property definitions.</summary>
public sealed record ChatToolDefinition(string Name, string Description,
    IReadOnlyDictionary<string, JsonElement> Properties, IReadOnlyList<string> Required);

public sealed record ChatToolCall(string Id, string Name, JsonElement Arguments);

/// <summary>Provider-neutral conversation item. Only redacted text ever goes into these.</summary>
public abstract record ChatItem;
public sealed record ChatUserItem(string Text) : ChatItem;
/// <param name="ProviderState">Raw assistant content to echo back verbatim within one tool loop (thinking signatures etc.).</param>
public sealed record ChatAssistantItem(string Text, IReadOnlyList<ChatToolCall> ToolCalls, object? ProviderState = null) : ChatItem;
public sealed record ChatToolResultItem(string CallId, string Name, string ResultJson) : ChatItem;

/// <summary>Failure is null on success; then Text and/or ToolCalls are set.</summary>
public sealed record ChatProviderResult(string? Text, IReadOnlyList<ChatToolCall> ToolCalls, object? ProviderState,
    string? Failure = null, string? Error = null)
{
    public static ChatProviderResult Fail(string failure, string error) => new(null, [], null, failure, error);
}

public interface IChatProvider
{
    /// <summary>"gemini", "groq" or "mock".</summary>
    string Name { get; }
    bool IsConfigured { get; }
    Task<ChatProviderResult> CompleteAsync(string system, IReadOnlyList<ChatItem> transcript,
        IReadOnlyList<ChatToolDefinition> tools, CancellationToken cancellationToken);
}

/// <summary>Google Gemini function calling over REST, using the key and model saved at /Settings.</summary>
public sealed class GeminiChatProvider(HttpClient httpClient, AiSettingsStore settings, ILogger<GeminiChatProvider> logger) : IChatProvider
{
    public string Name => "gemini";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.Read().Key);

    public async Task<ChatProviderResult> CompleteAsync(string system, IReadOnlyList<ChatItem> transcript,
        IReadOnlyList<ChatToolDefinition> tools, CancellationToken cancellationToken)
    {
        var (model, key) = settings.Read();
        if (string.IsNullOrWhiteSpace(key)) return ChatProviderResult.Fail("config", "Gemini chưa có API key.");
        var contents = new List<object>();
        for (var i = 0; i < transcript.Count; i++)
        {
            switch (transcript[i])
            {
                case ChatUserItem user:
                    contents.Add(new { role = "user", parts = new object[] { new { text = user.Text } } });
                    break;
                case ChatAssistantItem { ProviderState: JsonElement raw }:
                    contents.Add(raw);
                    break;
                case ChatAssistantItem assistant:
                    contents.Add(new { role = "model", parts = new object[] { new { text = assistant.Text } } });
                    break;
                case ChatToolResultItem:
                    // All results of one round go back in a single user turn.
                    var parts = new List<object>();
                    for (; i < transcript.Count && transcript[i] is ChatToolResultItem result; i++)
                        parts.Add(new { functionResponse = FunctionResponse(result) });
                    i--;
                    contents.Add(new { role = "user", parts });
                    break;
            }
        }
        var declarations = tools.Select(tool => new
        {
            name = tool.Name,
            description = tool.Description,
            parameters = new { type = "object", properties = tool.Properties, required = tool.Required }
        });
        var generationConfig = new Dictionary<string, object> { ["temperature"] = 0.2, ["maxOutputTokens"] = 2048 };
        if (model.StartsWith("gemini-3", StringComparison.Ordinal)) generationConfig["thinkingConfig"] = new { thinkingLevel = "low" };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        request.Headers.Add("x-goog-api-key", key);
        var body = new Dictionary<string, object>
        {
            ["systemInstruction"] = new { parts = new[] { new { text = system } } },
            ["contents"] = contents,
            ["generationConfig"] = generationConfig
        };
        if (tools.Count > 0) body["tools"] = new[] { new { functionDeclarations = declarations } };
        request.Content = JsonContent.Create(body);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini chat returned HTTP {StatusCode}", (int)response.StatusCode);
                return HttpFailure(response.StatusCode);
            }
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            var root = document!.RootElement;
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
                return root.TryGetProperty("promptFeedback", out _)
                    ? ChatProviderResult.Fail("blocked", "Gemini từ chối xử lý nội dung này.")
                    : ChatProviderResult.Fail("empty", "Gemini không trả về nội dung.");
            var candidate = candidates[0];
            var finish = candidate.TryGetProperty("finishReason", out var f) ? f.GetString() : null;
            if (finish is "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII")
                return ChatProviderResult.Fail("blocked", "Gemini từ chối xử lý nội dung này.");
            if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var partsElement))
                return ChatProviderResult.Fail("empty", "Gemini không trả về nội dung.");
            var texts = new List<string>();
            var calls = new List<ChatToolCall>();
            foreach (var part in partsElement.EnumerateArray())
            {
                if (part.TryGetProperty("functionCall", out var call))
                {
                    var id = call.TryGetProperty("id", out var callId) ? callId.GetString() ?? "" : "";
                    var args = call.TryGetProperty("args", out var a) ? a.Clone() : JsonSerializer.SerializeToElement(new { });
                    calls.Add(new(id, call.GetProperty("name").GetString() ?? "", args));
                }
                else if ((!part.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True) &&
                         part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    texts.Add(text.GetString()!);
                }
            }
            var reply = string.Join("\n", texts).Trim();
            if (calls.Count == 0 && reply.Length == 0) return ChatProviderResult.Fail("empty", "Gemini không trả về nội dung.");
            // The raw content keeps thought signatures, which Gemini requires when the function results are sent back.
            return new(reply.Length == 0 ? null : reply, calls, content.Clone());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ChatProviderResult.Fail("timeout", "Gemini phản hồi quá chậm."); }
        catch (HttpRequestException) { return ChatProviderResult.Fail("network", "Không kết nối được tới Gemini."); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or NullReferenceException)
        { return ChatProviderResult.Fail("json", "Phản hồi của Gemini không hợp lệ."); }
    }

    private static object FunctionResponse(ChatToolResultItem result)
    {
        using var parsed = JsonDocument.Parse(result.ResultJson);
        var response = new { result = parsed.RootElement.Clone() };
        return result.CallId.Length > 0
            ? new { id = result.CallId, name = result.Name, response }
            : new { name = result.Name, response };
    }

    private static ChatProviderResult HttpFailure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ChatProviderResult.Fail("auth", "API key Gemini chưa hợp lệ hoặc chưa được cấp quyền."),
        HttpStatusCode.TooManyRequests => ChatProviderResult.Fail("quota", "Gemini đang vượt hạn mức hoặc chưa có quota."),
        HttpStatusCode.BadRequest or HttpStatusCode.NotFound => ChatProviderResult.Fail("config", "Model hoặc cấu hình Gemini chưa hợp lệ."),
        _ => ChatProviderResult.Fail("unavailable", $"Gemini tạm thời không khả dụng (HTTP {(int)status}).")
    };
}

/// <summary>
/// Groq through its OpenAI-compatible Chat Completions API (function calling), using the key and model saved at
/// /Settings (or GROQ_API_KEY).
/// </summary>
public sealed class GroqChatProvider(HttpClient httpClient, ChatbotSettingsStore settings, ILogger<GroqChatProvider> logger) : IChatProvider
{
    public const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
    public string Name => "groq";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.Read().GroqKey);

    public async Task<ChatProviderResult> CompleteAsync(string system, IReadOnlyList<ChatItem> transcript,
        IReadOnlyList<ChatToolDefinition> tools, CancellationToken cancellationToken)
    {
        var saved = settings.Read();
        if (string.IsNullOrWhiteSpace(saved.GroqKey)) return ChatProviderResult.Fail("config", "Groq chưa có API key.");
        var messages = new List<object> { new { role = "system", content = system } };
        foreach (var item in transcript)
        {
            switch (item)
            {
                case ChatUserItem user:
                    messages.Add(new { role = "user", content = user.Text });
                    break;
                case ChatAssistantItem { ToolCalls.Count: > 0 } assistant:
                    messages.Add(new
                    {
                        role = "assistant",
                        content = assistant.Text,
                        tool_calls = assistant.ToolCalls.Select(call => new
                        {
                            id = call.Id,
                            type = "function",
                            function = new { name = call.Name, arguments = call.Arguments.GetRawText() }
                        })
                    });
                    break;
                case ChatAssistantItem assistant:
                    messages.Add(new { role = "assistant", content = assistant.Text });
                    break;
                case ChatToolResultItem result:
                    messages.Add(new { role = "tool", tool_call_id = result.CallId, name = result.Name, content = result.ResultJson });
                    break;
            }
        }
        var body = new Dictionary<string, object>
        {
            ["model"] = saved.GroqModel,
            ["messages"] = messages,
            ["temperature"] = 0.2,
            ["max_completion_tokens"] = 2048
        };
        if (tools.Count > 0)
        {
            body["tools"] = tools.Select(tool => new
            {
                type = "function",
                function = new { name = tool.Name, description = tool.Description, parameters = new { type = "object", properties = tool.Properties, required = tool.Required } }
            });
            body["tool_choice"] = "auto";
        }
        // Reasoning models: keep thinking short for chat replies.
        if (saved.GroqModel.StartsWith("openai/gpt-oss", StringComparison.Ordinal)) body["reasoning_effort"] = "low";
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", saved.GroqKey);
        request.Content = JsonContent.Create(body);
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Groq chat returned HTTP {StatusCode}", (int)response.StatusCode);
                return response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ChatProviderResult.Fail("auth", "API key Groq chưa hợp lệ hoặc chưa được cấp quyền."),
                    HttpStatusCode.TooManyRequests => ChatProviderResult.Fail("quota", "Groq đang vượt hạn mức (rate limit) hoặc hết quota."),
                    HttpStatusCode.NotFound => ChatProviderResult.Fail("config", "Model Groq không tồn tại hoặc không được phép."),
                    HttpStatusCode.BadRequest => ChatProviderResult.Fail("config", "Groq từ chối yêu cầu (model hoặc lời gọi công cụ không hợp lệ)."),
                    _ => ChatProviderResult.Fail("unavailable", $"Groq tạm thời không khả dụng (HTTP {(int)response.StatusCode}).")
                };
            }
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
            var choices = document!.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return ChatProviderResult.Fail("empty", "Groq không trả về nội dung.");
            var message = choices[0].GetProperty("message");
            var text = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString()!.Trim() : "";
            var calls = new List<ChatToolCall>();
            if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in toolCalls.EnumerateArray())
                {
                    var function = call.GetProperty("function");
                    calls.Add(new(call.GetProperty("id").GetString() ?? "", function.GetProperty("name").GetString() ?? "",
                        ParseArguments(function.TryGetProperty("arguments", out var args) ? args : default)));
                }
            }
            if (calls.Count == 0 && text.Length == 0) return ChatProviderResult.Fail("empty", "Groq không trả về nội dung.");
            return new(text.Length == 0 ? null : text, calls, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return ChatProviderResult.Fail("timeout", "Groq phản hồi quá chậm."); }
        catch (HttpRequestException) { return ChatProviderResult.Fail("network", "Không kết nối được tới Groq."); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or NullReferenceException)
        { return ChatProviderResult.Fail("json", "Phản hồi của Groq không hợp lệ."); }
    }

    /// <summary>OpenAI-style arguments arrive as a JSON string; malformed JSON becomes a non-object the backend rejects.</summary>
    private static JsonElement ParseArguments(JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.Object) return raw.Clone();
        if (raw.ValueKind == JsonValueKind.String)
        {
            try { using var parsed = JsonDocument.Parse(raw.GetString()!); return parsed.RootElement.Clone(); }
            catch (JsonException) { }
        }
        return JsonSerializer.SerializeToElement("invalid-arguments");
    }
}

/// <summary>
/// Offline provider for development, tests and fallback. It is not an AI: it picks a tool with simple keyword rules,
/// then words the reply from the real tool result. Because it calls the same backend tools, anything it reports as
/// saved really was saved.
/// </summary>
public sealed partial class MockChatProvider : IChatProvider
{
    public string Name => "mock";
    public bool IsConfigured => true;

    public Task<ChatProviderResult> CompleteAsync(string system, IReadOnlyList<ChatItem> transcript,
        IReadOnlyList<ChatToolDefinition> tools, CancellationToken cancellationToken)
    {
        if (transcript.Count > 0 && transcript[^1] is ChatToolResultItem)
        {
            var round = transcript.Reverse().TakeWhile(item => item is ChatToolResultItem).Cast<ChatToolResultItem>().Reverse();
            return Task.FromResult(Text(string.Join("\n\n", round.Select(Describe))));
        }
        var user = transcript.OfType<ChatUserItem>().LastOrDefault()?.Text ?? "";
        var call = Plan(user, tools);
        // A bare email (or "ok") after the bot asked for one: repeat the request that needed it.
        if (call is null && IsOnlyContact(user))
        {
            var lastBot = transcript.OfType<ChatAssistantItem>().LastOrDefault()?.Text ?? "";
            if (lastBot.Contains("email", StringComparison.OrdinalIgnoreCase) || lastBot.Contains("xác nhận", StringComparison.OrdinalIgnoreCase))
                foreach (var earlier in transcript.OfType<ChatUserItem>().Reverse().Skip(1).Take(4))
                    if (Plan(earlier.Text, tools) is { Name: not "search_faq" } retry) { call = retry; break; }
        }
        call ??= Tool("search_faq", new { query = user });
        return Task.FromResult(new ChatProviderResult(null, [call], null));
    }

    private static ChatToolCall? Plan(string text, IReadOnlyList<ChatToolDefinition> tools)
    {
        var normalized = KnowledgeSearch.Normalize(text);
        ChatToolCall? call = null;
        if (new[] { "nhan vien", "gap nguoi", "nguoi that", "tu van vien", "tong dai" }.Any(normalized.Contains) || IsUrgent(normalized))
            call = Tool("handoff_to_human", new { reason = IsUrgent(normalized) ? "Khách hàng cần hỗ trợ khẩn cấp hoặc đang bức xúc." : "Khách hàng yêu cầu gặp nhân viên.", urgent = IsUrgent(normalized) });
        else if (TicketCode().Match(text) is { Success: true } code)
            call = Tool("get_ticket_status", new { ticket_id = code.Value.ToUpperInvariant() });
        else if (new[] { "tao yeu cau", "tao ticket", "mo yeu cau", "gui yeu cau", "bao loi" }.Any(normalized.Contains))
            call = Tool("create_ticket", new
            {
                title = Title(text),
                description = text.Length >= 10 ? text : "Khách hàng yêu cầu tạo yêu cầu hỗ trợ qua chatbot.",
                category = normalized.Contains("thanh toan") || normalized.Contains("hoa don") ? "Billing"
                    : normalized.Contains("bao hanh") || normalized.Contains("sua chua") ? "Warranty & repair"
                    : normalized.Contains("dang nhap") || normalized.Contains("tai khoan") || normalized.Contains("mat khau") ? "Account access"
                    : normalized.Contains("san pham") ? "Product support" : "Other",
                priority = "Trung bình"
            });
        return call is null || tools.Any(tool => tool.Name == call.Name) ? call : null;
    }

    private static bool IsUrgent(string normalized) =>
        new[] { "khan cap", "gap lap tuc", "tuc gian", "buc minh", "qua te", "lua dao", "mat tien" }.Any(normalized.Contains);

    private static bool IsOnlyContact(string text) =>
        text.Replace(PiiRedactor.EmailToken, "").Trim(' ', '.', ',', ':').Length < 12;

    private static string Title(string text)
    {
        var clean = text.Replace('\n', ' ').Trim();
        return clean.Length <= 80 ? clean : clean[..80].TrimEnd() + "…";
    }

    private static ChatToolCall Tool(string name, object args) =>
        new("mock_" + Guid.NewGuid().ToString("N")[..8], name, JsonSerializer.SerializeToElement(args));

    private static ChatProviderResult Text(string text) => new(text, [], null);

    private static string Describe(ChatToolResultItem result)
    {
        using var document = JsonDocument.Parse(result.ResultJson);
        var root = document.RootElement;
        var ok = root.TryGetProperty("ok", out var okValue) && okValue.ValueKind == JsonValueKind.True;
        var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        if (!ok && root.TryGetProperty("error", out var error) && error.GetString() == "email_required")
            return "Để tiếp tục, vui lòng cho tôi biết email liên hệ của bạn (nhập vào ô email bên dưới hoặc gửi trong tin nhắn).";
        if (!ok && root.TryGetProperty("error", out var unverified) && unverified.GetString() == "email_unverified")
            return "Vui lòng mở email và bấm liên kết xác nhận chúng tôi vừa gửi, sau đó nhắn lại yêu cầu (hoặc nhắn \"ok\") để tôi tiếp tục.";
        if (!ok) return message.Length > 0 ? message : "Tôi chưa xử lý được yêu cầu này. Bạn có muốn chuyển nhân viên hỗ trợ không?";
        switch (result.Name)
        {
            case "search_faq":
                var items = root.GetProperty("results").EnumerateArray().ToList();
                if (items.Count == 0)
                    return "Tôi chưa tìm thấy hướng dẫn phù hợp cho câu hỏi này. Bạn có thể mô tả rõ hơn, hoặc nhắn \"gặp nhân viên\" để được chuyển tới nhân viên hỗ trợ.";
                var first = items[0];
                var reply = $"Theo hướng dẫn **{first.GetProperty("title").GetString()}**:\n{first.GetProperty("content").GetString()}";
                var more = items.Skip(1).Select(item => item.GetProperty("title").GetString()).ToList();
                return more.Count == 0 ? reply : reply + "\n\nBài liên quan: " + string.Join("; ", more) + ".";
            case "get_ticket_status":
                return $"Yêu cầu {root.GetProperty("ticket_number").GetString()} hiện ở trạng thái **{root.GetProperty("status_text").GetString()}** " +
                       $"(cập nhật lúc {root.GetProperty("updated_at").GetString()}).";
            default:
                return message;
        }
    }

    [GeneratedRegex(@"\bCS-[A-Z0-9-]{4,24}\b", RegexOptions.IgnoreCase)]
    private static partial Regex TicketCode();
}
