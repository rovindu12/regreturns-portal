using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

using RegReturns.ServiceDefaults.Web;

namespace RegReturns.Web.Security;

/// <summary>
/// Puts the request's Content Security Policy nonce on every <c>&lt;script&gt;</c> element a view renders
/// (ADR 0033), so views never handle the nonce themselves and a script tag cannot be forgotten.
/// </summary>
[HtmlTargetElement("script")]
public sealed class CspNonceTagHelper : TagHelper
{
    /// <summary>Gets or sets the view context (set by Razor).</summary>
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Attributes.SetAttribute("nonce", ViewContext.HttpContext.CspNonce());
    }
}
