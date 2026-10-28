using CustomerSupportAI.Data;
using CustomerSupportAI.Models;
using CustomerSupportAI.Services;
using CustomerSupportAI.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CustomerSupportAI.Controllers;
public sealed class KnowledgeController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? search, string? category)
    {
        var items = await db.KnowledgeArticles.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        if (!string.IsNullOrWhiteSpace(category)) items = items.Where(a => a.Category == category).ToList();
        if (!string.IsNullOrWhiteSpace(search)) items = KnowledgeSearch.Find(items, search, 100).ToList();
        return View(new KnowledgeViewModel(items, search?.Trim() ?? "", category ?? ""));
    }
    public async Task<IActionResult> Details(int id)
    {
        var article = await db.KnowledgeArticles.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
        return article is null ? NotFound() : View(article);
    }
    [Authorize(Roles = UserRoles.Admin)]
    [HttpGet]
    public async Task<IActionResult> Edit(int? id) => View(id is null ? new KnowledgeArticle() : await db.KnowledgeArticles.FindAsync(id) ?? new KnowledgeArticle());
    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost]
    public async Task<IActionResult> Edit([Bind("Id,Title,Category,Body")] KnowledgeArticle model)
    {
        if (!ModelState.IsValid) return View(model);
        var article = model.Id == 0 ? new KnowledgeArticle() : await db.KnowledgeArticles.FindAsync(model.Id);
        if (article is null) return NotFound();
        article.Title = model.Title.Trim(); article.Category = model.Category.Trim(); article.Body = model.Body.Trim(); article.UpdatedAt = DateTimeOffset.UtcNow;
        if (article.Id == 0) db.KnowledgeArticles.Add(article);
        await db.SaveChangesAsync(); TempData["Success"] = "Đã lưu bài hướng dẫn. Trợ lý sẽ sử dụng nội dung mới.";
        return RedirectToAction(nameof(Details), new { id = article.Id });
    }
}
