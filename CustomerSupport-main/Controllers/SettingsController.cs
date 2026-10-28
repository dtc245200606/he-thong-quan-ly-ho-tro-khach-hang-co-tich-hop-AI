using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CustomerSupportAI.Controllers;
[Authorize(Roles = UserRoles.Admin)]
public sealed class SettingsController(AiSettingsStore store, AiAssistantService assistant) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var settings = store.Read();
        return View(new AiSettingsForm { Model = settings.Model, HasKey = !string.IsNullOrWhiteSpace(settings.Key) });
    }
    [HttpPost]
    public IActionResult Index(AiSettingsForm form)
    {
        if (!ModelState.IsValid) { form.ApiKey = null; form.HasKey = assistant.IsConfigured; ModelState.Remove(nameof(form.ApiKey)); return View(form); }
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
}
