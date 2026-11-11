using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class PoolPlayStandingsCalculatorTests
{
	[Fact]
	public void CalculatePool_ThreeToZeroWin_AwardsMax10PointsAndUncappedGoals()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 3);
		// 4-0 win: 6 (win) + 3 (capped goals) + 1 (shutout) = 10
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 4, 0),
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games);

		var winner = result.Standings.Single(s => s.TeamId == teams[0].Id);
		var loser = result.Standings.Single(s => s.TeamId == teams[1].Id);

		Assert.Equal(10, winner.Points);
		Assert.Equal(4, winner.GoalsFor);
		Assert.Equal(0, winner.GoalsAgainst);
		Assert.Equal(4, winner.GoalDifferential);
		Assert.Equal(0, winner.CappedGoalsAgainst);
		Assert.Equal(3, winner.CappedGoalDifferential); // capped GF 3 - GA 0
		Assert.Equal(0, loser.Points);
		Assert.Equal(4, loser.GoalsAgainst);
		Assert.Equal(3, loser.CappedGoalsAgainst); // capped at 3 for TB
	}

	[Fact]
	public void CalculatePool_ScorelessDraw_Awards4PointsEach()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 2);
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 0, 0),
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games);

		Assert.All(result.Standings, s => Assert.Equal(4, s.Points)); // 3 draw + 1 shutout
	}

	[Fact]
	public void CalculatePool_FourToOneWin_Awards9PointsWithCappedGoalBonus()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 2);
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 4, 1),
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games);

		Assert.Equal(9, result.Standings.Single(s => s.TeamId == teams[0].Id).Points); // 6+3
		Assert.Equal(1, result.Standings.Single(s => s.TeamId == teams[1].Id).Points); // loss + 1 goal
	}

	[Fact]
	public void CalculatePool_Forfeit_Awards7PointsWithoutShutoutAndExcludesFromCappedTotals()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 2);
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 1, 0, isForfeit: true),
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games);

		Assert.True(result.AnyForfeitInScope);
		var winner = result.Standings.Single(s => s.TeamId == teams[0].Id);
		Assert.Equal(7, winner.Points);
		Assert.True(winner.HasForfeit);
		Assert.Equal(0, winner.CappedGoalsAgainst);
		Assert.Equal(0, winner.CappedGoalDifferential);
		Assert.Equal(1, winner.GoalsFor); // uncapped display still shows entered score
	}

	[Fact]
	public void CalculatePool_Cards_Deduct2PerRedAnd1PerYellow()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 2);
		// 2-1 win = 8 pts, then -2 RC -1 YC = 5
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 2, 1, homeRed: 1, homeYellow: 1),
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games);
		var team = result.Standings.Single(s => s.TeamId == teams[0].Id);

		Assert.Equal(5, team.Points);
		Assert.Equal(1, team.RedCards);
		Assert.Equal(1, team.YellowCards);
	}

	[Fact]
	public void CalculatePool_TwoWayTie_UsesHeadToHeadFirst()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 3);
		// Circular-ish: T1=10, T2=8, T3=8 with T3 beating T2 head-to-head
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 0, 1), // T2 beats T1 (8 pts)
			CreateGame(2, pool.Id, teams[1].Id, teams[2].Id, 0, 1), // T3 beats T2 (8 pts)
			CreateGame(3, pool.Id, teams[0].Id, teams[2].Id, 3, 0), // T1 beats T3 (10 pts)
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games);

		Assert.Equal(teams[0].Id, result.Standings[0].TeamId);
		Assert.Equal(teams[2].Id, result.Standings[1].TeamId); // H2H over T2
		Assert.Equal(teams[1].Id, result.Standings[2].TeamId);
		Assert.True(result.IsFinal);
	}

	[Fact]
	public void CalculatePool_ThreeWayTie_SkipsHeadToHeadAndRequiresKftmWhenStillTied()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 3);
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 1, 0),
			CreateGame(2, pool.Id, teams[1].Id, teams[2].Id, 1, 0),
			CreateGame(3, pool.Id, teams[2].Id, teams[0].Id, 1, 0),
		};

		var withoutOverride = PoolPlayStandingsCalculator.CalculatePool(teams, games);
		Assert.True(withoutOverride.RequiresKftmTiebreaker);
		Assert.Equal(3, withoutOverride.TiedTeamIdsForKftm.Count);

		var overrides = new List<TiebreakerOverride>
		{
			new()
			{
				DivisionId = 1,
				Scope = TiebreakerScope.PoolPlayStandings,
				TeamAId = teams[0].Id,
				TeamBId = teams[2].Id,
				WinningTeamId = teams[2].Id,
			},
			new()
			{
				DivisionId = 1,
				Scope = TiebreakerScope.PoolPlayStandings,
				TeamAId = teams[1].Id,
				TeamBId = teams[2].Id,
				WinningTeamId = teams[2].Id,
			},
			new()
			{
				DivisionId = 1,
				Scope = TiebreakerScope.PoolPlayStandings,
				TeamAId = teams[0].Id,
				TeamBId = teams[1].Id,
				WinningTeamId = teams[0].Id,
			},
		};

		var withOverride = PoolPlayStandingsCalculator.CalculatePool(teams, games, overrides);
		Assert.Equal(teams[2].Id, withOverride.Standings[0].TeamId);
		Assert.Equal(teams[0].Id, withOverride.Standings[1].TeamId);
		Assert.Equal(teams[1].Id, withOverride.Standings[2].TeamId);
		Assert.False(withOverride.RequiresKftmTiebreaker);
	}

	[Fact]
	public void CalculatePool_WhenForfeitExists_OmitsWinsAndGoalDifferentialFromTiebreakers()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 4);
		var games = new List<PoolPlayGame>
		{
			CreateGame(6, pool.Id, teams[2].Id, teams[3].Id, 1, 0, isForfeit: true),
			CreateGame(5, pool.Id, teams[0].Id, teams[1].Id, 0, 0),
			// T1 beats T4 1-0 with 1 RC => +6 pts (total 10), 1 win
			CreateGame(1, pool.Id, teams[0].Id, teams[3].Id, 1, 0, homeRed: 1),
			// T2 ties T3 3-3 => +6 pts (total 10), 0 wins, 0 RC
			CreateGame(2, pool.Id, teams[1].Id, teams[2].Id, 3, 3),
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games);

		Assert.True(result.AnyForfeitInScope);
		var t1 = result.Standings.Single(s => s.TeamId == teams[0].Id);
		var t2 = result.Standings.Single(s => s.TeamId == teams[1].Id);
		Assert.Equal(10, t1.Points);
		Assert.Equal(10, t2.Points);
		Assert.Equal(1, t1.Wins);
		Assert.Equal(0, t2.Wins);
		// Wins nullified; fewer red cards ranks T2 ahead of T1
		Assert.True(t2.Rank < t1.Rank);
	}

	[Fact]
	public void CalculatePool_Misconduct_PrefersFewerRedCardsOverYellowCards()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 2);
		// Both end at same points via 1-1 draw; T1 has 1 RC, T2 has 2 YC.
		// Points: each gets 3+1=4, then T1: 4-2=2, T2: 4-2=2.
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 1, 1, homeRed: 1, awayYellow: 2),
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games);

		Assert.Equal(2, result.Standings[0].Points);
		Assert.Equal(2, result.Standings[1].Points);
		// Fewer red cards wins: T2 (0 RC, 2 YC) ranks above T1 (1 RC, 0 YC)
		Assert.Equal(teams[1].Id, result.Standings[0].TeamId);
		Assert.Equal(teams[0].Id, result.Standings[1].TeamId);
	}

	[Fact]
	public void CalculatePool_IncompleteSchedule_IsNotFinalAndDoesNotRequireKftm()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 3);
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 1, 0),
			// Remaining two games not played
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games, expectedGamesInPool: 3);

		Assert.False(result.IsFinal);
		Assert.False(result.RequiresKftmTiebreaker);
	}

	[Fact]
	public void CalculatePool_IgnoresPsoScopeKftmOverrides()
	{
		var pool = CreatePool(1, "A");
		var teams = CreateTeams(pool, count: 3);
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, pool.Id, teams[0].Id, teams[1].Id, 1, 0),
			CreateGame(2, pool.Id, teams[1].Id, teams[2].Id, 1, 0),
			CreateGame(3, pool.Id, teams[2].Id, teams[0].Id, 1, 0),
		};
		var overrides = new List<TiebreakerOverride>
		{
			new()
			{
				DivisionId = 1,
				Scope = TiebreakerScope.PsoStandings,
				TeamAId = teams[0].Id,
				TeamBId = teams[1].Id,
				WinningTeamId = teams[0].Id,
			},
		};

		var result = PoolPlayStandingsCalculator.CalculatePool(teams, games, overrides);

		Assert.True(result.RequiresKftmTiebreaker);
	}

	[Fact]
	public void CalculateDivision_TenTeam_BuildsCombinedStandingsAndUsesDivisionWideForfeitScope()
	{
		var division = CreateDivision(DivisionSize.Ten);
		var poolA = CreatePool(1, "A", division.Id);
		var poolB = CreatePool(2, "B", division.Id);
		var teamsA = CreateTeams(poolA, count: 5, startingTeamId: 1);
		var teamsB = CreateTeams(poolB, count: 5, startingTeamId: 6);

		// Only complete a couple of games: forfeit in pool B should nullify wins TB in pool A when ranking combined.
		var games = new List<PoolPlayGame>
		{
			CreateGame(1, poolA.Id, teamsA[0].Id, teamsA[1].Id, 2, 0, divisionId: division.Id),
			CreateGame(2, poolB.Id, teamsB[0].Id, teamsB[1].Id, 1, 0, isForfeit: true, divisionId: division.Id),
		};

		var result = PoolPlayStandingsCalculator.CalculateDivision(
			division,
			[poolA, poolB],
			teamsA.Concat(teamsB).ToList(),
			games);

		Assert.True(result.AnyForfeitInDivision);
		Assert.NotNull(result.CombinedDivisionStandings);
		Assert.Equal(10, result.CombinedDivisionStandings!.Standings.Count);
		Assert.True(result.Pools.All(p => p.AnyForfeitInScope)); // division-wide forfeit scope
		Assert.Null(result.PoolWinnersStandings);
	}

	[Fact]
	public void CalculateDivision_TwelveTeam_RanksPoolWinnersAndRunnersUp()
	{
		var division = CreateDivision(DivisionSize.Twelve);
		var pools = new[]
		{
			CreatePool(1, "A", division.Id),
			CreatePool(2, "B", division.Id),
			CreatePool(3, "C", division.Id),
		};

		var allTeams = new List<Team>();
		var allGames = new List<PoolPlayGame>();
		var gameId = 1;

		// Each pool: dominant #1 with 10 pts (3-0), #2 with 8 pts (1-0), others lose.
		foreach (var pool in pools)
		{
			var teams = CreateTeams(pool, count: 4, startingTeamId: pool.Id * 10);
			allTeams.AddRange(teams);

			allGames.Add(CreateGame(gameId++, pool.Id, teams[0].Id, teams[1].Id, 3, 0, divisionId: division.Id));
			allGames.Add(CreateGame(gameId++, pool.Id, teams[0].Id, teams[2].Id, 3, 0, divisionId: division.Id));
			allGames.Add(CreateGame(gameId++, pool.Id, teams[0].Id, teams[3].Id, 3, 0, divisionId: division.Id));
			allGames.Add(CreateGame(gameId++, pool.Id, teams[1].Id, teams[2].Id, 1, 0, divisionId: division.Id));
			allGames.Add(CreateGame(gameId++, pool.Id, teams[1].Id, teams[3].Id, 1, 0, divisionId: division.Id));
			allGames.Add(CreateGame(gameId++, pool.Id, teams[2].Id, teams[3].Id, 1, 0, divisionId: division.Id));
		}

		var result = PoolPlayStandingsCalculator.CalculateDivision(division, pools, allTeams, allGames);

		Assert.True(result.AllPoolsFinal);
		Assert.NotNull(result.PoolWinnersStandings);
		Assert.NotNull(result.RunnersUpStandings);
		Assert.Equal(3, result.PoolWinnersStandings!.Standings.Count);
		Assert.Equal(3, result.RunnersUpStandings!.Standings.Count);
		Assert.All(result.PoolWinnersStandings.Standings, s => Assert.Equal(30, s.Points)); // 3×10
		Assert.All(result.RunnersUpStandings.Standings, s => Assert.True(s.Points >= 8));
		Assert.Null(result.CombinedDivisionStandings);
	}

	[Fact]
	public void SnapshotMapper_MapsPoolRowsAndCombinedStandings()
	{
		var division = CreateDivision(DivisionSize.Four);
		var pool = CreatePool(1, "A", division.Id);
		var teams = CreateTeams(pool, count: 4);
		var games = CreateRoundRobinShutouts(pool, teams, division.Id);

		var calculated = PoolPlayStandingsCalculator.CalculateDivision(division, [pool], teams, games);
		var snapshot = PoolPlayStandingsSnapshotMapper.ToSnapshot(calculated);

		Assert.Single(snapshot.Pools);
		Assert.Equal(4, snapshot.Pools[0].Teams.Count);
		Assert.True(snapshot.Pools[0].IsFinal);
		Assert.Equal(calculated.Pools[0].Standings[0].TeamId, snapshot.ChampionTeamId);
		Assert.Equal(calculated.Pools[0].Standings[0].Points, snapshot.Pools[0].Teams[0].Points);
	}

	private static Division CreateDivision(DivisionSize size, int id = 1) => new()
	{
		Id = id,
		TournamentYearId = 1,
		AgeGroup = AgeGroup.U12,
		Gender = Gender.Boys,
		Name = "12U Boys",
		Size = size,
	};

	private static Pool CreatePool(int id, string name, int divisionId = 1) => new()
	{
		Id = id,
		DivisionId = divisionId,
		Name = name,
	};

	private static List<Team> CreateTeams(Pool pool, int count, int startingTeamId = 1)
	{
		var teams = new List<Team>(count);
		for (var i = 0; i < count; i++)
		{
			teams.Add(new Team
			{
				Id = startingTeamId + i,
				PoolId = pool.Id,
				Pool = pool,
				Name = $"Region {startingTeamId + i}: Coach{i + 1}",
				TeamNumber = i + 1,
			});
		}

		pool.Teams = teams;
		return teams;
	}

	private static PoolPlayGame CreateGame(
		int id,
		int poolId,
		int homeTeamId,
		int awayTeamId,
		int homeGoals,
		int awayGoals,
		bool isForfeit = false,
		int homeRed = 0,
		int awayRed = 0,
		int homeYellow = 0,
		int awayYellow = 0,
		int divisionId = 1,
		int round = 1) => new()
	{
		Id = id,
		DivisionId = divisionId,
		PoolId = poolId,
		Round = round,
		HomeTeamId = homeTeamId,
		AwayTeamId = awayTeamId,
		HomeGoals = homeGoals,
		AwayGoals = awayGoals,
		IsForfeit = isForfeit,
		HomeRedCards = homeRed,
		AwayRedCards = awayRed,
		HomeYellowCards = homeYellow,
		AwayYellowCards = awayYellow,
	};

	private static List<PoolPlayGame> CreateRoundRobinShutouts(Pool pool, List<Team> teams, int divisionId)
	{
		var games = new List<PoolPlayGame>();
		var id = 1;
		for (var i = 0; i < teams.Count; i++)
		{
			for (var j = i + 1; j < teams.Count; j++)
			{
				// Lower team number always wins 1-0 so ranking is deterministic by TeamNumber.
				games.Add(CreateGame(
					id++,
					pool.Id,
					teams[i].Id,
					teams[j].Id,
					homeGoals: 1,
					awayGoals: 0,
					divisionId: divisionId,
					round: id));
			}
		}

		return games;
	}
}
