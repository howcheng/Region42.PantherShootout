using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Web.Controllers;
using Region42.PantherShootout.Web.Helpers;
using Region42.PantherShootout.Web.Models;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Controllers;

public class StandingsControllerTests
{
	[Fact]
	public async Task Index_RedirectsToMostRecentYearAndPreferredCookieDivision()
	{
		var year2026 = new TournamentYearDto { Id = 2, Year = 2026, Name = "2026 Panther Shootout", DivisionCount = 2 };

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetDefaultYearAsync(It.IsAny<CancellationToken>())).ReturnsAsync(year2026);
		yearService.Setup(s => s.GetDivisionsForYearAsync(2, It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				new DivisionSummaryDto { Id = 10, DivisionKey = "10u-girls", Name = "10U Girls" },
				new DivisionSummaryDto { Id = 11, DivisionKey = "14u-boys", Name = "14U Boys" },
			]);

		var httpContext = new DefaultHttpContext();
		httpContext.Request.Headers.Cookie = $"{DivisionPreferenceCookieHelper.CookieName}=14u-boys";

		var controller = new StandingsController(
			yearService.Object,
			Mock.Of<IPoolPlayStandingsService>(),
			Mock.Of<IChampionshipBracketService>(),
			Mock.Of<IPsoStandingsService>(),
			Mock.Of<ITournamentDataContext>())
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
		};

		var result = await controller.Index();

		var redirect = Assert.IsType<RedirectToActionResult>(result);
		Assert.Equal(nameof(StandingsController.Division), redirect.ActionName);
		Assert.Equal(2026, redirect.RouteValues?["year"]);
		Assert.Equal("14u-boys", redirect.RouteValues?["divisionKey"]);
	}

	[Theory]
	[InlineData(DivisionSize.Five, BracketTopologyKind.NoneChampionBannerOnly)]
	[InlineData(DivisionSize.Ten, BracketTopologyKind.NoneChampionBannerOnly)]
	[InlineData(DivisionSize.Four, BracketTopologyKind.DirectFinals)]
	[InlineData(DivisionSize.Eight, BracketTopologyKind.DirectFinals)]
	[InlineData(DivisionSize.Twelve, BracketTopologyKind.DirectFinals)]
	[InlineData(DivisionSize.Six, BracketTopologyKind.SemifinalsToFinal)]
	public async Task Division_BuildsExpectedTopologyAndSetsPreferredDivisionCookie(
		DivisionSize size,
		BracketTopologyKind expectedTopology)
	{
		var yearDto = new TournamentYearDto { Id = 1, Year = 2025, Name = "2025 Panther Shootout" };
		var divDto = new DivisionSummaryDto
		{
			Id = 7,
			TournamentYearId = 1,
			Year = 2025,
			DivisionKey = "10u-girls",
			ShortCode = "10UG",
			Name = "10U Girls",
			Size = size,
			PoolCount = 2,
			TeamCount = (int)size,
		};

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([yearDto]);
		yearService.Setup(s => s.GetDivisionsForYearAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([divDto]);

		var poolService = new Mock<IPoolPlayStandingsService>(MockBehavior.Strict);
		poolService.Setup(p => p.CalculateForDivisionAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionPoolPlayStandingsResult
			{
				DivisionId = 7,
				DivisionName = "10U Girls",
				Pools = [new PoolPlayStandingsResult { PoolId = 1, PoolName = "A" }],
				CombinedDivisionStandings = size == DivisionSize.Ten ? new PoolPlayStandingsResult { PoolName = "Combined" } : null,
				PoolWinnersStandings = size == DivisionSize.Twelve ? new PoolPlayStandingsResult { PoolName = "Winners" } : null,
				RunnersUpStandings = size == DivisionSize.Twelve ? new PoolPlayStandingsResult { PoolName = "RunnersUp" } : null,
			});

		var bracketService = new Mock<IChampionshipBracketService>(MockBehavior.Strict);
		bracketService.Setup(b => b.SeedAndProgressAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChampionshipBracketResult
			{
				DivisionId = 7,
				DivisionName = "10U Girls",
				Size = size,
				HasBracket = expectedTopology != BracketTopologyKind.NoneChampionBannerOnly,
			});

		var psoService = new Mock<IPsoStandingsService>(MockBehavior.Strict);
		psoService.Setup(p => p.CalculateForDivisionAsync(7, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PsoStandingsResult { DivisionId = 7, DivisionName = "10U Girls" });

		var dataContext = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		dataContext.Setup(c => c.LoadDivisionDataAsync(7, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		dataContext.SetupGet(c => c.Teams).Returns([]);
		dataContext.SetupGet(c => c.Pools).Returns([new Pool { Id = 1, DivisionId = 7, Name = "A" }]);
		dataContext.SetupGet(c => c.PoolPlayGames).Returns([]);
		dataContext.SetupGet(c => c.ShootoutRounds).Returns([]);
		dataContext.Setup(c => c.GetStandingsForDivision(7)).Returns((DivisionStandingsSnapshotDocument?)null);

		var httpContext = new DefaultHttpContext();
		var controller = new StandingsController(
			yearService.Object,
			poolService.Object,
			bracketService.Object,
			psoService.Object,
			dataContext.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
		};

		var result = await controller.Division(2025, "10u-girls");

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<DivisionStandingsViewModel>(view.Model);
		Assert.Equal(expectedTopology, model.Bracket.Topology);
		Assert.Equal(
			size is DivisionSize.Five or DivisionSize.Six or DivisionSize.Ten,
			model.Pso.HasFourthShootoutRound);

		var setCookie = httpContext.Response.Headers.SetCookie.ToString();
		Assert.Contains($"{DivisionPreferenceCookieHelper.CookieName}=10u-girls", setCookie);

		if (size == DivisionSize.Ten)
		{
			Assert.NotNull(model.CombinedTenTeamTable);
			Assert.Null(model.CombinedTenTeamTable!.PrintPoolUrl);
		}

		if (size == DivisionSize.Twelve)
		{
			Assert.NotNull(model.TwelveTeamWinnersTable);
			Assert.NotNull(model.TwelveTeamRunnersUpTable);
			Assert.Null(model.TwelveTeamWinnersTable!.PrintPoolUrl);
			Assert.Null(model.TwelveTeamRunnersUpTable!.PrintPoolUrl);
			Assert.Empty(model.TwelveTeamWinnersTable!.AdvancingTeamIds);
			Assert.Empty(model.TwelveTeamRunnersUpTable!.AdvancingTeamIds);
		}

		Assert.All(model.PoolTables, t =>
		{
			Assert.Empty(t.AdvancingTeamIds);
			Assert.Equal("/standings/2025/10u-girls/print?pool=A", t.PrintPoolUrl);
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Division_PopulatesAdvancingTeamIdsOnlyWhenAllPoolsFinal_AndIncludesGameScores(bool allPoolsFinal)
	{
		var yearDto = new TournamentYearDto { Id = 1, Year = 2026, Name = "2026 Panther Shootout" };
		var divDto = new DivisionSummaryDto
		{
			Id = 22,
			TournamentYearId = 1,
			Year = 2026,
			DivisionKey = "10u-boys",
			ShortCode = "10UB",
			Name = "10U Boys",
			Size = DivisionSize.Eight,
			PoolCount = 2,
			TeamCount = 8,
		};

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([yearDto]);
		yearService.Setup(s => s.GetDivisionsForYearAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([divDto]);

		var poolC = new Pool { Id = 23, DivisionId = 22, Name = "C" };
		var poolD = new Pool { Id = 28, DivisionId = 22, Name = "D" };
		var teamC1 = new Team { Id = 24, PoolId = 23, Pool = poolC, TeamNumber = 1, RegionName = "Thousand Oaks", CoachName = "Mallord" };
		var teamC2 = new Team { Id = 25, PoolId = 23, Pool = poolC, TeamNumber = 2, RegionName = "Newbury Park", CoachName = "Oakman" };
		var teamC3 = new Team { Id = 26, PoolId = 23, Pool = poolC, TeamNumber = 3, RegionName = "Arcadia", CoachName = "Wymar" };
		var teamD1 = new Team { Id = 29, PoolId = 28, Pool = poolD, TeamNumber = 1, RegionName = "Thousand Oaks", CoachName = "Furniss" };
		var teamD2 = new Team { Id = 30, PoolId = 28, Pool = poolD, TeamNumber = 2, RegionName = "Newbury Park", CoachName = "Cassaro" };
		var teamD3 = new Team { Id = 31, PoolId = 28, Pool = poolD, TeamNumber = 3, RegionName = "Santa Monica", CoachName = "Cruz" };

		var poolService = new Mock<IPoolPlayStandingsService>(MockBehavior.Strict);
		poolService.Setup(p => p.CalculateForDivisionAsync(22, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionPoolPlayStandingsResult
			{
				DivisionId = 22,
				DivisionName = "10U Boys",
				AllPoolsFinal = allPoolsFinal,
				Pools =
				[
					new PoolPlayStandingsResult
					{
						PoolId = 23,
						PoolName = "C",
						IsFinal = allPoolsFinal,
						Standings =
						[
							new PoolPlayStandingDto { Rank = 1, TeamId = 24 },
							new PoolPlayStandingDto { Rank = 2, TeamId = 25 },
							new PoolPlayStandingDto { Rank = 3, TeamId = 26 },
						],
					},
					new PoolPlayStandingsResult
					{
						PoolId = 28,
						PoolName = "D",
						IsFinal = allPoolsFinal,
						Standings =
						[
							new PoolPlayStandingDto { Rank = 1, TeamId = 29 },
							new PoolPlayStandingDto { Rank = 2, TeamId = 30 },
							new PoolPlayStandingDto { Rank = 3, TeamId = 31 },
						],
					},
				],
			});

		var bracketService = new Mock<IChampionshipBracketService>(MockBehavior.Strict);
		bracketService.Setup(b => b.SeedAndProgressAsync(22, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChampionshipBracketResult
			{
				DivisionId = 22,
				DivisionName = "10U Boys",
				Size = DivisionSize.Eight,
				HasBracket = true,
				PoolPlayFinal = allPoolsFinal,
			});

		var psoService = new Mock<IPsoStandingsService>(MockBehavior.Strict);
		psoService.Setup(p => p.CalculateForDivisionAsync(22, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PsoStandingsResult { DivisionId = 22, DivisionName = "10U Boys" });

		var round1Game = new PoolPlayGame
		{
			Id = 33,
			DivisionId = 22,
			PoolId = 23,
			Round = 1,
			HomeTeamId = 24,
			AwayTeamId = 25,
			HomeGoals = 3,
			AwayGoals = 1,
		};
		var round1Pso = new ShootoutRound
		{
			Id = 34,
			DivisionId = 22,
			Round = 1,
			PoolPlayGameId = 33,
			HomeTeamId = 24,
			AwayTeamId = 25,
			HomePkGoals = 4,
			AwayPkGoals = 2,
		};

		var dataContext = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		dataContext.Setup(c => c.LoadDivisionDataAsync(22, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		dataContext.SetupGet(c => c.Teams).Returns([teamC1, teamC2, teamC3, teamD1, teamD2, teamD3]);
		dataContext.SetupGet(c => c.Pools).Returns([poolC, poolD]);
		dataContext.SetupGet(c => c.PoolPlayGames).Returns([round1Game]);
		dataContext.SetupGet(c => c.ShootoutRounds).Returns([round1Pso]);
		dataContext.Setup(c => c.GetStandingsForDivision(22)).Returns((DivisionStandingsSnapshotDocument?)null);

		var controller = new StandingsController(
			yearService.Object,
			poolService.Object,
			bracketService.Object,
			psoService.Object,
			dataContext.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
		};

		var result = await controller.Division(2026, "10u-boys");

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<DivisionStandingsViewModel>(view.Model);

		var gameVm = Assert.Single(model.Schedule.Games);
		Assert.True(gameVm.HasScore);
		Assert.Equal(3, gameVm.HomeGoals);
		Assert.Equal(1, gameVm.AwayGoals);
		Assert.True(gameVm.HasPsoScore);
		Assert.Equal(4, gameVm.HomePkGoals);
		Assert.Equal(2, gameVm.AwayPkGoals);

		if (!allPoolsFinal)
		{
			Assert.All(model.PoolTables, t => Assert.Empty(t.AdvancingTeamIds));
		}
		else
		{
			var expectedAdvancing = new HashSet<int> { 24, 25, 29, 30 };
			Assert.All(model.PoolTables, t => Assert.Equal(expectedAdvancing, t.AdvancingTeamIds));
		}
	}

	[Theory]
	[InlineData(1999, "10u-girls")]
	[InlineData(2026, "")]
	[InlineData(2099, "10u-girls")]
	[InlineData(2026, "unknown-division")]
	public async Task Print_WhenYearOrDivisionInvalidOrMissing_ReturnsNotFound(int year, string divisionKey)
	{
		var yearDto = new TournamentYearDto { Id = 1, Year = 2026, Name = "2026 Panther Shootout" };
		var divDto = new DivisionSummaryDto { Id = 10, TournamentYearId = 1, Year = 2026, DivisionKey = "10u-girls", Name = "10U Girls" };

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		if (year >= 2000 && year <= 2100 && !string.IsNullOrWhiteSpace(divisionKey))
		{
			yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([yearDto]);
			if (year == 2026)
			{
				yearService.Setup(s => s.GetDivisionsForYearAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([divDto]);
			}
		}

		var controller = new StandingsController(
			yearService.Object,
			Mock.Of<IPoolPlayStandingsService>(),
			Mock.Of<IChampionshipBracketService>(),
			Mock.Of<IPsoStandingsService>(),
			Mock.Of<ITournamentDataContext>())
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
		};

		var result = await controller.Print(year, divisionKey);

		Assert.IsType<NotFoundResult>(result);
	}

	[Theory]
	[InlineData(null, "C", false, true, false, true)]
	[InlineData("c", "C", false, false, true, false)]
	public async Task Print_BuildsPoolSheetsWithPsoRunningTotalsGamePkScoresAndPoolFilter(
		string? requestedPool,
		string expectedFirstPoolName,
		bool expectedFirstHidden,
		bool expectedSecondLastVisible,
		bool expectedSecondHidden,
		bool expectedFirstNotLast)
	{
		var yearDto = new TournamentYearDto { Id = 1, Year = 2026, Name = "2026 Panther Shootout" };
		var divDto = new DivisionSummaryDto
		{
			Id = 30,
			TournamentYearId = 1,
			Year = 2026,
			DivisionKey = "10u-girls",
			ShortCode = "10UG",
			Name = "10U Girls",
			Size = DivisionSize.Ten,
			PoolCount = 2,
			TeamCount = 10,
		};

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([yearDto]);
		yearService.Setup(s => s.GetDivisionsForYearAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([divDto]);

		var poolC = new Pool { Id = 31, DivisionId = 30, Name = "C" };
		var poolD = new Pool { Id = 32, DivisionId = 30, Name = "D" };
		var teamC1 = new Team { Id = 41, PoolId = 31, Pool = poolC, TeamNumber = 1, RegionNumber = 9, RegionName = "Thousand Oaks", CoachName = "Mallord" };
		var teamC2 = new Team { Id = 42, PoolId = 31, Pool = poolC, TeamNumber = 2, RegionNumber = 42, RegionName = "Newbury Park", CoachName = "Oakman" };
		var teamD1 = new Team { Id = 51, PoolId = 32, Pool = poolD, TeamNumber = 1, RegionNumber = 121, RegionName = "Simi Valley", CoachName = "Wright" };

		var poolService = new Mock<IPoolPlayStandingsService>(MockBehavior.Strict);
		poolService.Setup(p => p.CalculateForDivisionAsync(30, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionPoolPlayStandingsResult
			{
				DivisionId = 30,
				DivisionName = "10U Girls",
				AllPoolsFinal = false,
				Pools =
				[
					new PoolPlayStandingsResult
					{
						PoolId = 31,
						PoolName = "C",
						IsFinal = false,
						Standings =
						[
							new PoolPlayStandingDto { Rank = 1, TeamId = 41, TeamDesignation = "C1", RegionNumber = 9, CoachName = "Mallord", TeamName = "Thousand Oaks: Mallord", Points = 9, GamesPlayed = 1, Wins = 1 },
							new PoolPlayStandingDto { Rank = 2, TeamId = 42, TeamDesignation = "C2", RegionNumber = 42, CoachName = "Oakman", TeamName = "Newbury Park: Oakman", Points = 0, GamesPlayed = 1, Losses = 1 },
						],
					},
					new PoolPlayStandingsResult
					{
						PoolId = 32,
						PoolName = "D",
						IsFinal = false,
						Standings =
						[
							new PoolPlayStandingDto { Rank = 1, TeamId = 51, TeamDesignation = "D1", TeamName = "Simi Valley: Wright", Points = 6, GamesPlayed = 1, Wins = 1 },
						],
					},
				],
			});

		var bracketService = new Mock<IChampionshipBracketService>(MockBehavior.Strict);
		bracketService.Setup(b => b.SeedAndProgressAsync(30, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChampionshipBracketResult
			{
				DivisionId = 30,
				DivisionName = "10U Girls",
				Size = DivisionSize.Ten,
				HasBracket = false,
			});

		var psoService = new Mock<IPsoStandingsService>(MockBehavior.Strict);
		psoService.Setup(p => p.CalculateForDivisionAsync(30, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PsoStandingsResult
			{
				DivisionId = 30,
				DivisionName = "10U Girls",
				IsFinal = false,
				Standings =
				[
					new PsoStandingDto { Rank = 1, TeamId = 51, TeamDesignation = "D1", TeamName = "Simi Valley: Wright", TotalPkGoalsFor = 5, TotalPkGoalsAgainst = 2, RoundsCompleted = 1 },
					new PsoStandingDto { Rank = 2, TeamId = 41, TeamDesignation = "C1", TeamName = "Thousand Oaks: Mallord", TotalPkGoalsFor = 4, TotalPkGoalsAgainst = 3, RoundsCompleted = 1 },
					new PsoStandingDto { Rank = 3, TeamId = 42, TeamDesignation = "C2", TeamName = "Newbury Park: Oakman", TotalPkGoalsFor = 3, TotalPkGoalsAgainst = 4, RoundsCompleted = 1 },
				],
			});

		var round1Game = new PoolPlayGame
		{
			Id = 60,
			DivisionId = 30,
			PoolId = 31,
			Round = 1,
			Location = "Field 3",
			HomeTeamId = 41,
			AwayTeamId = 42,
			HomeGoals = 2,
			AwayGoals = 0,
		};
		var round5GameNoPso = new PoolPlayGame
		{
			Id = 61,
			DivisionId = 30,
			PoolId = 31,
			Round = 5,
			HomeTeamId = 42,
			AwayTeamId = 41,
		};
		var round1Pso = new ShootoutRound
		{
			Id = 70,
			DivisionId = 30,
			Round = 1,
			PoolPlayGameId = 60,
			HomeTeamId = 41,
			AwayTeamId = 42,
			HomePkGoals = 4,
			AwayPkGoals = 3,
		};

		var dataContext = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		dataContext.Setup(c => c.LoadDivisionDataAsync(30, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		dataContext.SetupGet(c => c.Teams).Returns([teamC1, teamC2, teamD1]);
		dataContext.SetupGet(c => c.Pools).Returns([poolC, poolD]);
		dataContext.SetupGet(c => c.PoolPlayGames).Returns([round1Game, round5GameNoPso]);
		dataContext.SetupGet(c => c.ShootoutRounds).Returns([round1Pso]);
		dataContext.Setup(c => c.GetStandingsForDivision(30)).Returns((DivisionStandingsSnapshotDocument?)null);

		var httpContext = new DefaultHttpContext();
		httpContext.Request.Scheme = "https";
		httpContext.Request.Host = new HostString("shootout.aysoregion42.org", 8443);

		var controller = new StandingsController(
			yearService.Object,
			poolService.Object,
			bracketService.Object,
			psoService.Object,
			dataContext.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
		};

		var result = await controller.Print(2026, "10u-girls", requestedPool);

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<DivisionPrintViewModel>(view.Model);
		Assert.Equal(2, model.PoolSheets.Count);
		Assert.Equal("D1 Simi Valley: Wright", model.PsoLeaderDisplay);
		Assert.Equal("https://shootout.aysoregion42.org:8443/standings/2026/10u-girls", model.PublicDivisionUrl);
		Assert.Equal("shootout.aysoregion42.org:8443/standings/2026/10u-girls", model.PublicDivisionDisplayUrl);
		Assert.StartsWith("data:image/png;base64,", model.QrCodeDataUri);
		var pngBytes = Convert.FromBase64String(model.QrCodeDataUri["data:image/png;base64,".Length..]);
		Assert.Equal(0x89, pngBytes[0]);
		Assert.Equal((byte)'P', pngBytes[1]);
		Assert.Equal((byte)'N', pngBytes[2]);
		Assert.Equal((byte)'G', pngBytes[3]);

		var sheetC = model.PoolSheets[0];
		var sheetD = model.PoolSheets[1];
		Assert.Equal(expectedFirstPoolName, sheetC.PoolName);
		Assert.Equal("10U Girls — Pool C", sheetC.HeaderTitle);
		Assert.Equal(1, sheetC.CompletedRounds);
		Assert.Equal(5, sheetC.TotalRounds);
		Assert.Equal(expectedFirstHidden, sheetC.IsHiddenByInitialFilter);
		Assert.Equal(!expectedFirstNotLast, sheetC.IsLastVisibleSheet);
		Assert.Equal(expectedSecondHidden, sheetD.IsHiddenByInitialFilter);
		Assert.Equal(expectedSecondLastVisible, sheetD.IsLastVisibleSheet);

		var c1Row = sheetC.StandingsRows[0];
		Assert.Equal("Mallord (9)", c1Row.PrintCoachAndRegion);
		Assert.Equal(4, c1Row.PsoGoalsFor);
		Assert.Equal(3, c1Row.PsoGoalsAgainst);
		Assert.False(c1Row.IsPsoDivisionLeader);
		Assert.True(sheetD.StandingsRows[0].IsPsoDivisionLeader);

		Assert.Equal(2, sheetC.Games.Count);
		Assert.Equal("3", sheetC.Games[0].FieldShortLabel);
		Assert.Equal("C1: Mallord (9)", sheetC.Games[0].HomePrintName);
		Assert.Equal("C2: Oakman (42)", sheetC.Games[0].AwayPrintName);
		Assert.True(sheetC.Games[0].HasPairedShootoutRound);
		Assert.True(sheetC.Games[0].HasPsoScore);
		Assert.Equal(4, sheetC.Games[0].HomePkGoals);
		Assert.Equal(3, sheetC.Games[0].AwayPkGoals);
		Assert.False(sheetC.Games[1].HasPairedShootoutRound);
		Assert.True(model.HasFourthShootoutRound);
	}

	[Fact]
	public async Task Print_And_Division_FlagRound4TeamWhoseScoreDoesNotCountAfterThreeTurns()
	{
		var yearDto = new TournamentYearDto { Id = 1, Year = 2026, Name = "2026 Panther Shootout" };
		var divDto = new DivisionSummaryDto
		{
			Id = 40,
			TournamentYearId = 1,
			Year = 2026,
			DivisionKey = "12u-girls",
			ShortCode = "12UG",
			Name = "12U Girls",
			Size = DivisionSize.Five,
			PoolCount = 1,
			TeamCount = 5,
		};

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([yearDto]);
		yearService.Setup(s => s.GetDivisionsForYearAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([divDto]);

		var poolA = new Pool { Id = 41, DivisionId = 40, Name = "A" };
		var t1 = new Team { Id = 101, PoolId = 41, Pool = poolA, TeamNumber = 1, RegionName = "Newbury Park", CoachName = "Alpha" };
		var t2 = new Team { Id = 102, PoolId = 41, Pool = poolA, TeamNumber = 2, RegionName = "Thousand Oaks", CoachName = "Bravo" };
		var t3 = new Team { Id = 103, PoolId = 41, Pool = poolA, TeamNumber = 3, RegionName = "Simi Valley", CoachName = "Charlie" };
		var t4 = new Team { Id = 104, PoolId = 41, Pool = poolA, TeamNumber = 4, RegionName = "Agoura", CoachName = "Delta" };
		var t5 = new Team { Id = 105, PoolId = 41, Pool = poolA, TeamNumber = 5, RegionName = "Arcadia", CoachName = "Echo" };

		var poolService = new Mock<IPoolPlayStandingsService>(MockBehavior.Strict);
		poolService.Setup(p => p.CalculateForDivisionAsync(40, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionPoolPlayStandingsResult
			{
				DivisionId = 40,
				DivisionName = "12U Girls",
				Pools = [new PoolPlayStandingsResult { PoolId = 41, PoolName = "A" }],
			});

		var bracketService = new Mock<IChampionshipBracketService>(MockBehavior.Strict);
		bracketService.Setup(b => b.SeedAndProgressAsync(40, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChampionshipBracketResult { DivisionId = 40, DivisionName = "12U Girls", Size = DivisionSize.Five });

		var psoService = new Mock<IPsoStandingsService>(MockBehavior.Strict);
		psoService.Setup(p => p.CalculateForDivisionAsync(40, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PsoStandingsResult
			{
				DivisionId = 40,
				DivisionName = "12U Girls",
				Standings =
				[
					new PsoStandingDto { Rank = 1, TeamId = 101, TeamDesignation = "A1", RoundScores = [2, 2, 1, null], TotalPkGoalsFor = 5, RoundsCompleted = 3 },
					new PsoStandingDto { Rank = 2, TeamId = 105, TeamDesignation = "A5", RoundScores = [null, 1, 1, 3], TotalPkGoalsFor = 5, RoundsCompleted = 3 },
				],
			});

		// Team A1 (101) plays in R1, R2, R3 (3 turns!) AND plays A5 (105) in R4 -> A1's R4 score (4) does not count!
		var games = new List<PoolPlayGame>
		{
			new() { Id = 201, DivisionId = 40, PoolId = 41, Round = 1, HomeTeamId = 101, AwayTeamId = 102, HomeGoals = 1, AwayGoals = 0 },
			new() { Id = 202, DivisionId = 40, PoolId = 41, Round = 2, HomeTeamId = 101, AwayTeamId = 103, HomeGoals = 1, AwayGoals = 0 },
			new() { Id = 203, DivisionId = 40, PoolId = 41, Round = 3, HomeTeamId = 101, AwayTeamId = 104, HomeGoals = 1, AwayGoals = 0 },
			new() { Id = 204, DivisionId = 40, PoolId = 41, Round = 4, HomeTeamId = 101, AwayTeamId = 105, HomeGoals = 2, AwayGoals = 2 },
		};
		var shootouts = new List<ShootoutRound>
		{
			new() { Id = 301, DivisionId = 40, Round = 1, PoolPlayGameId = 201, HomeTeamId = 101, AwayTeamId = 102, HomePkGoals = 2, AwayPkGoals = 1 },
			new() { Id = 302, DivisionId = 40, Round = 2, PoolPlayGameId = 202, HomeTeamId = 101, AwayTeamId = 103, HomePkGoals = 2, AwayPkGoals = 0 },
			new() { Id = 303, DivisionId = 40, Round = 3, PoolPlayGameId = 203, HomeTeamId = 101, AwayTeamId = 104, HomePkGoals = 1, AwayPkGoals = 1 },
			new() { Id = 304, DivisionId = 40, Round = 4, PoolPlayGameId = 204, HomeTeamId = 101, AwayTeamId = 105, HomePkGoals = 4, AwayPkGoals = 3 },
		};

		var dataContext = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		dataContext.Setup(c => c.LoadDivisionDataAsync(40, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		dataContext.SetupGet(c => c.Teams).Returns([t1, t2, t3, t4, t5]);
		dataContext.SetupGet(c => c.Pools).Returns([poolA]);
		dataContext.SetupGet(c => c.PoolPlayGames).Returns(games);
		dataContext.SetupGet(c => c.ShootoutRounds).Returns(shootouts);
		dataContext.Setup(c => c.GetStandingsForDivision(40)).Returns((DivisionStandingsSnapshotDocument?)null);

		var controller = new StandingsController(
			yearService.Object,
			poolService.Object,
			bracketService.Object,
			psoService.Object,
			dataContext.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
		};

		var divView = Assert.IsType<ViewResult>(await controller.Division(2026, "12u-girls"));
		var divModel = Assert.IsType<DivisionStandingsViewModel>(divView.Model);
		Assert.True(divModel.Pso.HasFourthShootoutRound);
		Assert.Contains(101, divModel.Pso.CappedFourthRoundTeamIds);
		Assert.Equal(4, divModel.Pso.ExcludedFourthRoundScoresByTeamId[101]);

		var r4ScheduleCard = divModel.Schedule.Games.Single(g => g.Round == 4);
		Assert.True(r4ScheduleCard.HomePsoDoesNotCount);
		Assert.False(r4ScheduleCard.AwayPsoDoesNotCount);
		Assert.Equal("A1", r4ScheduleCard.NonCountingPsoTeamCode);

		var printView = Assert.IsType<ViewResult>(await controller.Print(2026, "12u-girls"));
		var printModel = Assert.IsType<DivisionPrintViewModel>(printView.Model);
		Assert.True(printModel.HasFourthShootoutRound);
		var r4PrintGame = Assert.Single(printModel.PoolSheets).Games.Single(g => g.Round == 4);
		Assert.True(r4PrintGame.HomePsoDoesNotCount);
		Assert.Equal("A1", r4PrintGame.NonCountingPsoTeamCode);
	}
}
