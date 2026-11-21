using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Integration;

public sealed partial class WebPipelineTests : IDisposable
{
	private readonly string _tempStorageDir;
	private readonly WebApplicationFactory<Program> _factory;

	public WebPipelineTests()
	{
		_tempStorageDir = Path.Combine(Path.GetTempPath(), $"panther-shootout-web-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempStorageDir);

		_factory = new WebApplicationFactory<Program>()
			.WithWebHostBuilder(builder =>
			{
				builder.UseEnvironment("Production");
				builder.ConfigureAppConfiguration((_, config) =>
				{
					config.AddInMemoryCollection(new Dictionary<string, string?>
					{
						["Storage:Provider"] = "LocalFile",
						["Storage:CompetitionSlug"] = "panther-shootout",
						["Storage:LocalRoot"] = _tempStorageDir,
						["Authentication:Google:ClientId"] = "test-google-client-id",
						["Authentication:Google:ClientSecret"] = "test-google-client-secret"
					});
				});
			});
	}

	[Fact]
	public async Task GetHomeIndex_Returns200WithMatchingCspNonceOnEveryScriptTag()
	{
		using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			BaseAddress = new Uri("https://localhost")
		});

		using var response = await client.GetAsync("/");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var cspValues));
		var csp = Assert.Single(cspValues);

		var headerNonceMatch = CspHeaderNonceRegex().Match(csp);
		Assert.True(headerNonceMatch.Success, $"Expected nonce in CSP header, got: {csp}");
		var expectedNonce = headerNonceMatch.Groups[1].Value;

		var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
		var scriptTags = ScriptTagRegex().Matches(html);
		Assert.True(scriptTags.Count >= 3, $"Expected at least 3 rendered <script> tags in layout, found {scriptTags.Count}.");

		foreach (Match scriptTag in scriptTags)
		{
			Assert.Contains($"nonce=\"{expectedNonce}\"", scriptTag.Value);
		}
	}

	[Fact]
	public async Task ConsecutiveRequests_GenerateDistinctCryptographicCspNonces()
	{
		using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			BaseAddress = new Uri("https://localhost")
		});

		using var response1 = await client.GetAsync("/");
		using var response2 = await client.GetAsync("/");

		var csp1 = Assert.Single(response1.Headers.GetValues("Content-Security-Policy"));
		var csp2 = Assert.Single(response2.Headers.GetValues("Content-Security-Policy"));

		var nonce1 = CspHeaderNonceRegex().Match(csp1).Groups[1].Value;
		var nonce2 = CspHeaderNonceRegex().Match(csp2).Groups[1].Value;

		Assert.NotEmpty(nonce1);
		Assert.NotEmpty(nonce2);
		Assert.NotEqual(nonce1, nonce2);
	}

	[Fact]
	public async Task GetAccountLogin_ChallengesGoogleOAuthEndpoint()
	{
		using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			BaseAddress = new Uri("https://localhost")
		});

		using var response = await client.GetAsync("/Account/Login?returnUrl=%2FPrivacy");

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.NotNull(response.Headers.Location);
		Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", response.Headers.Location.ToString());
	}

	[Fact]
	public async Task GetAccountLogout_WhenAnonymous_ChallengesGoogleOAuthEndpoint()
	{
		using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			BaseAddress = new Uri("https://localhost")
		});

		using var response = await client.GetAsync("/Account/Logout");

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.NotNull(response.Headers.Location);
		Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", response.Headers.Location.ToString());
	}

	[Fact]
	public async Task AdminPolicy_EnforcesAysoRegion42DomainThroughRegisteredAuthorizationService()
	{
		using var scope = _factory.Services.CreateScope();
		var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

		var allowedUser = new ClaimsPrincipal(new ClaimsIdentity(
		[
			new Claim(ClaimTypes.Email, "admin@aysoregion42.org")
		], "Cookies"));

		var externalUser = new ClaimsPrincipal(new ClaimsIdentity(
		[
			new Claim(ClaimTypes.Email, "visitor@gmail.com")
		], "Cookies"));

		var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());

		var allowedResult = await authorizationService.AuthorizeAsync(allowedUser, resource: null, "AdminPolicy");
		var externalResult = await authorizationService.AuthorizeAsync(externalUser, resource: null, "AdminPolicy");
		var anonymousResult = await authorizationService.AuthorizeAsync(anonymousUser, resource: null, "AdminPolicy");

		Assert.True(allowedResult.Succeeded);
		Assert.False(externalResult.Succeeded);
		Assert.False(anonymousResult.Succeeded);
	}

	[Theory]
	[InlineData("/")]
	[InlineData("/shootout")]
	public async Task PublicRoutes_Return200WithMatchingCspNonceAndZeroInlineStyles(string path)
	{
		using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			BaseAddress = new Uri("https://localhost")
		});

		using var response = await client.GetAsync(path);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
		var expectedNonce = CspHeaderNonceRegex().Match(csp).Groups[1].Value;
		Assert.NotEmpty(expectedNonce);
		var rawHtml = await response.Content.ReadAsStringAsync();
		Assert.DoesNotContain("style=\"", rawHtml, StringComparison.OrdinalIgnoreCase);

		var decodedHtml = System.Net.WebUtility.HtmlDecode(rawHtml);
		foreach (Match scriptTag in ScriptTagRegex().Matches(decodedHtml))
		{
			Assert.Contains($"nonce=\"{expectedNonce}\"", scriptTag.Value);
		}
	}

	[Theory]
	[InlineData("/Scores")]
	[InlineData("/Import/Upload")]
	[InlineData("/Admin/Years")]
	[InlineData("/Admin/Divisions")]
	[InlineData("/Admin/Regions")]
	public async Task AdminRoutes_WhenAnonymous_ChallengeGoogleOAuthEndpoint(string path)
	{
		using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			BaseAddress = new Uri("https://localhost")
		});

		using var response = await client.GetAsync(path);

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.NotNull(response.Headers.Location);
		Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", response.Headers.Location.ToString());
	}

	public void Dispose()
	{
		_factory.Dispose();
		try
		{
			if (Directory.Exists(_tempStorageDir))
			{
				Directory.Delete(_tempStorageDir, recursive: true);
			}
		}
		catch (IOException)
		{
			// Best-effort cleanup of temp storage directory.
		}
	}

	[GeneratedRegex(@"script-src 'nonce-([A-Za-z0-9+/=]+)' 'strict-dynamic'")]
	private static partial Regex CspHeaderNonceRegex();

	[GeneratedRegex(@"<script\b[^>]*>", RegexOptions.IgnoreCase)]
	private static partial Regex ScriptTagRegex();
}
