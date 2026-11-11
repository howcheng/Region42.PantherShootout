using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class ScoreEntryServiceTests
{
	[Fact]
	public async Task SavePoolPlayScoreAsync_UpdatesGameAndRefreshesStandings()
	{
		var game = new PoolPlayGame
		{
			Id = 11,
			DivisionId = 5,
			PoolId = 1,
			Round = 1,
			HomeTeamId = 1,
			AwayTeamId = 2,
		};

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.SetupGet(c => c.PoolPlayGames).Returns([game]);
		context.Setup(c => c.LoadDivisionDataAsync(5, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.Setup(c => c.Update(game));

		var snapshot = new DivisionStandingsSnapshotDocument { ChampionTeamId = 1 };
		var refresh = new Mock<IStandingsRefreshService>(MockBehavior.Strict);
		refresh
			.Setup(r => r.RefreshDivisionAsync(5, true, It.IsAny<CancellationToken>()))
			.ReturnsAsync(snapshot);

		var service = new ScoreEntryService(context.Object, refresh.Object);

		var result = await service.SavePoolPlayScoreAsync(new PoolPlayScoreUpdateDto
		{
			GameId = 11,
			HomeGoals = 2,
			AwayGoals = 0,
			HomeYellowCards = 1,
		});

		Assert.Equal(2, game.HomeGoals);
		Assert.Equal(0, game.AwayGoals);
		Assert.Equal(1, game.HomeYellowCards);
		Assert.Same(snapshot, result);
		context.Verify(c => c.Update(game), Times.Once);
		refresh.VerifyAll();
	}

	[Fact]
	public async Task SavePoolPlayScoreAsync_RejectsInvalidForfeitScore()
	{
		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		var refresh = new Mock<IStandingsRefreshService>(MockBehavior.Strict);
		var service = new ScoreEntryService(context.Object, refresh.Object);

		var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SavePoolPlayScoreAsync(
			new PoolPlayScoreUpdateDto
			{
				GameId = 1,
				HomeGoals = 3,
				AwayGoals = 0,
				IsForfeit = true,
			}));

		Assert.Contains("1-0", ex.Message);
	}

	[Fact]
	public async Task SaveChampionshipScoreAsync_RequiresSeededTeams()
	{
		var game = new ChampionshipGame
		{
			Id = 20,
			DivisionId = 5,
			Slot = BracketSlot.Championship,
			HomeSourceDescription = "1st",
			AwaySourceDescription = "2nd",
			HomeTeamId = null,
			AwayTeamId = null,
		};

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.SetupGet(c => c.ChampionshipGames).Returns([game]);
		context.Setup(c => c.LoadDivisionDataAsync(5, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

		var service = new ScoreEntryService(context.Object, Mock.Of<IStandingsRefreshService>());

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveChampionshipScoreAsync(
			new ChampionshipScoreUpdateDto { GameId = 20, HomeGoals = 1, AwayGoals = 0 }));
	}

	[Fact]
	public async Task SavePsoScoreAsync_UpdatesPkGoalsAndRefreshes()
	{
		var round = new ShootoutRound
		{
			Id = 30,
			DivisionId = 7,
			Round = 1,
			HomeTeamId = 1,
			AwayTeamId = 2,
		};

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.SetupGet(c => c.ShootoutRounds).Returns([round]);
		context.Setup(c => c.LoadDivisionDataAsync(7, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.Setup(c => c.Update(round));

		var refresh = new Mock<IStandingsRefreshService>(MockBehavior.Strict);
		refresh
			.Setup(r => r.RefreshDivisionAsync(7, true, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionStandingsSnapshotDocument());

		var service = new ScoreEntryService(context.Object, refresh.Object);

		await service.SavePsoScoreAsync(new PsoScoreUpdateDto
		{
			ShootoutRoundId = 30,
			HomePkGoals = 3,
			AwayPkGoals = 2,
		});

		Assert.Equal(3, round.HomePkGoals);
		Assert.Equal(2, round.AwayPkGoals);
	}

	[Fact]
	public async Task UpsertKftmOverrideAsync_CreatesNormalizedOverrideAndRefreshes()
	{
		TiebreakerOverride? added = null;
		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.LoadDivisionDataAsync(9, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.SetupGet(c => c.Divisions).Returns(
		[
			new Division
			{
				Id = 9,
				TournamentYearId = 1,
				AgeGroup = AgeGroup.U12,
				Gender = Gender.Boys,
				Name = "12U Boys",
				Size = DivisionSize.Four,
			},
		]);
		context.SetupGet(c => c.TiebreakerOverrides).Returns([]);
		context.Setup(c => c.AllocateNextId()).Returns(100);
		context
			.Setup(c => c.Add(It.IsAny<TiebreakerOverride>()))
			.Callback<TiebreakerOverride>(o => added = o);

		var refresh = new Mock<IStandingsRefreshService>(MockBehavior.Strict);
		refresh
			.Setup(r => r.RefreshDivisionAsync(9, true, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionStandingsSnapshotDocument());

		var service = new ScoreEntryService(context.Object, refresh.Object);

		await service.UpsertKftmOverrideAsync(new KftmOverrideUpsertDto
		{
			DivisionId = 9,
			Scope = TiebreakerScope.PoolPlayStandings,
			TeamAId = 5,
			TeamBId = 2,
			WinningTeamId = 5,
		});

		Assert.NotNull(added);
		Assert.Equal(2, added!.TeamAId); // normalized min
		Assert.Equal(5, added.TeamBId);
		Assert.Equal(5, added.WinningTeamId);
		Assert.Equal(TiebreakerScope.PoolPlayStandings, added.Scope);
	}

	[Fact]
	public async Task RemoveKftmOverrideAsync_RemovesAndRefreshes()
	{
		var existing = new TiebreakerOverride
		{
			Id = 44,
			DivisionId = 3,
			Scope = TiebreakerScope.PsoStandings,
			TeamAId = 1,
			TeamBId = 2,
			WinningTeamId = 1,
		};

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.SetupGet(c => c.TiebreakerOverrides).Returns([existing]);
		context.Setup(c => c.LoadDivisionDataAsync(3, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.Setup(c => c.Remove(existing));

		var refresh = new Mock<IStandingsRefreshService>(MockBehavior.Strict);
		refresh
			.Setup(r => r.RefreshDivisionAsync(3, true, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionStandingsSnapshotDocument());

		var service = new ScoreEntryService(context.Object, refresh.Object);

		await service.RemoveKftmOverrideAsync(44);

		context.Verify(c => c.Remove(existing), Times.Once);
	}

	[Theory]
	[InlineData(null, 1)]
	[InlineData(1, null)]
	public async Task SavePoolPlayScoreAsync_RejectsPartialScores(int? home, int? away)
	{
		var service = new ScoreEntryService(
			Mock.Of<ITournamentDataContext>(),
			Mock.Of<IStandingsRefreshService>());

		await Assert.ThrowsAsync<ArgumentException>(() => service.SavePoolPlayScoreAsync(
			new PoolPlayScoreUpdateDto { GameId = 1, HomeGoals = home, AwayGoals = away }));
	}
}
