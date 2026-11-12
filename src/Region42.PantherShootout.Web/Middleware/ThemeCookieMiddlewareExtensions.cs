namespace Region42.PantherShootout.Web.Middleware;

public static class ThemeCookieMiddlewareExtensions
{
	public static IApplicationBuilder UseThemeCookie(this IApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder.UseMiddleware<ThemeCookieMiddleware>();
	}
}
