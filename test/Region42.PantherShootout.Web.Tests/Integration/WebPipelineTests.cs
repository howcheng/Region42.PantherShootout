using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
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

	[Fact]
	public async Task GetStandingsPrint_Returns200WithCspNonceZeroInlineStylesAndPoolSheets()
	{
		using (var scope = _factory.Services.CreateScope())
		{
			var dataContext = scope.ServiceProvider.GetRequiredService<ITournamentDataContext>();
			await dataContext.LoadAllAsync();

			var year = new TournamentYear
			{
				Year = 2026,
				Name = "2026 Panther Shootout",
				IsActive = true,
			};
			dataContext.Add(year);

			var division = new Division
			{
				TournamentYearId = year.Id,
				AgeGroup = AgeGroup.U12,
				Gender = Gender.Boys,
				Name = "12U Boys",
				Size = DivisionSize.Twelve,
			};
			dataContext.Add(division);

			var poolA = new Pool { DivisionId = division.Id, Name = "A" };
			var poolB = new Pool { DivisionId = division.Id, Name = "B" };
			var poolC = new Pool { DivisionId = division.Id, Name = "C" };
			dataContext.Add(poolA);
			dataContext.Add(poolB);
			dataContext.Add(poolC);

			var teamA1 = new Team { PoolId = poolA.Id, TeamNumber = 1, RegionNumber = 42, RegionName = "Newbury Park", CoachName = "Oakman" };
			var teamA2 = new Team { PoolId = poolA.Id, TeamNumber = 2, RegionNumber = 9, RegionName = "Thousand Oaks", CoachName = "Mallord" };
			var teamB1 = new Team { PoolId = poolB.Id, TeamNumber = 1, RegionNumber = 121, RegionName = "Simi Valley", CoachName = "Wright" };
			var teamC1 = new Team { PoolId = poolC.Id, TeamNumber = 1, RegionNumber = 20, RegionName = "Santa Monica", CoachName = "Cruz" };
			dataContext.Add(teamA1);
			dataContext.Add(teamA2);
			dataContext.Add(teamB1);
			dataContext.Add(teamC1);

			var gameA = new PoolPlayGame
			{
				DivisionId = division.Id,
				PoolId = poolA.Id,
				Round = 1,
				Location = "Field 2",
				HomeTeamId = teamA1.Id,
				AwayTeamId = teamA2.Id,
				HomeGoals = 3,
				AwayGoals = 1,
			};
			dataContext.Add(gameA);

			var psoA = new ShootoutRound
			{
				DivisionId = division.Id,
				Round = 1,
				PoolPlayGameId = gameA.Id,
				HomeTeamId = teamA1.Id,
				AwayTeamId = teamA2.Id,
				HomePkGoals = 5,
				AwayPkGoals = 4,
			};
			dataContext.Add(psoA);

			await dataContext.SaveChangesAsync();
		}

		using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			BaseAddress = new Uri("https://localhost:7443")
		});

		using var response = await client.GetAsync("/standings/2026/12u-boys/print?pool=A");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
		var expectedNonce = CspHeaderNonceRegex().Match(csp).Groups[1].Value;
		Assert.NotEmpty(expectedNonce);

		var rawHtml = await response.Content.ReadAsStringAsync();
		Assert.DoesNotContain("style=\"", rawHtml, StringComparison.OrdinalIgnoreCase);

		var decodedHtml = System.Net.WebUtility.HtmlDecode(rawHtml);
		Assert.Contains("12U Boys — Pool A", decodedHtml);
		Assert.Contains("12U Boys — Pool B", decodedHtml);
		Assert.Contains("12U Boys — Pool C", decodedHtml);
		Assert.Contains("A1: Oakman (42)", decodedHtml);
		Assert.Contains("A2: Mallord (9)", decodedHtml);
		Assert.Contains("src=\"data:image/png;base64,", decodedHtml);
		Assert.Contains("alt=\"QR code for https://localhost:7443/standings/2026/12u-boys\"", decodedHtml);
		Assert.Contains("localhost:7443/standings/2026/12u-boys", decodedHtml);

		var scriptTags = ScriptTagRegex().Matches(decodedHtml);
		Assert.NotEmpty(scriptTags);
		foreach (Match scriptTag in scriptTags)
		{
			Assert.Contains($"nonce=\"{expectedNonce}\"", scriptTag.Value);
		}
	}

	[Fact]
	public async Task FiveTeamDivision_RendersFourthRoundNonCountingNoticeOnPrintAndScreenViews()
	{
		using (var scope = _factory.Services.CreateScope())
		{
			var dataContext = scope.ServiceProvider.GetRequiredService<ITournamentDataContext>();
			await dataContext.LoadAllAsync();

			var year = new TournamentYear { Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
			dataContext.Add(year);

			var division = new Division
			{
				TournamentYearId = year.Id,
				AgeGroup = AgeGroup.U12,
				Gender = Gender.Girls,
				Name = "12U Girls",
				Size = DivisionSize.Five,
			};
			dataContext.Add(division);

			var poolG = new Pool { DivisionId = division.Id, Name = "G" };
			dataContext.Add(poolG);

			var g1 = new Team
			{
				PoolId = poolG.Id,
				TeamNumber = 1,
				RegionNumber = 42,
				RegionName = "Newbury Park",
				CoachName = "Alpha",
				HasVolunteerPointDeduction = true,
			};
			var g2 = new Team { PoolId = poolG.Id, TeamNumber = 2, RegionNumber = 9, RegionName = "Thousand Oaks", CoachName = "Bravo" };
			var g3 = new Team { PoolId = poolG.Id, TeamNumber = 3, RegionNumber = 121, RegionName = "Simi Valley", CoachName = "Charlie" };
			var g4 = new Team { PoolId = poolG.Id, TeamNumber = 4, RegionNumber = 4, RegionName = "Agoura", CoachName = "Delta" };
			var g5 = new Team { PoolId = poolG.Id, TeamNumber = 5, RegionNumber = 2, RegionName = "Arcadia", CoachName = "Echo" };
			dataContext.Add(g1);
			dataContext.Add(g2);
			dataContext.Add(g3);
			dataContext.Add(g4);
			dataContext.Add(g5);

			// G1 plays R1, R2, R3 (3 turns) + R4 vs G5 -> G1's R4 shootout does not count
			var pg1 = new PoolPlayGame { DivisionId = division.Id, PoolId = poolG.Id, Round = 1, HomeTeamId = g1.Id, AwayTeamId = g2.Id, HomeGoals = 2, AwayGoals = 0 };
			var pg2 = new PoolPlayGame { DivisionId = division.Id, PoolId = poolG.Id, Round = 2, HomeTeamId = g1.Id, AwayTeamId = g3.Id, HomeGoals = 1, AwayGoals = 1 };
			var pg3 = new PoolPlayGame { DivisionId = division.Id, PoolId = poolG.Id, Round = 3, HomeTeamId = g1.Id, AwayTeamId = g4.Id, HomeGoals = 3, AwayGoals = 1 };
			var pg4 = new PoolPlayGame { DivisionId = division.Id, PoolId = poolG.Id, Round = 4, HomeTeamId = g1.Id, AwayTeamId = g5.Id, HomeGoals = 1, AwayGoals = 2 };
			dataContext.Add(pg1);
			dataContext.Add(pg2);
			dataContext.Add(pg3);
			dataContext.Add(pg4);

			dataContext.Add(new ShootoutRound { DivisionId = division.Id, Round = 1, PoolPlayGameId = pg1.Id, HomeTeamId = g1.Id, AwayTeamId = g2.Id, HomePkGoals = 3, AwayPkGoals = 2 });
			dataContext.Add(new ShootoutRound { DivisionId = division.Id, Round = 2, PoolPlayGameId = pg2.Id, HomeTeamId = g1.Id, AwayTeamId = g3.Id, HomePkGoals = 2, AwayPkGoals = 1 });
			dataContext.Add(new ShootoutRound { DivisionId = division.Id, Round = 3, PoolPlayGameId = pg3.Id, HomeTeamId = g1.Id, AwayTeamId = g4.Id, HomePkGoals = 4, AwayPkGoals = 3 });
			dataContext.Add(new ShootoutRound { DivisionId = division.Id, Round = 4, PoolPlayGameId = pg4.Id, HomeTeamId = g1.Id, AwayTeamId = g5.Id, HomePkGoals = 5, AwayPkGoals = 2 });

			await dataContext.SaveChangesAsync();
		}

		using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false,
			BaseAddress = new Uri("https://localhost")
		});

		using var printResp = await client.GetAsync("/standings/2026/12u-girls/print");
		Assert.Equal(HttpStatusCode.OK, printResp.StatusCode);
		var printHtml = System.Net.WebUtility.HtmlDecode(await printResp.Content.ReadAsStringAsync());
		Assert.DoesNotContain("style=\"", printHtml, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("G1 no count*", printHtml);
		Assert.Contains("4th PSO Round:", printHtml);
		Assert.Contains("-1 VOL", printHtml);
		Assert.Contains("1 pt Region 42 volunteer deduction", printHtml);

		using var screenResp = await client.GetAsync("/standings/2026/12u-girls");
		Assert.Equal(HttpStatusCode.OK, screenResp.StatusCode);
		var screenHtml = System.Net.WebUtility.HtmlDecode(await screenResp.Content.ReadAsStringAsync());
		Assert.DoesNotContain("style=\"", screenHtml, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("G1 score does not count (already had 3 turns)", screenHtml);
		Assert.Contains("4th Shootout Round:", screenHtml);
		Assert.Contains("-1 VOL", screenHtml);
		Assert.Contains("1 point deducted (Region 42 volunteer requirement)", screenHtml);

		using var psoResp = await client.GetAsync("/shootout/2026?division=12u-girls");
		Assert.Equal(HttpStatusCode.OK, psoResp.StatusCode);
		var psoHtml = System.Net.WebUtility.HtmlDecode(await psoResp.Content.ReadAsStringAsync());
		Assert.DoesNotContain("style=\"", psoHtml, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("G1 score does not count (already had 3 turns)", psoHtml);
	}

	[Theory]
	[InlineData("/Scores")]
	[InlineData("/Import/Upload")]
	[InlineData("/Admin/Years")]
	[InlineData("/Admin/Teams")]
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
