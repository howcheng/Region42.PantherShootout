using Moq;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class PoolPlayStandingsServiceTests
{
	[Fact]
	public async Task CalculateForDivisionAsync_LoadsDivisionAndReturnsPoolStandings()
	{
		var division = new Division
		{
			Id = 7,
			TournamentYearId = 1,
			AgeGroup = AgeGroup.U10,
			Gender = Gender.Girls,
			Name = "10U Girls",
			Size = DivisionSize.Four,
		};
		var pool = new Pool { Id = 3, DivisionId = 7, Name = "A" };
		var teams = Enumerable.Range(1, 4)
			.Select(i => new Team
			{
				Id = i,
				PoolId = 3,
				Pool = pool,
				Name = $"Team {i}",
				TeamNumber = i,
			})
			.ToList();
		pool.Teams = teams;

		var games = new List<PoolPlayGame>
		{
			new()
			{
				Id = 1,
				DivisionId = 7,
				PoolId = 3,
				Round = 1,
				HomeTeamId = 1,
				AwayTeamId = 2,
				HomeGoals = 2,
				AwayGoals = 0,
			},
		};

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context
			.Setup(c => c.LoadDivisionDataAsync(7, It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);
		context.SetupGet(c => c.Divisions).Returns([division]);
		context.SetupGet(c => c.Pools).Returns([pool]);
		context.SetupGet(c => c.Teams).Returns(teams);
		context.SetupGet(c => c.PoolPlayGames).Returns(games);
		context.SetupGet(c => c.TiebreakerOverrides).Returns(Array.Empty<TiebreakerOverride>());

		var service = new PoolPlayStandingsService(context.Object);

		var result = await service.CalculateForDivisionAsync(7);

		Assert.Equal(7, result.DivisionId);
		Assert.Single(result.Pools);
		Assert.Equal(4, result.Pools[0].Standings.Count);
		Assert.Equal(9, result.Pools[0].Standings.Single(s => s.TeamId == 1).Points);
		context.VerifyAll();
	}

	[Fact]
	public async Task CalculateForDivisionAsync_ThrowsWhenDivisionMissing()
	{
		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context
			.Setup(c => c.LoadDivisionDataAsync(99, It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);
		context.SetupGet(c => c.Divisions).Returns(Array.Empty<Division>());

		var service = new PoolPlayStandingsService(context.Object);

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.CalculateForDivisionAsync(99));
	}
}
