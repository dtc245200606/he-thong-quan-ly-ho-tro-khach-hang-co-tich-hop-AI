using Microsoft.AspNetCore.WebUtilities;

namespace CustomerSupportAI.ViewModels;

/// <summary>One page of a server-side list; rendered by the shared _Pagination partial.</summary>
public sealed record PageInfo(int Page, int PageSize, int TotalItems)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public int FirstItem => TotalItems == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastItem => Math.Min(Page * PageSize, TotalItems);
    public int Skip => (Page - 1) * PageSize;

    /// <summary>Clamps a requested page number into the valid range for <paramref name="totalItems"/>.</summary>
    public static PageInfo For(int requestedPage, int pageSize, int totalItems)
    {
        var last = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        return new PageInfo(Math.Clamp(requestedPage, 1, last), pageSize, totalItems);
    }
}

/// <summary>Sortable column header: toggles between "field" (ascending) and "field_desc".</summary>
public sealed record SortHeader(string Label, string Field, string Current)
{
    public bool IsActive => Current == Field || Current == Field + "_desc";
    public bool IsDescending => Current == Field + "_desc";
    public string Next => IsActive && !IsDescending ? Field + "_desc" : Field;
    public string AriaSort => !IsActive ? "none" : IsDescending ? "descending" : "ascending";
}

public static class UserSorting
{
    /// <summary>Orders an account list by a whitelisted sort key; unknown keys fall back to newest first.</summary>
    public static IQueryable<Models.SupportUser> Apply(IQueryable<Models.SupportUser> query, string sort) => sort switch
    {
        "id" => query.OrderBy(user => user.Id),
        "name" => query.OrderBy(user => user.DisplayName).ThenBy(user => user.Id),
        "name_desc" => query.OrderByDescending(user => user.DisplayName).ThenBy(user => user.Id),
        "email" => query.OrderBy(user => user.Email),
        "email_desc" => query.OrderByDescending(user => user.Email),
        "role" => query.OrderBy(user => user.Role).ThenBy(user => user.Id),
        "role_desc" => query.OrderByDescending(user => user.Role).ThenBy(user => user.Id),
        _ => query.OrderByDescending(user => user.Id)
    };
}

public static class QueryUrl
{
    /// <summary>Current path and query string with the given keys replaced (null removes a key).</summary>
    public static string With(HttpRequest request, params (string Key, string? Value)[] changes)
    {
        var values = request.Query
            .Where(pair => !changes.Any(change => string.Equals(change.Key, pair.Key, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)))
            .Concat(changes.Where(change => !string.IsNullOrEmpty(change.Value))
                .Select(change => new KeyValuePair<string, string?>(change.Key, change.Value)));
        return QueryHelpers.AddQueryString(request.PathBase + request.Path, values);
    }
}
