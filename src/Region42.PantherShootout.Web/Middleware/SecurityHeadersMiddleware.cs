using Region42.PantherShootout.Web.Services;

namespace Region42.PantherShootout.Web.Middleware;

/// <summary>
/// Applies OWASP recommended HTTP security headers and a per-request nonce-based
/// <c>'strict-dynamic'</c> Content Security Policy.
/// </summary>
public class SecurityHeadersMiddleware
{
	private readonly RequestDelegate _next;
	private readonly IWebHostEnvironment _environment;
	private readonly ILogger<SecurityHeadersMiddleware> _logger;

	public SecurityHeadersMiddleware(
		RequestDelegate next,
		IWebHostEnvironment environment,
		ILogger<SecurityHeadersMiddleware> logger)
	{
		ArgumentNullException.ThrowIfNull(next);
		ArgumentNullException.ThrowIfNull(environment);
		ArgumentNullException.ThrowIfNull(logger);

		_next = next;
		_environment = environment;
		_logger = logger;
	}

	public Task InvokeAsync(HttpContext context, ICspNonceService cspNonceService)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(cspNonceService);

		var nonce = cspNonceService.GetNonce();

		// 1. Content-Security-Policy
		context.Response.Headers["Content-Security-Policy"] = BuildCspPolicy(nonce);

		// 2. X-Frame-Options (Clickjacking protection)
		context.Response.Headers["X-Frame-Options"] = "DENY";

		// 3. X-Content-Type-Options (MIME-sniffing protection)
		context.Response.Headers["X-Content-Type-Options"] = "nosniff";

		// 4. Referrer-Policy
		context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

		// 5. Permissions-Policy
		// Disable intrusive features (camera, microphone, geolocation) by default.
		// In Development, additionally allow Browser Link to use the unload event for page-reload detection.
		context.Response.Headers["Permissions-Policy"] = _environment.IsDevelopment()
			? "camera=(), microphone=(), geolocation=(), unload=*"
			: "camera=(), microphone=(), geolocation=()";

		// 6. Cross-Origin-Opener-Policy (Spectre protection)
		context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";

		// 7. Cross-Origin-Resource-Policy (Spectre / cross-origin read protection)
		context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";

		_logger.LogDebug("OWASP Security Headers and strict-dynamic CSP applied successfully.");

		return _next(context);
	}

	private string BuildCspPolicy(string nonce)
	{
		var connectSrc = _environment.IsDevelopment()
			? "connect-src 'self' ws://localhost:* wss://localhost:* http://localhost:* https://localhost:*"
			: "connect-src 'self'";

		if (_environment.IsDevelopment())
		{
			_logger.LogDebug("CSP configured for Development environment with localhost WebSocket/HTTP connect-src support.");
		}

		var directives = new[]
		{
			"default-src 'self'",
			$"script-src 'nonce-{nonce}' 'strict-dynamic'",
			"object-src 'none'",
			"style-src 'self'",
			"img-src 'self' data:",
			"font-src 'self'",
			connectSrc,
			"frame-ancestors 'none'",
			"base-uri 'self'",
			"form-action 'self'"
		};

		return string.Join("; ", directives);
	}
}
