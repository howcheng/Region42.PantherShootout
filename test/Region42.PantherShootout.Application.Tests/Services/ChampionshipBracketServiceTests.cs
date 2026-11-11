using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class ChampionshipBracketServiceTests
{
	[Fact]
	public async Task SeedAndProgressAsync_SixTeam_SyncsPsoRound4AndResolvesChampion()
	{
		var division = new Division
		{
			Id = 4,
			TournamentYearId = 1,
			AgeGroup = AgeGroup.U10,
			Gender = Gender.Boys,
			Name = "10U Boys",
			Size = DivisionSize.Six,
		};

		var championshipGames = ChampionshipBracketBuilder.CreateBracketShells(4, DivisionSize.Six);
		for (var i = 0; i < championshipGames.Count; i++)
		{
			championshipGames[i].Id = 20 + i;
		}

		var shootouts = championshipGames
			.Where(g => g.Slot is BracketSlot.Consolation or BracketSlot.Semifinal1 or BracketSlot.Semifinal2)
			.Select((g, index) => new ShootoutRound
			{
				Id = 50 + index,
				DivisionId = 4,
				Round = 4,
				ChampionshipGameId = g.Id,
			})
			.ToList();

		var poolStandings = new DivisionPoolPlayStandingsResult
		{
			DivisionId = 4,
			DivisionName = division.Name,
			AllPoolsFinal = true,
			Pools =
			[
				Pool("A", [1, 2, 3]),
				Pool("B", [4, 5, 6]),
			],
		};

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.SetupGet(c => c.Divisions).Returns([division]);
		context.SetupGet(c => c.ChampionshipGames).Returns(championshipGames);
		context.SetupGet(c => c.ShootoutRounds).Returns(shootouts);

		var poolService = new Mock<IPoolPlayStandingsService>(MockBehavior.Strict);
		poolService
			.Setup(s => s.CalculateForDivisionAsync(4, It.IsAny<CancellationToken>()))
			.ReturnsAsync(poolStandings);

		var service = new ChampionshipBracketService(context.Object, poolService.Object);

		var seeded = await service.SeedAndProgressAsync(4);
		Assert.Equal(1, Find(championshipGames, BracketSlot.Semifinal1).HomeTeamId);
		Assert.All(shootouts, r => Assert.NotNull(r.HomeTeamId));

		Find(championshipGames, BracketSlot.Semifinal1).HomeGoals = 2;
		Find(championshipGames, BracketSlot.Semifinal1).AwayGoals = 0;
		Find(championshipGames, BracketSlot.Semifinal2).HomeGoals = 1;
		Find(championshipGames, BracketSlot.Semifinal2).AwayGoals = 0;
		await service.SeedAndProgressAsync(4);
		Assert.Equal(1, Find(championshipGames, BracketSlot.Championship).HomeTeamId);
		Assert.Equal(4, Find(championshipGames, BracketSlot.Championship).AwayTeamId);

		Find(championshipGames, BracketSlot.Championship).HomeGoals = 1;
		Find(championshipGames, BracketSlot.Championship).AwayGoals = 0;
		var complete = await service.SeedAndProgressAsync(4);

		Assert.Equal(1, complete.ChampionTeamId);
		Assert.Equal(1, seeded.PsoRound4Assignments.Count(a => a.ChampionshipSlot == BracketSlot.Consolation));
	}

	private static ChampionshipGame Find(IEnumerable<ChampionshipGame> games, BracketSlot slot) =>
		games.Single(g => g.Slot == slot);

	private static PoolPlayStandingsResult Pool(string name, IReadOnlyList<int> rankedTeamIds) => new()
	{
		PoolName = name,
		IsFinal = true,
		Standings = rankedTeamIds
			.Select((id, index) => new PoolPlayStandingDto
			{
				Rank = index + 1,
				TeamId = id,
				TeamName = $"{name}{index + 1}",
				PoolName = name,
			})
			.ToList(),
	};
}
