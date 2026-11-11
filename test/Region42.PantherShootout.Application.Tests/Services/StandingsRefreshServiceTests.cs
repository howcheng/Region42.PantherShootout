using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class StandingsRefreshServiceTests
{
	[Fact]
	public async Task RefreshDivisionAsync_MergesPoolBracketAndPsoSnapshotsAndSaves()
	{
		var poolResult = new DivisionPoolPlayStandingsResult
		{
			DivisionId = 8,
			DivisionName = "12U Boys",
			AllPoolsFinal = true,
			Pools =
			[
				new PoolPlayStandingsResult
				{
					PoolId = 1,
					PoolName = "A",
					IsFinal = true,
					Standings =
					[
						new PoolPlayStandingDto { Rank = 1, TeamId = 1, Points = 10 },
						new PoolPlayStandingDto { Rank = 2, TeamId = 2, Points = 8 },
					],
				},
			],
		};

		var bracketResult = new ChampionshipBracketResult
		{
			DivisionId = 8,
			Size = DivisionSize.Four,
			HasBracket = true,
			ChampionTeamId = 1,
		};

		var psoResult = new PsoStandingsResult
		{
			DivisionId = 8,
			IsFinal = true,
			WinnerTeamId = 2,
			Standings =
			[
				new PsoStandingDto { Rank = 1, TeamId = 2, TotalPkGoalsFor = 9, RoundsCompleted = 3 },
				new PsoStandingDto { Rank = 2, TeamId = 1, TotalPkGoalsFor = 4, RoundsCompleted = 3 },
			],
		};

		DivisionStandingsSnapshotDocument? savedSnapshot = null;

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context
			.Setup(c => c.SetStandingsForDivision(8, It.IsAny<DivisionStandingsSnapshotDocument>()))
			.Callback<int, DivisionStandingsSnapshotDocument>((_, snapshot) => savedSnapshot = snapshot);
		context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

		var poolService = new Mock<IPoolPlayStandingsService>(MockBehavior.Strict);
		poolService
			.Setup(s => s.CalculateForDivisionAsync(8, It.IsAny<CancellationToken>()))
			.ReturnsAsync(poolResult);

		var bracketService = new Mock<IChampionshipBracketService>(MockBehavior.Strict);
		bracketService
			.Setup(s => s.SeedAndProgressAsync(8, It.IsAny<CancellationToken>()))
			.ReturnsAsync(bracketResult);

		var psoService = new Mock<IPsoStandingsService>(MockBehavior.Strict);
		psoService
			.Setup(s => s.CalculateForDivisionAsync(8, It.IsAny<CancellationToken>()))
			.ReturnsAsync(psoResult);

		var service = new StandingsRefreshService(
			context.Object,
			poolService.Object,
			bracketService.Object,
			psoService.Object);

		var result = await service.RefreshDivisionAsync(8);

		Assert.NotNull(savedSnapshot);
		Assert.Same(savedSnapshot, result);
		Assert.Equal(1, result.ChampionTeamId); // bracket champion, not PSO winner
		Assert.Equal(2, result.PsoStandings.WinnerTeamId);
		Assert.Single(result.Pools);
		Assert.Equal(10, result.Pools[0].Teams[0].Points);
		context.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task RefreshDivisionAsync_WhenSaveChangesFalse_DoesNotPersist()
	{
		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.SetStandingsForDivision(3, It.IsAny<DivisionStandingsSnapshotDocument>()));

		var poolService = new Mock<IPoolPlayStandingsService>(MockBehavior.Strict);
		poolService
			.Setup(s => s.CalculateForDivisionAsync(3, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionPoolPlayStandingsResult { DivisionId = 3, Pools = [] });

		var bracketService = new Mock<IChampionshipBracketService>(MockBehavior.Strict);
		bracketService
			.Setup(s => s.SeedAndProgressAsync(3, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChampionshipBracketResult { DivisionId = 3, Size = DivisionSize.Five });

		var psoService = new Mock<IPsoStandingsService>(MockBehavior.Strict);
		psoService
			.Setup(s => s.CalculateForDivisionAsync(3, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PsoStandingsResult { DivisionId = 3 });

		var service = new StandingsRefreshService(
			context.Object,
			poolService.Object,
			bracketService.Object,
			psoService.Object);

		await service.RefreshDivisionAsync(3, saveChanges: false);

		context.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
	}
}
