using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Helpers;
using Xunit;

namespace Region42.PantherShootout.Domain.Tests.Helpers;

public class DocumentEntityMapperTests
{
	[Fact]
	public void TournamentAndDivisionHierarchy_RoundTripsBetweenEntitiesAndDocuments()
	{
		var year = new TournamentYear
		{
			Id = 1,
			Year = 2026,
			Name = "2026 Panther Shootout",
			IsActive = true,
			CustomMessage = "Welcome!",
		};

		var division = new Division
		{
			Id = 10,
			TournamentYearId = year.Id,
			AgeGroup = AgeGroup.U12,
			Gender = Gender.Girls,
			Name = "12U Girls",
			Size = DivisionSize.Six,
		};

		var poolA = new Pool { Id = 100, DivisionId = division.Id, Name = "A" };
		var poolB = new Pool { Id = 101, DivisionId = division.Id, Name = "B" };

		var teams = new List<Team>
		{
			new() { Id = 1002, PoolId = poolA.Id, Pool = poolA, TeamNumber = 2, Name = "A2 Simi Valley: Jones" },
			new() { Id = 1001, PoolId = poolA.Id, Pool = poolA, TeamNumber = 1, Name = "A1 Newbury Park: Smith" },
			new() { Id = 1003, PoolId = poolB.Id, Pool = poolB, TeamNumber = 1, Name = "B1 Thousand Oaks: Lee" },
		};

		var yearDoc = DocumentEntityMapper.ToDocument(year);
		var divisionDoc = DocumentEntityMapper.ToDocument(division, [poolB, poolA], teams);

		Assert.Equal("12u-girls", divisionDoc.Key);
		Assert.Equal(6, divisionDoc.Size);
		Assert.Equal(["A", "B"], divisionDoc.Pools.Select(p => p.Name).ToArray());
		Assert.Equal([1, 2], divisionDoc.Pools[0].Teams.Select(t => t.TeamNumber).ToArray());

		var rehydratedYear = DocumentEntityMapper.ToEntity(yearDoc);
		var rehydratedDivision = DocumentEntityMapper.ToEntity(divisionDoc, rehydratedYear.Id, rehydratedYear);
		var rehydratedPoolA = DocumentEntityMapper.ToEntity(divisionDoc.Pools[0], rehydratedDivision.Id, rehydratedDivision);
		var rehydratedTeamA1 = DocumentEntityMapper.ToEntity(divisionDoc.Pools[0].Teams[0], rehydratedPoolA.Id, rehydratedPoolA);

		Assert.Equal(2026, rehydratedYear.Year);
		Assert.Equal(DivisionSize.Six, rehydratedDivision.Size);
		Assert.Equal("A1", rehydratedTeamA1.GetDesignation());
	}

