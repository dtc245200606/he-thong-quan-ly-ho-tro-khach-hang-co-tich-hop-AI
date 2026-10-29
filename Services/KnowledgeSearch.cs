using System.Globalization;
using System.Text;
using CustomerSupportAI.Models;
namespace CustomerSupportAI.Services;
public static class KnowledgeSearch
{
    public static bool IsSmallTalk(string question)
    {
        var text = Normalize(question).Trim(' ', '?', '!', '.', ',');
        return new[] { "xin chao", "chao", "hello", "hi", "ban la ai", "ban co the giup gi", "ban giup duoc gi", "cam on", "chao ban" }.Contains(text);
    }
    public static bool IsSupportQuestion(string question) => !IsSmallTalk(question) &&
        new[] { "tai khoan", "dang nhap", "mat khau", "thanh toan", "hoa don", "bao hanh", "san pham", "yeu cau", "ho tro", "nhan vien", "giao dich", "trang thai", "cs-", "don hang", "doi tra", "hoan tien", "van chuyen", "chinh sach", "mo cua" }.Any(term => Normalize(question).Contains(term));
    public static IReadOnlyList<KnowledgeArticle> ForAssistant(IEnumerable<KnowledgeArticle> articles, string question, string? previousQuestion)
    {
        if (IsSmallTalk(question)) return [];
        var matches = Find(articles, question);
        if (IsSupportQuestion(question)) return matches;
        var followUp = new[] { "vay", "con ", "the ", "cu the", "tiep", "dieu do", "nhu the nao" }.Any(term => Normalize(question).StartsWith(term));
        return followUp && previousQuestion is not null && IsSupportQuestion(previousQuestion) ? Find(articles, previousQuestion) : [];
    }
    public static string Normalize(string text) => new string(text.ToLowerInvariant().Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ', 'd');
    public static IReadOnlyList<KnowledgeArticle> Find(IEnumerable<KnowledgeArticle> articles, string question, int limit = 3)
    {
        var words = Normalize(question).Split(new[] { ' ', ',', '.', '?', '!', ':', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !new[] { "toi", "ban", "cua", "nhu", "nao", "cho", "voi", "the", "duoc", "khong" }.Contains(w)).Distinct().ToArray();
        return articles.Select(a => new { Article = a, Score = words.Sum(w => (Normalize(a.Title).Contains(w) ? 4 : 0) + (Normalize(a.Body).Contains(w) ? 1 : 0)) })
            .Where(a => a.Score > 0).OrderByDescending(a => a.Score).ThenBy(a => a.Article.Id).Take(limit).Select(a => a.Article).ToList();
    }
}
