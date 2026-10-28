using Microsoft.AspNetCore.Mvc.Razor.Infrastructure;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace CustomerSupportAI.TagHelpers;

/// <summary>
/// Renders a Lucide icon from the local sprite: <c>&lt;icon name="ticket" /&gt;</c>.
/// Icons are decorative; the surrounding element carries the accessible text.
/// </summary>
[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconTagHelper(IFileVersionProvider versions) : TagHelper
{
    private const string Sprite = "/img/icons.svg";

    public string Name { get; set; } = string.Empty;

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var href = versions.AddFileVersionToPath(ViewContext.HttpContext.Request.PathBase, Sprite);
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        var extra = output.Attributes.TryGetAttribute("class", out var existing) ? " " + existing.Value : "";
        output.Attributes.SetAttribute("class", "icon" + extra);
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("focusable", "false");
        output.Content.SetHtmlContent($"<use href=\"{href}#{Name}\"></use>");
    }
}
