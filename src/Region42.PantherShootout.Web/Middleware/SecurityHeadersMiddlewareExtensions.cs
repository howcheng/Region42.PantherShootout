namespace Region42.PantherShootout.Web.Middleware;

public static class SecurityHeadersMiddlewareExtensions
{
	public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder.UseMiddleware<SecurityHeadersMiddleware>();
	}
}
