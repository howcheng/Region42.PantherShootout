using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Region42.PantherShootout.Web.Middleware;
using Region42.PantherShootout.Web.Services;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Middleware;

public class SecurityHeadersMiddlewareTests
{
	[Fact]
	public async Task InvokeAsync_Production_SetsAllOwaspHeadersAndStrictDynamicCsp()
	{
		var env = CreateEnvironment(Environments.Production);
		var nextCalled = false;
		var middleware = new SecurityHeadersMiddleware(
			_ =>
			{
				nextCalled = true;
				return Task.CompletedTask;
			},
			env,
			NullLogger<SecurityHeadersMiddleware>.Instance);

		var context = new DefaultHttpContext();
		var nonceService = new CspNonceService(CreateAccessor(context));

		await middleware.InvokeAsync(context, nonceService);

		Assert.True(nextCalled);

		var nonce = nonceService.GetNonce();
		Assert.False(string.IsNullOrWhiteSpace(nonce));
		Assert.Equal(CspNonceService.NonceByteLength, Convert.FromBase64String(nonce).Length);

		var csp = context.Response.Headers["Content-Security-Policy"].ToString();
		Assert.Contains("default-src 'self'", csp);
		Assert.Contains($"script-src 'nonce-{nonce}' 'strict-dynamic'", csp);
		Assert.DoesNotContain("script-src 'self'", csp);
		Assert.Contains("object-src 'none'", csp);
		Assert.Contains("style-src 'self'", csp);
		Assert.Contains("img-src 'self' data:", csp);
		Assert.Contains("font-src 'self'", csp);
		Assert.Contains("connect-src 'self'", csp);
		Assert.DoesNotContain("ws://localhost:*", csp);
		Assert.Contains("frame-ancestors 'none'", csp);
		Assert.Contains("base-uri 'self'", csp);
		Assert.Contains("form-action 'self'", csp);

		Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
		Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
		Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"].ToString());
		Assert.Equal("camera=(), microphone=(), geolocation=()", context.Response.Headers["Permissions-Policy"].ToString());
		Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"].ToString());
		Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Resource-Policy"].ToString());
	}

	[Fact]
	public async Task InvokeAsync_Development_AllowsLocalhostConnectSrcAndUnloadPermission()
	{
		var env = CreateEnvironment(Environments.Development);
		var middleware = new SecurityHeadersMiddleware(
			_ => Task.CompletedTask,
			env,
			NullLogger<SecurityHeadersMiddleware>.Instance);

		var context = new DefaultHttpContext();
		var nonceService = new CspNonceService(CreateAccessor(context));

		await middleware.InvokeAsync(context, nonceService);

		var csp = context.Response.Headers["Content-Security-Policy"].ToString();
		Assert.Contains("connect-src 'self' ws://localhost:* wss://localhost:* http://localhost:* https://localhost:*", csp);
		Assert.Contains($"script-src 'nonce-{nonceService.GetNonce()}' 'strict-dynamic'", csp);
		Assert.Equal("camera=(), microphone=(), geolocation=(), unload=*", context.Response.Headers["Permissions-Policy"].ToString());
	}

	[Fact]
	public void CspNonceService_ReturnsStableNonceWithinRequestAndDistinctNonceAcrossRequests()
	{
		var context1 = new DefaultHttpContext();
		var service1A = new CspNonceService(CreateAccessor(context1));
		var service1B = new CspNonceService(CreateAccessor(context1));

		var context2 = new DefaultHttpContext();
		var service2 = new CspNonceService(CreateAccessor(context2));

		var nonce1 = service1A.GetNonce();
		Assert.Equal(nonce1, service1A.GetNonce());
		Assert.Equal(nonce1, service1B.GetNonce());

		var nonce2 = service2.GetNonce();
		Assert.NotEqual(nonce1, nonce2);
	}

	[Fact]
	public async Task ThemeCookieMiddleware_ExistingThemeCookie_RefreshesSlidingCookie()
	{
		var middleware = new ThemeCookieMiddleware(_ => Task.CompletedTask);
		var context = new DefaultHttpContext();
		context.Request.Scheme = "https";
		context.Request.Headers.Cookie = $"{ThemeCookieMiddleware.ThemeCookieName}=dark";

		await middleware.InvokeAsync(context);

		var setCookie = context.Response.Headers.SetCookie.ToString();
		Assert.Contains($"{ThemeCookieMiddleware.ThemeCookieName}=dark", setCookie);
		Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
	}

	private static IWebHostEnvironment CreateEnvironment(string environmentName)
	{
		var mock = new Mock<IWebHostEnvironment>();
		mock.SetupGet(e => e.EnvironmentName).Returns(environmentName);
		return mock.Object;
	}

	private static IHttpContextAccessor CreateAccessor(HttpContext context)
	{
		var mock = new Mock<IHttpContextAccessor>();
		mock.SetupGet(a => a.HttpContext).Returns(context);
		return mock.Object;
	}
}
