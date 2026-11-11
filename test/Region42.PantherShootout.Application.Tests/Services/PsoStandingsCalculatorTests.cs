using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class PsoStandingsCalculatorTests
{
	[Fact]
	public void Calculate_ThreeRoundDivision_SumsPkGoalsAndRanksByTotalFor()
	{
		var division = CreateDivision(DivisionSize.Four);
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, 4);

		var rounds = new List<ShootoutRound>
		{
			Complete(1, 1, teams[0].Id, teams[1].Id, homePk: 3, awayPk: 1),
			Complete(2, 1, teams[2].Id, teams[3].Id, homePk: 2, awayPk: 2),
			Complete(3, 2, teams[0].Id, teams[2].Id, homePk: 2, awayPk: 0),
			Complete(4, 2, teams[1].Id, teams[3].Id, homePk: 1, awayPk: 4),
			Complete(5, 3, teams[0].Id, teams[3].Id, homePk: 1, awayPk: 1),
			Complete(6, 3, teams[1].Id, teams[2].Id, homePk: 0, awayPk: 3),
		};

		var result = PsoStandingsCalculator.Calculate(division, teams, rounds);

		Assert.True(result.IsFinal);
		// T4: 2+4+1 = 7; T1: 3+2+1 = 6; T3: 2+0+3 = 5; T2: 1+1+0 = 2
		Assert.Equal(teams[3].Id, result.WinnerTeamId);
		Assert.Equal(7, result.Standings[0].TotalPkGoalsFor);
		Assert.Equal(3, result.Standings[0].RoundsCompleted);
		Assert.Equal(teams[0].Id, result.Standings[1].TeamId);
		Assert.Equal(teams[2].Id, result.Standings[2].TeamId);
		Assert.False(result.RequiresKftmTiebreaker);
	}

	[Fact]
	public void Calculate_IgnoresIncompleteRounds()
	{
		var division = CreateDivision(DivisionSize.Four);
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, 2);
		var rounds = new List<ShootoutRound>
		{
			new()
			{
				Id = 1,
				DivisionId = 1,
				Round = 1,
				HomeTeamId = teams[0].Id,
				AwayTeamId = teams[1].Id,
				HomePkGoals = 2,
				AwayPkGoals = null,
			},
		};

		var result = PsoStandingsCalculator.Calculate(division, teams, rounds);

		Assert.All(result.Standings, s => Assert.Equal(0, s.RoundsCompleted));
		Assert.False(result.IsFinal);
	}

	[Fact]
	public void Calculate_FiveTeam_FourthRoundCap_ExcludesRound4WhenThreeEarlyMatchupsExist()
	{
		var division = CreateDivision(DivisionSize.Five);
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, 5);
		var team = teams[0];

		// Team 1 already has 3 complete matchups in rounds 1-3; round 4 must not count.
		var rounds = new List<ShootoutRound>
		{
			Complete(1, 1, team.Id, teams[1].Id, 2, 1),
			Complete(2, 2, team.Id, teams[2].Id, 2, 0),
			Complete(3, 3, team.Id, teams[3].Id, 1, 1),
			Complete(4, 4, team.Id, teams[4].Id, 5, 0), // would add 5 if counted
		};

		var result = PsoStandingsCalculator.Calculate(division, teams, rounds);
		var row = result.Standings.Single(s => s.TeamId == team.Id);

		Assert.Equal(3, row.RoundsCompleted);
		Assert.Equal(5, row.TotalPkGoalsFor); // 2+2+1, not +5
		Assert.Null(row.RoundScores[3]); // round 4 excluded from counted scores
	}

	[Fact]
	public void Calculate_FiveTeam_FourthRoundCountsWhenFewerThanThreeEarlyMatchups()
	{
		var division = CreateDivision(DivisionSize.Five);
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, 5);
		var team = teams[0];

		// Bye in round 3 → only 2 early matchups; round 4 fills the 3rd.
		var rounds = new List<ShootoutRound>
		{
			Complete(1, 1, team.Id, teams[1].Id, 2, 1),
			Complete(2, 2, team.Id, teams[2].Id, 1, 0),
			Complete(3, 4, team.Id, teams[3].Id, 3, 2),
		};

		var result = PsoStandingsCalculator.Calculate(division, teams, rounds);
		var row = result.Standings.Single(s => s.TeamId == team.Id);

		Assert.Equal(3, row.RoundsCompleted);
		Assert.Equal(6, row.TotalPkGoalsFor); // 2+1+3
		Assert.Equal(3, row.RoundScores[3]);
	}

	[Fact]
	public void Calculate_SixTeam_IncludesChampionshipRound4Matchups()
	{
		var division = CreateDivision(DivisionSize.Six);
		var poolA = CreatePool(1, "A");
		var poolB = CreatePool(2, "B");
		var teamsA = CreateTeams(poolA, 3, startingTeamId: 1);
		var teamsB = CreateTeams(poolB, 3, startingTeamId: 4);
		var allTeams = teamsA.Concat(teamsB).ToList();

		// Each team: 2 pool PSO rounds + 1 championship Round 4 = 3 matchups.
		var rounds = new List<ShootoutRound>
		{
			Complete(1, 1, teamsA[0].Id, teamsA[1].Id, 2, 1),
			Complete(2, 1, teamsB[0].Id, teamsB[1].Id, 1, 1),
			Complete(3, 2, teamsA[0].Id, teamsA[2].Id, 1, 0),
			Complete(4, 2, teamsB[0].Id, teamsB[2].Id, 2, 0),
			// Round 3 byes / other pairings omitted; Round 4 championship slots:
			Complete(10, 4, teamsA[0].Id, teamsB[1].Id, 3, 1, championshipGameId: 100), // A1 vs B2
			Complete(11, 4, teamsB[0].Id, teamsA[1].Id, 0, 2, championshipGameId: 101), // B1 vs A2
			Complete(12, 4, teamsA[2].Id, teamsB[2].Id, 1, 1, championshipGameId: 102), // A3 vs B3
			// Fill remaining early matchups so every team reaches 3:
			Complete(5, 3, teamsA[1].Id, teamsA[2].Id, 1, 2),
			Complete(6, 3, teamsB[1].Id, teamsB[2].Id, 2, 1),
		};

		var result = PsoStandingsCalculator.Calculate(division, allTeams, rounds);

		Assert.True(result.IsFinal);
		Assert.Equal(4, result.Standings.Single(s => s.TeamId == teamsA[0].Id).RoundScores.Count);
		Assert.Equal(3, result.Standings.Single(s => s.TeamId == teamsA[0].Id).RoundScores[3]); // R4 PK
		Assert.Equal(teamsA[0].Id, result.Standings[0].TeamId); // 2+1+3 = 6
	}

	[Fact]
	public void Calculate_TieOnGoalsFor_UsesGoalsAgainstThenKftm()
	{
		var division = CreateDivision(DivisionSize.Four);
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, 2);

		// Equal GF and equal GA to force KFTM:
		var rounds = new List<ShootoutRound>
		{
			Complete(1, 1, teams[0].Id, teams[1].Id, 2, 1), // T1: +2 / +1; T2: +1 / +2
			Complete(2, 2, teams[0].Id, teams[1].Id, 1, 2), // T1: +1 / +2; T2: +2 / +1 → both 3 for, 3 against
		};

		var without = PsoStandingsCalculator.Calculate(division, teams, rounds);
		Assert.True(without.RequiresKftmTiebreaker);
		Assert.Null(without.WinnerTeamId);

		var overrides = new List<TiebreakerOverride>
		{
			new()
			{
				DivisionId = 1,
				Scope = TiebreakerScope.PsoStandings,
				TeamAId = teams[0].Id,
				TeamBId = teams[1].Id,
				WinningTeamId = teams[1].Id,
			},
		};

		var with = PsoStandingsCalculator.Calculate(division, teams, rounds, overrides);
		Assert.Equal(teams[1].Id, with.Standings[0].TeamId);
		Assert.False(with.RequiresKftmTiebreaker);
	}

	[Fact]
	public void Calculate_IgnoresPoolPlayKftmOverrides()
	{
		var division = CreateDivision(DivisionSize.Four);
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, 2);
		var rounds = new List<ShootoutRound>
		{
			Complete(1, 1, teams[0].Id, teams[1].Id, 1, 1),
			Complete(2, 2, teams[0].Id, teams[1].Id, 1, 1),
		};
		var overrides = new List<TiebreakerOverride>
		{
			new()
			{
				DivisionId = 1,
				Scope = TiebreakerScope.PoolPlayStandings,
				TeamAId = teams[0].Id,
				TeamBId = teams[1].Id,
				WinningTeamId = teams[0].Id,
			},
		};

		var result = PsoStandingsCalculator.Calculate(division, teams, rounds, overrides);
		Assert.True(result.RequiresKftmTiebreaker);
	}

	[Fact]
	public void Calculate_TenTeam_RanksAcrossPoolsTogether()
	{
		var division = CreateDivision(DivisionSize.Ten);
		var poolA = CreatePool(1, "A");
		var poolB = CreatePool(2, "B");
		var teamsA = CreateTeams(poolA, 2, startingTeamId: 1);
		var teamsB = CreateTeams(poolB, 2, startingTeamId: 6);
		var all = teamsA.Concat(teamsB).ToList();

		var rounds = new List<ShootoutRound>
		{
			Complete(1, 1, teamsA[0].Id, teamsA[1].Id, 4, 0),
			Complete(2, 1, teamsB[0].Id, teamsB[1].Id, 1, 1),
		};

		var result = PsoStandingsCalculator.Calculate(division, all, rounds);

		Assert.Equal(4, result.Standings.Count);
		Assert.Equal(teamsA[0].Id, result.Standings[0].TeamId);
		Assert.Contains(result.Standings.Skip(1), s => s.TeamId == teamsB[0].Id);
		Assert.Contains(result.Standings.Skip(1), s => s.TeamId == teamsB[1].Id);
	}

	[Fact]
	public void SnapshotMapper_MapsWinnerAndRoundScores()
	{
		var division = CreateDivision(DivisionSize.Four);
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, 2);
		var rounds = new List<ShootoutRound>
		{
			Complete(1, 1, teams[0].Id, teams[1].Id, 2, 0),
			Complete(2, 2, teams[0].Id, teams[1].Id, 2, 0),
			Complete(3, 3, teams[0].Id, teams[1].Id, 2, 0),
		};

		var calculated = PsoStandingsCalculator.Calculate(division, teams, rounds);
		var snapshot = PsoStandingsSnapshotMapper.ToSnapshot(calculated);

		Assert.True(snapshot.IsFinal);
		Assert.Equal(teams[0].Id, snapshot.WinnerTeamId);
		Assert.Equal(6, snapshot.Teams[0].TotalPkGoalsFor);
		Assert.Equal(3, snapshot.Teams[0].RoundScores.Count);
	}

	private static Division CreateDivision(DivisionSize size) => new()
	{
		Id = 1,
		TournamentYearId = 1,
		AgeGroup = AgeGroup.U12,
		Gender = Gender.Girls,
		Name = "12U Girls",
		Size = size,
	};

	private static Pool CreatePool(int id, string name) => new()
	{
		Id = id,
		DivisionId = 1,
		Name = name,
	};

	private static List<Team> CreateTeams(Pool pool, int count, int startingTeamId = 1)
	{
		var teams = Enumerable.Range(0, count)
			.Select(i => new Team
			{
				Id = startingTeamId + i,
				PoolId = pool.Id,
				Pool = pool,
				Name = $"Team {startingTeamId + i}",
				TeamNumber = i + 1,
			})
			.ToList();
		pool.Teams = teams;
		return teams;
	}

	private static ShootoutRound Complete(
		int id,
		int round,
		int homeTeamId,
		int awayTeamId,
		int homePk,
		int awayPk,
		int? championshipGameId = null) => new()
	{
		Id = id,
		DivisionId = 1,
		Round = round,
		HomeTeamId = homeTeamId,
		AwayTeamId = awayTeamId,
		HomePkGoals = homePk,
		AwayPkGoals = awayPk,
		ChampionshipGameId = championshipGameId,
	};
}
