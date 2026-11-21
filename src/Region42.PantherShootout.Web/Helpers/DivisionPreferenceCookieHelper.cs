namespace Region42.PantherShootout.Web.Helpers;

/// <summary>
/// Reads and writes the visitor's preferred division slug (<c>10u-girls</c>..<c>14u-boys</c>)
/// so mobile users return immediately to their team's division across Home, Standings, and PSO views.
/// </summary>
public static class DivisionPreferenceCookieHelper
{
	public const string CookieName = "panther_preferred_division";

	public static string? GetPreferredDivisionKey(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.Request.Cookies.TryGetValue(CookieName, out var rawValue) &&
			!string.IsNullOrWhiteSpace(rawValue))
		{
			return rawValue.Trim().ToLowerInvariant();
		}

		return null;
	}

	public static void SetPreferredDivisionKey(HttpContext context, string divisionKey, DateTimeOffset? now = null)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentException.ThrowIfNullOrWhiteSpace(divisionKey);

		var current = now ?? DateTimeOffset.UtcNow;
		var endOfYear = new DateTimeOffset(current.Year, 12, 31, 23, 59, 59, TimeSpan.Zero);
		if (endOfYear <= current)
		{
			endOfYear = endOfYear.AddYears(1);
		}

		context.Response.Cookies.Append(CookieName, divisionKey.Trim().ToLowerInvariant(), new CookieOptions
		{
			Expires = endOfYear,
			HttpOnly = true,
			IsEssential = false,
			SameSite = SameSiteMode.Lax,
			Secure = context.Request.IsHttps,
			Path = "/",
		});
	}
}
