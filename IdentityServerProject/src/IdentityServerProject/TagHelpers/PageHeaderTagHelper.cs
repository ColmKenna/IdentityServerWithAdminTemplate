using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace IdentityServerProject.TagHelpers;

/// <summary>
///     Carries the rich sub-heading from a nested &lt;page-sub&gt; up to its &lt;page-header&gt;.
/// </summary>
internal sealed class PageHeaderContext
{
    public IHtmlContent? Sub { get; set; }
}

/// <summary>
///     The heading block every admin page opens with: a title, an optional sub-heading, and an
///     optional action on the right.
/// </summary>
/// <remarks>
///     A TagHelper rather than a partial because the action is arbitrary child markup — a
///     _BackLink partial with its own route values on some pages, a "New X" link on others,
///     nothing on the rest. A partial would force that into a stringly-typed parameter.
///     <para>
///         Two variants, matching the two heading treatments the console already had.
///         <c>list</c> renders <c>h1.page-title</c> inside <c>div.page-header</c>;
///         <c>editor</c> renders <c>h1.hero-title</c> inside <c>div.editor-header</c>. The
///         wrapper classes share one rule; the visible difference is the heading size, which is
///         why the variant names the page's role rather than a size.
///     </para>
///     <example>
///         <code>
///         &lt;page-header title="Clients" sub="OAuth/OIDC client applications"&gt;
///             &lt;a class="btn-primary" asp-page="./Create"&gt;New client&lt;/a&gt;
///         &lt;/page-header&gt;
///         </code>
///     </example>
/// </remarks>
[HtmlTargetElement("page-header")]
public class PageHeaderTagHelper : TagHelper
{
    /// <summary>Heading text. Encoded — pass model values directly.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    ///     Plain-text sub-heading. For a sub-heading containing markup, nest a
    ///     &lt;page-sub&gt; element instead; it wins if both are supplied.
    /// </summary>
    public string? Sub { get; set; }

    /// <summary><c>list</c> (default) or <c>editor</c>.</summary>
    public string Variant { get; set; } = "list";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var headerContext = new PageHeaderContext();

        // Set before the children run: TagHelperContext.Items flows parent -> child, so this is
        // how <page-sub> hands its content back.
        context.Items[typeof(PageHeaderContext)] = headerContext;
        TagHelperContent action = await output.GetChildContentAsync();

        bool isEditor = string.Equals(Variant, "editor", StringComparison.OrdinalIgnoreCase);

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", isEditor ? "editor-header" : "page-header");

        var content = new HtmlContentBuilder();
        content.AppendHtml("<div>");
        content.AppendHtml(isEditor ? "<h1 class=\"hero-title\">" : "<h1 class=\"page-title\">");
        content.Append(Title);
        content.AppendHtml("</h1>");

        if (headerContext.Sub is not null)
        {
            content.AppendHtml("<p class=\"page-sub\">").AppendHtml(headerContext.Sub).AppendHtml("</p>");
        }
        else if (!string.IsNullOrEmpty(Sub))
        {
            content.AppendHtml("<p class=\"page-sub\">").Append(Sub).AppendHtml("</p>");
        }

        content.AppendHtml("</div>");
        content.AppendHtml(action);

        output.Content.SetHtmlContent(content);
    }
}

/// <summary>
///     A sub-heading that contains markup — a &lt;code&gt; span, a &lt;strong&gt; client name.
///     Renders nothing itself; its content is placed by the parent &lt;page-header&gt;.
/// </summary>
[HtmlTargetElement("page-sub", ParentTag = "page-header")]
public class PageSubTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (context.Items.TryGetValue(typeof(PageHeaderContext), out object? item) &&
            item is PageHeaderContext headerContext)
        {
            headerContext.Sub = await output.GetChildContentAsync();
        }

        output.SuppressOutput();
    }
}
