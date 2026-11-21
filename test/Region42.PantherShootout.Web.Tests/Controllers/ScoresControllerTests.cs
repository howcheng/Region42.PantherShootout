using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Web.Controllers;
using Region42.PantherShootout.Web.Models;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Controllers;

public class ScoresControllerTests
{
	[Fact]
	public async Task EntryGet_BuildsPairedPoolAndPsoCardsAndActiveKftmPrompts()
	{
		var yearDto = new TournamentYearDto { Id = 1, Year = 2026, Name = "2026 Panther Shootout" };
		var divDto = new DivisionSummaryDto
		{
			Id = 8,
			TournamentYearId = 1,
			Year = 2026,
			DivisionKey = "10u-boys",
			ShortCode = "10UB",
			Name = "10U Boys",
			Size = DivisionSize.Four,
		};

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([yearDto]);
		yearService.Setup(s => s.GetDefaultYearAsync(It.IsAny<CancellationToken>())).ReturnsAsync(yearDto);
		yearService.Setup(s => s.GetDivisionsForYearAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([divDto]);

		var poolService = new Mock<IPoolPlayStandingsService>(MockBehavior.Strict);
		poolService.Setup(p => p.CalculateForDivisionAsync(8, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionPoolPlayStandingsResult
			{
				DivisionId = 8,
				Pools =
				[
					new PoolPlayStandingsResult
					{
						PoolId = 1,
						PoolName = "C",
						RequiresKftmTiebreaker = true,
						TiedTeamIdsForKftm = [1, 2],
					},
				],
			});

		var bracketService = new Mock<IChampionshipBracketService>(MockBehavior.Strict);
		bracketService.Setup(b => b.SeedAndProgressAsync(8, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChampionshipBracketResult
			{
				DivisionId = 8,
				Size = DivisionSize.Four,
				HasBracket = true,
				Games =
				[
					new ChampionshipGameDto { GameId = 77, Slot = BracketSlot.Championship, HomeSourceDescription = "1st", AwaySourceDescription = "2nd" },
				],
			});

		var psoService = new Mock<IPsoStandingsService>(MockBehavior.Strict);
		psoService.Setup(p => p.CalculateForDivisionAsync(8, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PsoStandingsResult { DivisionId = 8 });

		var pool = new Pool { Id = 1, DivisionId = 8, Name = "C" };
		var team1 = new Team { Id = 1, PoolId = 1, Pool = pool, TeamNumber = 1, RegionName = "Newbury Park", CoachName = "Alpha" };
		var team2 = new Team { Id = 2, PoolId = 1, Pool = pool, TeamNumber = 2, RegionName = "Simi Valley", CoachName = "Beta" };
		var poolGame = new PoolPlayGame { Id = 55, DivisionId = 8, PoolId = 1, Round = 1, HomeTeamId = 1, AwayTeamId = 2 };
		var psoRound = new ShootoutRound { Id = 66, DivisionId = 8, Round = 1, PoolPlayGameId = 55, HomeTeamId = 1, AwayTeamId = 2 };

		var dataContext = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		dataContext.Setup(c => c.LoadDivisionDataAsync(8, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		dataContext.SetupGet(c => c.Teams).Returns([team1, team2]);
		dataContext.SetupGet(c => c.Pools).Returns([pool]);
		dataContext.SetupGet(c => c.PoolPlayGames).Returns([poolGame]);
		dataContext.SetupGet(c => c.ChampionshipGames).Returns([]);
		dataContext.SetupGet(c => c.ShootoutRounds).Returns([psoRound]);
		dataContext.SetupGet(c => c.TiebreakerOverrides).Returns([]);

		var controller = CreateController(
			yearService.Object,
			poolService.Object,
			bracketService.Object,
			psoService.Object,
			Mock.Of<IScoreEntryService>(),
			dataContext.Object);

		var result = await controller.Entry();

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<ScoresEntryPageViewModel>(view.Model);
		Assert.Single(model.PoolPlayCards);
		Assert.Equal(66, model.PoolPlayCards[0].PairedShootoutRoundId);
		Assert.Single(model.ChampionshipCards);
		Assert.Equal(1, model.ActiveKftmTieCount);
	}

	[Fact]
	public async Task SaveBatchPost_DelegatesToScoreEntryServiceAndRedirectsBackToFilter()
	{
		var scoreService = new Mock<IScoreEntryService>(MockBehavior.Strict);
		scoreService.Setup(s => s.SaveDivisionScoresBatchAsync(
				It.Is<DivisionScoreBatchUpdateDto>(b => b.DivisionId == 8 && b.PoolPlayScores.Count == 1 && b.PsoScores.Count == 1),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionStandingsSnapshotDocument());

		var controller = CreateController(
			Mock.Of<ITournamentYearService>(),
			Mock.Of<IPoolPlayStandingsService>(),
			Mock.Of<IChampionshipBracketService>(),
			Mock.Of<IPsoStandingsService>(),
			scoreService.Object,
			Mock.Of<ITournamentDataContext>());

		var result = await controller.SaveBatch(new DivisionScoresFormPostModel
		{
			Year = 2026,
			DivisionKey = "10u-boys",
			DivisionId = 8,
			SelectedRound = 2,
			ActiveTab = "pool",
			PoolPlayScores = [new PoolPlayScoreUpdateDto { GameId = 55, HomeGoals = 2, AwayGoals = 1 }],
			PsoScores = [new PsoScoreUpdateDto { ShootoutRoundId = 66, HomePkGoals = 3, AwayPkGoals = 2 }],
		});

		var redirect = Assert.IsType<RedirectToActionResult>(result);
		Assert.Equal(nameof(ScoresController.Entry), redirect.ActionName);
		Assert.Equal(2026, redirect.RouteValues?["year"]);
		Assert.Equal("10u-boys", redirect.RouteValues?["division"]);
		Assert.Equal(2, redirect.RouteValues?["round"]);
	}

	[Fact]
	public async Task UpsertAndRemoveKftm_DelegateToScoreEntryService()
	{
		var scoreService = new Mock<IScoreEntryService>(MockBehavior.Strict);
		scoreService.Setup(s => s.UpsertKftmOverrideAsync(
				It.Is<KftmOverrideUpsertDto>(u => u.Scope == TiebreakerScope.PsoStandings && u.WinningTeamId == 2),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionStandingsSnapshotDocument());
		scoreService.Setup(s => s.RemoveKftmOverrideAsync(99, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionStandingsSnapshotDocument());

		var controller = CreateController(
			Mock.Of<ITournamentYearService>(),
			Mock.Of<IPoolPlayStandingsService>(),
			Mock.Of<IChampionshipBracketService>(),
			Mock.Of<IPsoStandingsService>(),
			scoreService.Object,
			Mock.Of<ITournamentDataContext>());

		var upsertRedirect = Assert.IsType<RedirectToActionResult>(
			await controller.UpsertKftm(2026, "10u-boys", 8, TiebreakerScope.PsoStandings, 1, 2, 2));
		Assert.Equal("kftm", upsertRedirect.RouteValues?["tab"]);

		var removeRedirect = Assert.IsType<RedirectToActionResult>(
			await controller.RemoveKftm(2026, "10u-boys", 99));
		Assert.Equal("kftm", removeRedirect.RouteValues?["tab"]);
	}

	private static ScoresController CreateController(
		ITournamentYearService yearService,
		IPoolPlayStandingsService poolService,
		IChampionshipBracketService bracketService,
		IPsoStandingsService psoService,
		IScoreEntryService scoreService,
		ITournamentDataContext dataContext)
	{
		var httpContext = new DefaultHttpContext();
		return new ScoresController(yearService, poolService, bracketService, psoService, scoreService, dataContext)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
			TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>()),
		};
	}
}
