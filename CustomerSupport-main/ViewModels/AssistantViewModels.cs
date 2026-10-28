using System.ComponentModel.DataAnnotations;

namespace CustomerSupportAI.ViewModels;

/// <summary>Tin nhắn gửi tới trợ lý hỗ trợ.</summary>
public sealed record AssistantMessageRequest
{
    [Required(ErrorMessage = "Vui lòng nhập câu hỏi."), StringLength(2000, MinimumLength = 1, ErrorMessage = "Câu hỏi không được vượt quá 2.000 ký tự.")]
    public required string Message { get; init; }
}

/// <summary>Câu trả lời do trợ lý hỗ trợ trả về.</summary>
public sealed record AssistantMessageResponse(string Reply);