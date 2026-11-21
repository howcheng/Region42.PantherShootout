using System.Text.RegularExpressions;

namespace Region42.PantherShootout.Web.Helpers;

/// <summary>
/// Generates CSP-compliant <c>data:image/svg+xml</c> color swatch URIs so fill colors from
/// Excel schedule cells can be rendered in <c>&lt;img&gt;</c> tags (<c>img-src 'self' data:</c>)
/// without inline <c>style</c> attributes (<c>style-src 'self'</c>).
/// </summary>
public static partial class ColorSwatchHelper
{
	public static string? ToSvgDataUri(string? hexRgb, int size = 20)
	{
		if (string.IsNullOrWhiteSpace(hexRgb))
		{
			return null;
		}

		var trimmed = hexRgb.Trim().TrimStart('#');
		if (trimmed.Length == 8)
		{
			// Strip leading ARGB alpha channel if present
			trimmed = trimmed[2..];
		}

		if (!HexColorRegex().IsMatch(trimmed))
		{
			return null;
		}

		var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}'>" +
			$"<rect width='{size}' height='{size}' rx='4' fill='#{trimmed.ToUpperInvariant()}' stroke='#6c757d' stroke-width='1'/>" +
			"</svg>";

		return $"data:image/svg+xml;utf8,{Uri.EscapeDataString(svg)}";
	}

	[GeneratedRegex(@"^[0-9A-Fa-f]{6}$")]
	private static partial Regex HexColorRegex();
}
