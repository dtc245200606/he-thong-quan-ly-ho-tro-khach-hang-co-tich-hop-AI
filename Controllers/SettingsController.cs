using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CustomerSupportAI.Controllers;
[Authorize(Roles = UserRoles.Admin)]
public sealed class SettingsController(AiSettingsStore store, AiAssistantService assistant, ChatbotSettingsStore chatbotStore,
    IEnumerable<IChatProvider> chatProviders) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var settings = store.Read();
        ViewData["Chatbot"] = ChatbotForm();
        return View(new AiSettingsForm { Model = settings.Model, HasKey = !string.IsNullOrWhiteSpace(settings.Key) });
    }
    [HttpPost]
    public IActionResult Index(AiSettingsForm form)
    {
        if (!ModelState.IsValid) { form.ApiKey = null; form.HasKey = assistant.IsConfigured; ModelState.Remove(nameof(form.ApiKey)); ViewData["Chatbot"] = ChatbotForm(); return View(form); }
        store.Save(form); TempData["Success"] = "Đã lưu cấu hình AI. API key được mã hóa trên máy chủ.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    public async Task<IActionResult> Test(CancellationToken cancellationToken)
    {
        var response = await assistant.GetReplyAsync("Trả lời chính xác: Kết nối thành công.", "", [], [], cancellationToken);
        TempData[response.Success ? "Success" : "Error"] = response.Success ? "Kết nối Gemini thành công, model đã trả lời." : response.Notice ?? "Chưa cấu hình Gemini API key. Hãy nhập key và lưu trước khi kiểm tra.";
        return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    public IActionResult Chatbot(ChatbotSettingsForm form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        chatbotStore.Save(form);
        TempData["Success"] = "Đã lưu cấu hình chatbot. API key Groq (nếu có) được mã hóa trên máy chủ.";
        return RedirectToAction(nameof(Index));
    }
    /// <summary>Sends one real request to the provider the chatbot would use now (no tools, no customer data).</summary>
    [HttpPost]
    public async Task<IActionResult> TestChatbot(CancellationToken cancellationToken)
    {
        var saved = chatbotStore.Read();
        var provider = saved.Provider == "auto"
            ? chatProviders.FirstOrDefault(item => item.Name == "groq" && item.IsConfigured) ?? chatProviders.FirstOrDefault(item => item.Name == "gemini" && item.IsConfigured)
            : chatProviders.FirstOrDefault(item => item.Name == saved.Provider);
        if (provider is null || provider.Name == "mock")
        {
            TempData["Error"] = "Chatbot đang dùng chế độ Mock (không gọi AI thật). Hãy nhập API key Groq hoặc Gemini để kiểm tra kết nối.";
            return RedirectToAction(nameof(Index));
        }
        if (!provider.IsConfigured)
        {
            TempData["Error"] = $"Chưa có API key cho {provider.Name}.";
            return RedirectToAction(nameof(Index));
        }
        var result = await provider.CompleteAsync("Bạn là công cụ kiểm tra kết nối.", [new ChatUserItem("Trả lời chính xác: Kết nối thành công.")], [], cancellationToken);
        TempData[result.Failure is null ? "Success" : "Error"] = result.Failure is null
            ? $"Kết nối chatbot với {provider.Name} thành công, model đã trả lời."
            : $"Kết nối {provider.Name} thất bại: {result.Error}";
        return RedirectToAction(nameof(Index));
    }
    private ChatbotSettingsForm ChatbotForm()
    {
        var saved = chatbotStore.Read();
        store.Read();
        ViewData["GeminiKeyUnreadable"] = store.SavedKeyUnreadable;
        ViewData["GroqKeyUnreadable"] = chatbotStore.SavedKeyUnreadable;
        return new ChatbotSettingsForm { Provider = saved.Provider, FallbackToMock = saved.FallbackToMock, GroqModel = saved.GroqModel, HasGroqKey = saved.GroqKey is not null };
    }
}
