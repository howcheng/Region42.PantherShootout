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

		var setCookie = httpContext.Response.Headers.SetCookie.ToString();
		Assert.Contains($"{DivisionPreferenceCookieHelper.CookieName}=10u-girls", setCookie);

		if (size == DivisionSize.Ten)
		{
			Assert.NotNull(model.CombinedTenTeamTable);
		}

		if (size == DivisionSize.Twelve)
		{
			Assert.NotNull(model.TwelveTeamWinnersTable);
			Assert.NotNull(model.TwelveTeamRunnersUpTable);
			Assert.Empty(model.TwelveTeamWinnersTable!.AdvancingTeamIds);
			Assert.Empty(model.TwelveTeamRunnersUpTable!.AdvancingTeamIds);
		}

		Assert.All(model.PoolTables, t => Assert.Empty(t.AdvancingTeamIds));
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
}
