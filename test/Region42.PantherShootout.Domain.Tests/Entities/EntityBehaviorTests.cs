using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Xunit;

namespace Region42.PantherShootout.Domain.Tests.Entities;

public class EntityBehaviorTests
{
	[Fact]
	public void Team_GetDesignation_CombinesPoolNameAndTeamNumber()
	{
		var pool = new Pool { Id = 1, Name = "B" };
		var team = new Team { Id = 2, PoolId = 1, Pool = pool, TeamNumber = 3, Name = "B3 Newbury Park: Rivera" };

		Assert.Equal("B3", team.GetDesignation());
		Assert.Equal("B3", team.Code);
	}

	[Fact]
	public void Team_DiscreteProperties_FormatsDisplayNameFromRegionAndCoach()
	{
		var pool = new Pool { Id = 1, Name = "C" };
		var team = new Team
		{
			Id = 5,
			PoolId = 1,
			Pool = pool,
			TeamNumber = 4,
			RegionNumber = 638,
			CoachName = "Carper",
		};

		Assert.Equal("C4", team.Code);
		Assert.Equal("Region 638: Carper", team.Name);

		team.RegionName = "Quartz Hill";
		Assert.Equal("Quartz Hill: Carper", team.Name);
	}

	[Theory]
	[InlineData(null, null, GameWinner.Undetermined)]
	[InlineData(2, null, GameWinner.Undetermined)]
	[InlineData(3, 1, GameWinner.Home)]
	[InlineData(0, 2, GameWinner.Away)]
	[InlineData(2, 2, GameWinner.Draw)]
	public void PoolPlayGame_GetWinner_ComparesGoals(int? homeGoals, int? awayGoals, GameWinner expected)
	{
		var game = new PoolPlayGame
		{
			HomeGoals = homeGoals,
			AwayGoals = awayGoals,
		};

		Assert.Equal(expected, game.GetWinner());
	}

	[Fact]
	public void ShootoutRound_IsComplete_RequiresTeamsAndBothScores()
	{
		var round = new ShootoutRound
		{
			DivisionId = 1,
			Round = 4,
			ChampionshipGameId = 10,
			HomePkGoals = 3,
			AwayPkGoals = 2,
		};

		Assert.False(round.IsComplete);

		round.HomeTeamId = 101;
		round.AwayTeamId = 102;

		Assert.True(round.IsComplete);
	}
}