	[Fact]
	public void GamesShootoutRoundsAndOverrides_RoundTripBetweenEntitiesAndDocuments()
	{
		var modifiedAt = new DateTime(2026, 9, 12, 18, 30, 0, DateTimeKind.Utc);

		var poolGame = new PoolPlayGame
		{
			Id = 50,
			DivisionId = 10,
			PoolId = 100,
			Round = 1,
			ScheduledDateTime = modifiedAt,
			Location = "Field 1",
			HomeTeamId = 1001,
			AwayTeamId = 1002,
			HomeGoals = 1,
			AwayGoals = 0,
			IsForfeit = true,
			HomeYellowCards = 1,
			AwayRedCards = 1,
			ModifiedAt = modifiedAt,
			ModifiedBy = "admin@aysoregion42.org",
		};

		var championshipGame = new ChampionshipGame
		{
			Id = 60,
			DivisionId = 10,
			Slot = BracketSlot.Semifinal1,
			HomeSourceDescription = "Pool A 1st place",
			AwaySourceDescription = "Pool B 2nd place",
			HomeTeamId = 1001,
			AwayTeamId = 1003,
			HomeGoals = 2,
			AwayGoals = 1,
			ModifiedAt = modifiedAt,
			ModifiedBy = "admin@aysoregion42.org",
		};

		var round4Shootout = new ShootoutRound
		{
			Id = 70,
			DivisionId = 10,
			Round = 4,
			ChampionshipGameId = championshipGame.Id,
			HomeTeamId = 1001,
			AwayTeamId = 1003,
			HomePkGoals = 4,
			AwayPkGoals = 3,
			ModifiedAt = modifiedAt,
			ModifiedBy = "admin@aysoregion42.org",
		};

		var kftmOverride = new TiebreakerOverride
		{
			Id = 80,
			DivisionId = 10,
			Scope = TiebreakerScope.PsoStandings,
			TeamAId = 1001,
			TeamBId = 1002,
			WinningTeamId = 1002,
			ModifiedAt = modifiedAt,
			ModifiedBy = "admin@aysoregion42.org",
		};

		var rehydratedPoolGame = DocumentEntityMapper.ToEntity(DocumentEntityMapper.ToDocument(poolGame), 10);
		var rehydratedChampGame = DocumentEntityMapper.ToEntity(DocumentEntityMapper.ToDocument(championshipGame), 10);
		var rehydratedShootout = DocumentEntityMapper.ToEntity(
			DocumentEntityMapper.ToDocument(round4Shootout),
			10,
			championshipGame: rehydratedChampGame);
		var rehydratedOverride = DocumentEntityMapper.ToEntity(DocumentEntityMapper.ToDocument(kftmOverride), 10);

		Assert.True(rehydratedPoolGame.IsForfeit);
		Assert.Equal(GameWinner.Home, rehydratedPoolGame.GetWinner());
		Assert.Equal(1, rehydratedPoolGame.HomeYellowCards);
		Assert.Equal(1, rehydratedPoolGame.AwayRedCards);

		Assert.Equal(BracketSlot.Semifinal1, rehydratedChampGame.Slot);
		Assert.Same(rehydratedShootout, rehydratedChampGame.ShootoutRound);
		Assert.True(rehydratedShootout.IsComplete);
		Assert.Equal(60, rehydratedShootout.ChampionshipGameId);

		Assert.Equal(TiebreakerScope.PsoStandings, rehydratedOverride.Scope);
		Assert.Equal(1002, rehydratedOverride.WinningTeamId);
	}

	[Fact]
	public void DiscreteTeamAndRegionDocuments_RoundTripCleanly()
	{
		var region = new AysoRegion
		{
			Id = 7,
			RegionNumber = 121,
			Name = "Simi Valley",
		};

		var regionDoc = DocumentEntityMapper.ToDocument(region);
		var rehydratedRegion = DocumentEntityMapper.ToEntity(regionDoc);

		Assert.Equal(7, rehydratedRegion.Id);
		Assert.Equal(121, rehydratedRegion.RegionNumber);
		Assert.Equal("Simi Valley", rehydratedRegion.Name);

		var pool = new Pool { Id = 10, Name = "E" };
		var team = new Team
		{
			Id = 44,
			PoolId = pool.Id,
			Pool = pool,
			TeamNumber = 2,
			RegionNumber = 638,
			RegionName = "Quartz Hill",
			CoachName = "Carper",
		};

		var teamDoc = DocumentEntityMapper.ToDocument(team);
		Assert.Equal(638, teamDoc.RegionNumber);
		Assert.Equal("Carper", teamDoc.CoachName);
		Assert.Equal("Quartz Hill: Carper", teamDoc.Name);

		var rehydratedTeam = DocumentEntityMapper.ToEntity(teamDoc, pool.Id, pool);
		Assert.Equal("E2", rehydratedTeam.Code);
		Assert.Equal(638, rehydratedTeam.RegionNumber);
		Assert.Equal("Carper", rehydratedTeam.CoachName);
		Assert.Equal("Quartz Hill: Carper", rehydratedTeam.Name);
	}
}
