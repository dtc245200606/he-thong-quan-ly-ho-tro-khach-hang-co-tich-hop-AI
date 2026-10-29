using System.Text.RegularExpressions;

namespace CustomerSupportAI.Services;

public sealed record RedactionResult(string Text, int Count);

/// <summary>
/// Removes personal data before text reaches an AI provider: email, phone, card/account/ID numbers,
/// identity-document numbers, passwords and OTP codes. Applied to customer messages, chat history and tool results.
/// </summary>
public static partial class PiiRedactor
{
    public const string EmailToken = "[ĐÃ_ẨN_EMAIL]";
    public const string PhoneToken = "[ĐÃ_ẨN_SĐT]";
    public const string NumberToken = "[ĐÃ_ẨN_SỐ]";
    public const string SecretToken = "[ĐÃ_ẨN_BÍ_MẬT]";

    public static RedactionResult Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new(text ?? string.Empty, 0);
        var count = 0;
        string Count(Match match, string token) { count++; return token; }
        string Keep(Match match, string token) { count++; return match.Groups["label"].Value + token; }

        text = Password().Replace(text, m => Keep(m, SecretToken));
        text = OneTimeCode().Replace(text, m => Keep(m, SecretToken));
        text = IdentityDocument().Replace(text, m => Keep(m, NumberToken));
        text = Email().Replace(text, m => Count(m, EmailToken));
        text = LongDigits().Replace(text, m => Count(m, NumberToken));
        text = Phone().Replace(text, m => Count(m, PhoneToken));
        return new(text, count);
    }

    /// <summary>First syntactically valid email address in the text, or null.</summary>
    public static string? FindEmail(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = Email().Match(text);
        return match.Success && IsValidEmail(match.Value) ? match.Value.Trim().TrimEnd('.') : null;
    }

    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= 320 && StrictEmail().IsMatch(email.Trim());

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Email();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$")]
    private static partial Regex StrictEmail();

    // Card, account or citizen-ID numbers: 12+ digits, optionally grouped by spaces or dashes.
    [GeneratedRegex(@"\b\d(?:[ -]?\d){11,}\b")]
    private static partial Regex LongDigits();

    [GeneratedRegex(@"(?<!\w)(?:\+84|0)(?:[\s.-]?\d){9,10}(?!\w)")]
    private static partial Regex Phone();

    // "CMND 123456789", "số CCCD: 0790...", "hộ chiếu B1234567".
    [GeneratedRegex(@"(?<label>\b(?:cmnd|cccd|cmt|căn cước|hộ chiếu|passport)\b[^\d\n]{0,12})[A-Z]?\d{6,12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex IdentityDocument();

    // "mật khẩu là abc123", "password: x" (needs an explicit "là/:/=" so "quên mật khẩu tài khoản" stays intact).
    [GeneratedRegex(@"(?<label>\b(?:mật khẩu|mat khau|password|passcode|pass|mk)\b(?:\s+(?:của\s+(?:tôi|mình|em)|hiện tại|mới|cũ))?\s*(?:là|:|=)\s*)(?!\[)[^\s,;]{3,64}", RegexOptions.IgnoreCase)]
    private static partial Regex Password();

    // "OTP 123456", "mã xác thực là 1234", "CVV: 123", "mã PIN 1234".
    [GeneratedRegex(@"(?<label>\b(?:mã otp|otp|mã xác thực|mã xác nhận|cvv|cvc|mã pin)\b\s*(?:là|:|=)?\s*)\d{3,8}\b", RegexOptions.IgnoreCase)]
    private static partial Regex OneTimeCode();
}

public sealed record SafetyResult(string Text, bool Blocked, bool Warned);

/// <summary>
/// Last check on a chatbot reply before it reaches the customer: no requests for passwords/OTP/card data,
/// no unconfirmed refund/compensation/discount promises, and no claim of saving data the backend did not save.
/// </summary>
public static partial class ChatReplyGuard
{
    public const string BlockedReply =
        "Vì lý do bảo mật, chúng tôi không bao giờ yêu cầu mật khẩu, mã OTP, mã CVV hay số thẻ đầy đủ. " +
        "Vui lòng không gửi các thông tin này. Bạn có thể mô tả vấn đề để tôi hỗ trợ hoặc chuyển nhân viên.";

    public const string RefundNotice =
        "Lưu ý: các vấn đề hoàn tiền, bồi thường hoặc giảm giá cần nhân viên xem xét và xác nhận; " +
        "nội dung trên không phải là cam kết của chúng tôi.";

    public const string UnconfirmedSaveReply =
        "Tôi chưa thể xác nhận đã lưu yêu cầu của bạn trên hệ thống. Bạn vui lòng thử lại hoặc yêu cầu chuyển nhân viên.";

    /// <param name="dataSaved">True when a create/handoff tool succeeded during this reply.</param>
    public static SafetyResult Check(string reply, bool dataSaved)
    {
        if (AsksForSecret().IsMatch(reply)) return new(BlockedReply, true, false);
        if (!dataSaved && ClaimsSaved().IsMatch(reply)) return new(UnconfirmedSaveReply, true, false);
        if (PromisesMoney().IsMatch(reply) && !reply.Contains(RefundNotice, StringComparison.Ordinal))
            return new(reply.TrimEnd() + "\n\n" + RefundNotice, false, true);
        return new(reply, false, false);
    }

    // A request to hand the secret to us ("cung cấp/gửi/nhắn/cho biết ... mật khẩu/OTP/CVV/số thẻ") rather than a
    // warning ("không gửi mật khẩu") or a self-service step ("nhập mật khẩu mới tại trang Đổi mật khẩu").
    [GeneratedRegex(@"(?<!không\s)(?<!đừng\s)(?<!không bao giờ\s)(?<!chưa\s)\b(?:cung cấp|gửi|nhắn|cho (?:tôi|chúng tôi|mình|em) biết|đọc)\s+(?:(?:giúp|cho)\s+(?:tôi|chúng tôi|mình|em)\s+)?(?:lại\s+)?(?:mật khẩu|mã otp|otp|mã xác thực|cvv|cvc|mã pin|số thẻ(?: đầy đủ)?)", RegexOptions.IgnoreCase)]
    private static partial Regex AsksForSecret();

    [GeneratedRegex(@"\b(?:đã|vừa)\s+(?:tạo|lưu|ghi nhận|chuyển)\s+(?:thành công\s+)?(?:yêu cầu|ticket|phiếu)|\bđã chuyển (?:cho|tới|đến) nhân viên", RegexOptions.IgnoreCase)]
    private static partial Regex ClaimsSaved();

    [GeneratedRegex(@"\b(?:sẽ|đã|được|chắc chắn|cam kết)\b[^.\n]{0,40}\b(?:hoàn tiền|hoàn lại tiền|bồi thường|đền bù|giảm giá|khuyến mãi|miễn phí)", RegexOptions.IgnoreCase)]
    private static partial Regex PromisesMoney();
}
