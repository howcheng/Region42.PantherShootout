using Microsoft.AspNetCore.Razor.TagHelpers;
using Region42.PantherShootout.Web.Services;

namespace Region42.PantherShootout.Web.TagHelpers;

/// <summary>
/// Automatically adds the current request's Content Security Policy <c>nonce</c> attribute
/// to all Razor <c>&lt;script&gt;</c> elements so scripts execute under <c>script-src 'strict-dynamic'</c>.
/// Set <c>asp-add-nonce="false"</c> on an element to opt out.
/// </summary>
[HtmlTargetElement("script")]
public class CspNonceScriptTagHelper : TagHelper
{
	public const string NonceAttributeName = "nonce";

	private readonly ICspNonceService _cspNonceService;

	public CspNonceScriptTagHelper(ICspNonceService cspNonceService)
	{
		ArgumentNullException.ThrowIfNull(cspNonceService);

		_cspNonceService = cspNonceService;
	}

	/// <summary>
	/// Runs after built-in ASP.NET Core tag helpers (<c>UrlResolutionTagHelper</c>, <c>ScriptTagHelper</c>,
	/// and <c>ImportMapTagHelper</c>) so suppressed tags are skipped and rendered tags receive a nonce.
	/// </summary>
	public override int Order => 100;

	/// <summary>
	/// Gets or sets whether a CSP <c>nonce</c> attribute should be added to the <c>&lt;script&gt;</c> tag.
	/// Defaults to <see langword="true"/>.
	/// </summary>
	[HtmlAttributeName("asp-add-nonce")]
	public bool AddNonce { get; set; } = true;

	public override void Process(TagHelperContext context, TagHelperOutput output)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(output);

		if (output.TagName is null || !AddNonce || output.Attributes.ContainsName(NonceAttributeName))
		{
			return;
		}

		var nonce = _cspNonceService.GetNonce();
		if (!string.IsNullOrWhiteSpace(nonce))
		{
			output.Attributes.SetAttribute(NonceAttributeName, nonce);
		}
	}
}
