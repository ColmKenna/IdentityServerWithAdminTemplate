using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace IdentityServerProject.TagHelpers;

/// <summary>
///     Carries the footer action from a nested &lt;card-action&gt; up to its &lt;detail-card&gt;.
/// </summary>
internal sealed class DetailCardContext
{
    public IHtmlContent? Action { get; set; }
}

/// <summary>
///     One card on the client detail grid: an icon and heading, a body of fields, and a
///     link through to the editor page that owns them.
/// </summary>
/// <remarks>
///     A TagHelper rather than a partial because the body is arbitrary child markup — field
///     rows on most cards, a row of scope chips on another. The action is nested rather than
///     passed as attributes so its link keeps using the routing TagHelpers, which resolve
///     <c>asp-page</c> against the page being rendered.
///     <example>
///         <code>
///         &lt;detail-card id="card-basics" icon="card-basics" title="Basics"&gt;
///             &lt;div class="field-group"&gt;…&lt;/div&gt;
///             &lt;card-action&gt;
///                 &lt;a asp-page="./Basics" class="card-action-link"&gt;Configure details &amp;rarr;&lt;/a&gt;
///             &lt;/card-action&gt;
///         &lt;/detail-card&gt;
///         </code>
///     </example>
/// </remarks>
[HtmlTargetElement("detail-card")]
public class DetailCardTagHelper(IHtmlHelper htmlHelper) : TagHelper
{
    private readonly IHtmlHelper _htmlHelper = htmlHelper;

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    /// <summary>Element id, so the card can be linked to and asserted against.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Icon name, resolved by the shared _AdminIcon partial.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Heading text. Encoded — pass model values directly.</summary>
    public string Title { get; set; } = string.Empty;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var cardContext = new DetailCardContext();

        // Set before the children run: TagHelperContext.Items flows parent -> child, so this
        // is how <card-action> hands its content back.
        context.Items[typeof(DetailCardContext)] = cardContext;
        TagHelperContent body = await output.GetChildContentAsync();

        ((IViewContextAware)_htmlHelper).Contextualize(ViewContext);
        IHtmlContent icon = await _htmlHelper.PartialAsync("_AdminIcon", (Icon, "card-icon"));

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "panel detail-card");
        output.Attributes.SetAttribute("id", Id);

        var content = new HtmlContentBuilder();
        content.AppendHtml("<div class=\"card-header\">");
        content.AppendHtml(icon);
        content.AppendHtml("<h2>").Append(Title).AppendHtml("</h2>");
        content.AppendHtml("</div>");
        content.AppendHtml("<div class=\"card-body\">").AppendHtml(body).AppendHtml("</div>");

        if (cardContext.Action is not null)
        {
            content.AppendHtml("<div class=\"card-footer\">")
                .AppendHtml(cardContext.Action)
                .AppendHtml("</div>");
        }

        output.Content.SetHtmlContent(content);
    }
}

/// <summary>
///     The link in a detail card's footer. Renders nothing itself; its content is placed by
///     the parent &lt;detail-card&gt;.
/// </summary>
[HtmlTargetElement("card-action", ParentTag = "detail-card")]
public class CardActionTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (context.Items.TryGetValue(typeof(DetailCardContext), out object? item) &&
            item is DetailCardContext cardContext)
        {
            cardContext.Action = await output.GetChildContentAsync();
        }

        output.SuppressOutput();
    }
}
