namespace Region42.PantherShootout.Web.Middleware;

/// <summary>
/// Refreshes the theme preference cookie's expiration on every request so that,
/// as long as the user visits the site periodically, their light/dark mode
/// choice never expires (a sliding-expiration cookie).
/// </summary>
public class ThemeCookieMiddleware
{
	public const string ThemeCookieName = "region42_theme";
	public static readonly TimeSpan ThemeCookieLifetime = TimeSpan.FromDays(400);

	private readonly RequestDelegate _next;

	public ThemeCookieMiddleware(RequestDelegate next)
	{
		ArgumentNullException.ThrowIfNull(next);

		_next = next;
	}

	public Task InvokeAsync(HttpContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (context.Request.Cookies.TryGetValue(ThemeCookieName, out var themeValue) &&
			!string.IsNullOrEmpty(themeValue))
		{
			context.Response.Cookies.Append(ThemeCookieName, themeValue, new CookieOptions
			{
				Expires = DateTimeOffset.UtcNow.Add(ThemeCookieLifetime),
				HttpOnly = false, // client-side JS toggles the theme without a round trip
				IsEssential = false,
				SameSite = SameSiteMode.Lax,
				Secure = context.Request.IsHttps,
				Path = "/"
			});
		}

		return _next(context);
	}
}
