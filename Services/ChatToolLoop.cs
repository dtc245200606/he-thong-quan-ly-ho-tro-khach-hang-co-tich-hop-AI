namespace CustomerSupportAI.Services;

/// <summary>Result of one backend tool call, as returned to the model.</summary>
public sealed record ToolExecution(bool Ok, string Json, string? Error);

/// <summary>Final text, or Failure/Error when the provider failed (Text is then null).</summary>
public sealed record ToolLoopOutcome(string? Text, string? Failure, string? Error);

/// <summary>What happened during a loop, for logging and for business decisions after it (e.g. "was data saved?").</summary>
public sealed class ToolLoopTrace
{
    public int Rounds { get; set; }
    public int Redacted { get; set; }
    public List<string> Calls { get; } = [];
    public List<(string Tool, ToolExecution Result)> Executions { get; } = [];
}

/// <summary>
/// The provider-neutral tool loop shared by the website chatbot and the staff CRM assistant: ask the model,
/// run the tools it requests through <paramref name="execute"/> (which enforces permissions), redact the results,
/// and repeat until it answers, at most <paramref name="maxRounds"/> tool rounds per message.
/// </summary>
public static class ChatToolLoop
{
    public static async Task<ToolLoopOutcome> RunAsync(IChatProvider provider, string system, IReadOnlyList<ChatItem> history,
        IReadOnlyList<ChatToolDefinition> tools, int maxRounds, string limitReply, Func<ChatToolCall, Task<ToolExecution>> execute,
        ToolLoopTrace trace, ILogger logger, CancellationToken ct)
    {
        var transcript = new List<ChatItem>(history);
        var rounds = 0;
        while (true)
        {
            ChatProviderResult result;
            try
            {
                result = await provider.CompleteAsync(system, transcript, tools, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("Chat provider {Provider} threw {Type}", provider.Name, ex.GetType().Name);
                return new(null, "exception", "Nhà cung cấp AI gặp lỗi không mong muốn.");
            }
            if (result.Failure is not null) return new(null, result.Failure, result.Error);
            if (result.ToolCalls.Count == 0)
                return string.IsNullOrWhiteSpace(result.Text) ? new(null, "empty", "AI không trả về nội dung.") : new(result.Text.Trim(), null, null);
            if (rounds >= maxRounds)
            {
                trace.Calls.Add("limit:reached");
                return new(limitReply, null, null);
            }
            rounds++;
            trace.Rounds = Math.Max(trace.Rounds, rounds);
            transcript.Add(new ChatAssistantItem(result.Text ?? "", result.ToolCalls, result.ProviderState));
            foreach (var call in result.ToolCalls)
            {
                var executed = await execute(call);
                trace.Calls.Add($"{call.Name}:{(executed.Ok ? "ok" : executed.Error)}");
                trace.Executions.Add((call.Name, executed));
                var redacted = PiiRedactor.Redact(executed.Json);
                trace.Redacted += redacted.Count;
                transcript.Add(new ChatToolResultItem(call.Id, call.Name, redacted.Text));
            }
        }
    }
}
