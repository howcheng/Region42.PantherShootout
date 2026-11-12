using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Pure in-memory calculator for Panther Shootout pool-play standings using the AYSO 10-point
/// system and the 6-step tiebreaker chain from <c>docs/BusinessRules.md</c>:
/// <list type="bullet">
/// <item>Win = 6, Draw = 3, Loss = 0</item>
/// <item>+1 per goal scored, capped at 3 per game</item>
/// <item>+1 shutout bonus when GA == 0 (not awarded on forfeits)</item>
/// <item>Forfeit winner scored as 1-0 = 7 points (no shutout)</item>
/// <item>−1 per yellow card, −2 per red card</item>
/// <item>Tiebreakers: 2-way H2H → Wins (nullified if any forfeit in scope) → RC then YC →
/// non-forfeit capped GA → non-forfeit capped GD (nullified if any forfeit in scope) → KFTM</item>
/// </list>
/// </summary>
public static class PoolPlayStandingsCalculator
{
	public const int WinPoints = 6;
	public const int DrawPoints = 3;
	public const int LossPoints = 0;
	public const int MaxGoalBonusPerGame = 3;
	public const int ShutoutBonusPoints = 1;
	public const int ForfeitWinPoints = 7;
	public const int RedCardDeduction = 2;
	public const int YellowCardDeduction = 1;

	/// <summary>
	/// Calculates standings for a single pool.
	/// </summary>
	/// <param name="teams">Teams belonging to the pool.</param>
	/// <param name="poolGames">Pool-play games for the pool (completed and incomplete).</param>
	/// <param name="kftmOverrides">Optional pool-play KFTM overrides (Scope = PoolPlayStandings).</param>
	/// <param name="expectedGamesInPool">
	/// Expected completed round-robin game count for <see cref="PoolPlayStandingsResult.IsFinal"/>.
	/// When null, derived as <c>n * (n - 1) / 2</c> from <paramref name="teams"/>.
	/// </param>
	/// <param name="anyForfeitInScope">
	/// When set, overrides the pool-local forfeit flag for wins/GD nullification
	/// (used by 10-team divisions where forfeit scope is division-wide).
	/// </param>
	public static PoolPlayStandingsResult CalculatePool(
		IReadOnlyList<Team> teams,
		IReadOnlyList<PoolPlayGame> poolGames,
		IReadOnlyList<TiebreakerOverride>? kftmOverrides = null,
		int? expectedGamesInPool = null,
		bool? anyForfeitInScope = null)
	{
		ArgumentNullException.ThrowIfNull(teams);
		ArgumentNullException.ThrowIfNull(poolGames);

		var completedGames = poolGames.Where(IsCompletedPoolGame).ToList();
		var forfeitInScope = anyForfeitInScope ?? completedGames.Any(g => g.IsForfeit);
		var expectedCount = expectedGamesInPool ?? (teams.Count * (teams.Count - 1) / 2);
		var isFinal = expectedCount > 0 && completedGames.Count >= expectedCount;

		var poolId = teams.FirstOrDefault()?.PoolId
			?? poolGames.FirstOrDefault()?.PoolId;
		var poolName = teams.FirstOrDefault()?.Pool?.Name
			?? string.Empty;

		var rows = teams
			.Select(team => CalculateTeamStanding(team, completedGames))
			.ToList();

		var poolPlayOverrides = FilterPoolPlayOverrides(kftmOverrides);
		var (sortedRows, requiresKftm, tiedTeamIds) = SortWithTiebreakers(
			rows,
			completedGames,
			forfeitInScope,
			isFinal,
			poolPlayOverrides);

		for (var i = 0; i < sortedRows.Count; i++)
		{
			sortedRows[i].Rank = i + 1;
		}

		return new PoolPlayStandingsResult
		{
			PoolId = poolId,
			PoolName = poolName,
			Standings = sortedRows,
			AnyForfeitInScope = forfeitInScope,
			IsFinal = isFinal,
			RequiresKftmTiebreaker = requiresKftm,
			TiedTeamIdsForKftm = tiedTeamIds,
		};
	}

	/// <summary>
	/// Calculates per-pool standings for a division, plus 10-team combined ranking and
	/// 12-team pool-winner / runner-up sub-rankings when applicable.
	/// </summary>
	public static DivisionPoolPlayStandingsResult CalculateDivision(
		Division division,
		IReadOnlyList<Pool> pools,
		IReadOnlyList<Team> teams,
		IReadOnlyList<PoolPlayGame> games,
		IReadOnlyList<TiebreakerOverride>? kftmOverrides = null)
	{
		ArgumentNullException.ThrowIfNull(division);
		ArgumentNullException.ThrowIfNull(pools);
		ArgumentNullException.ThrowIfNull(teams);
		ArgumentNullException.ThrowIfNull(games);

		var divisionPools = pools
			.Where(p => p.DivisionId == division.Id)
			.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var divisionTeams = teams.Where(t => divisionPools.Any(p => p.Id == t.PoolId)).ToList();
		var divisionGames = games.Where(g => g.DivisionId == division.Id).ToList();
		var completedDivisionGames = divisionGames.Where(IsCompletedPoolGame).ToList();
		var anyForfeitInDivision = completedDivisionGames.Any(g => g.IsForfeit);
		var useDivisionWideForfeit = DivisionSizeRules.UsesDivisionWideForfeitScope(division.Size);
		var expectedGamesPerPool = DivisionSizeRules.GetExpectedGamesPerPool(division.Size);
		var poolPlayOverrides = FilterPoolPlayOverrides(kftmOverrides);

		var poolResults = new List<PoolPlayStandingsResult>(divisionPools.Count);
		foreach (var pool in divisionPools)
		{
			var poolTeams = divisionTeams
				.Where(t => t.PoolId == pool.Id)
				.OrderBy(t => t.TeamNumber)
				.ToList();
			var poolGames = divisionGames.Where(g => g.PoolId == pool.Id).ToList();

			// Ensure pool navigation is available for designations when callers pass detached teams.
			foreach (var team in poolTeams.Where(t => t.Pool is null))
			{
				team.Pool = pool;
			}

			var poolResult = CalculatePool(
				poolTeams,
				poolGames,
				poolPlayOverrides,
				expectedGamesPerPool,
				useDivisionWideForfeit ? anyForfeitInDivision : null);

			if (string.IsNullOrEmpty(poolResult.PoolName))
			{
				poolResult.PoolName = pool.Name;
			}

			poolResult.PoolId ??= pool.Id;
			poolResults.Add(poolResult);
		}

		var result = new DivisionPoolPlayStandingsResult
		{
			DivisionId = division.Id,
			DivisionName = division.Name,
			Pools = poolResults,
			AnyForfeitInDivision = anyForfeitInDivision,
			AllPoolsFinal = poolResults.Count > 0 && poolResults.All(p => p.IsFinal),
		};

		if (DivisionSizeRules.UsesCombinedDivisionStandings(division.Size))
		{
			result.CombinedDivisionStandings = CalculateCombinedDivisionStandings(
				divisionTeams,
				divisionPools,
				divisionGames,
				poolPlayOverrides,
				anyForfeitInDivision,
				expectedGamesPerPool * DivisionSizeRules.GetPoolCount(division.Size));
		}

		if (DivisionSizeRules.UsesPoolWinnerRunnerUpRanking(division.Size) && poolResults.Count >= 3)
		{
			result.PoolWinnersStandings = RankPlacementGroup(
				poolResults,
				placementRank: 1,
				groupName: "Pool Winners",
				poolPlayOverrides,
				anyForfeitInDivision);

			result.RunnersUpStandings = RankPlacementGroup(
				poolResults,
				placementRank: 2,
				groupName: "Runners-Up",
				poolPlayOverrides,
				anyForfeitInDivision);
		}

		return result;
	}

	private static PoolPlayStandingsResult CalculateCombinedDivisionStandings(
		IReadOnlyList<Team> teams,
		IReadOnlyList<Pool> pools,
		IReadOnlyList<PoolPlayGame> games,
		IReadOnlyList<TiebreakerOverride> kftmOverrides,
		bool anyForfeitInDivision,
		int expectedGamesInDivision)
	{
		foreach (var team in teams.Where(t => t.Pool is null))
		{
			team.Pool = pools.FirstOrDefault(p => p.Id == team.PoolId);
		}

		var completedGames = games.Where(IsCompletedPoolGame).ToList();
		var isFinal = expectedGamesInDivision > 0 && completedGames.Count >= expectedGamesInDivision;

		var rows = teams
			.Select(team => CalculateTeamStanding(team, completedGames))
			.ToList();

		var (sortedRows, requiresKftm, tiedTeamIds) = SortWithTiebreakers(
			rows,
			completedGames,
			anyForfeitInDivision,
			isFinal,
			kftmOverrides);

		for (var i = 0; i < sortedRows.Count; i++)
		{
			sortedRows[i].Rank = i + 1;
		}

		return new PoolPlayStandingsResult
		{
			PoolId = null,
			PoolName = "Division",
			Standings = sortedRows,
			AnyForfeitInScope = anyForfeitInDivision,
			IsFinal = isFinal,
			RequiresKftmTiebreaker = requiresKftm,
			TiedTeamIdsForKftm = tiedTeamIds,
		};
	}

	private static PoolPlayStandingsResult RankPlacementGroup(
		IReadOnlyList<PoolPlayStandingsResult> poolResults,
		int placementRank,
		string groupName,
		IReadOnlyList<TiebreakerOverride> kftmOverrides,
		bool anyForfeitInDivision)
	{
		var members = poolResults
			.Select(pool => pool.Standings.FirstOrDefault(s => s.Rank == placementRank))
			.Where(s => s is not null)
			.Select(s => CloneStanding(s!))
			.ToList();

		var allPoolsFinal = poolResults.All(p => p.IsFinal);

		// Cross-pool teams never played each other, so H2H is skipped (group size is typically 3).
		// Re-sort using the same remaining tiebreakers against each team's existing pool stats.
		var (sortedRows, requiresKftm, tiedTeamIds) = SortWithTiebreakers(
			members,
			completedPoolGames: Array.Empty<PoolPlayGame>(),
			anyForfeitInScope: anyForfeitInDivision,
			allPoolGamesCompleted: allPoolsFinal,
			kftmOverrides: kftmOverrides);

		for (var i = 0; i < sortedRows.Count; i++)
		{
			sortedRows[i].Rank = i + 1;
		}

		return new PoolPlayStandingsResult
		{
			PoolId = null,
			PoolName = groupName,
			Standings = sortedRows,
			AnyForfeitInScope = anyForfeitInDivision,
			IsFinal = allPoolsFinal,
			RequiresKftmTiebreaker = requiresKftm,
			TiedTeamIdsForKftm = tiedTeamIds,
		};
	}

	private static PoolPlayStandingDto CalculateTeamStanding(
		Team team,
		List<PoolPlayGame> completedGames)
	{
		var dto = new PoolPlayStandingDto
		{
			TeamId = team.Id,
			TeamName = team.Name,
			TeamDesignation = team.GetDesignation(),
			RegionNumber = team.RegionNumber,
			RegionName = team.RegionName,
			CoachName = team.CoachName,
			PoolId = team.PoolId,
			PoolName = team.Pool?.Name ?? string.Empty,
			TeamNumber = team.TeamNumber,
		};

		var teamGames = completedGames
			.Where(g => g.HomeTeamId == team.Id || g.AwayTeamId == team.Id)
			.ToList();

		dto.GamesPlayed = teamGames.Count;

		foreach (var game in teamGames)
		{
			var isHome = game.HomeTeamId == team.Id;
			var goalsFor = isHome ? game.HomeGoals!.Value : game.AwayGoals!.Value;
			var goalsAgainst = isHome ? game.AwayGoals!.Value : game.HomeGoals!.Value;
			var redCards = isHome ? game.HomeRedCards : game.AwayRedCards;
			var yellowCards = isHome ? game.HomeYellowCards : game.AwayYellowCards;

			dto.GoalsFor += goalsFor;
			dto.GoalsAgainst += goalsAgainst;
			dto.RedCards += redCards;
			dto.YellowCards += yellowCards;

			if (game.IsForfeit)
			{
				dto.HasForfeit = true;

				if (goalsFor > goalsAgainst)
				{
					dto.Wins++;
					dto.Points += ForfeitWinPoints;
				}
				else
				{
					dto.Losses++;
				}

				// Forfeit games are excluded from capped GA / GD tiebreaker totals.
			}
			else
			{
				if (goalsFor > goalsAgainst)
				{
					dto.Wins++;
					dto.Points += WinPoints;
				}
				else if (goalsFor == goalsAgainst)
				{
					dto.Draws++;
					dto.Points += DrawPoints;
				}
				else
				{
					dto.Losses++;
					dto.Points += LossPoints;
				}

				dto.Points += Math.Min(goalsFor, MaxGoalBonusPerGame);

				if (goalsAgainst == 0)
				{
					dto.Points += ShutoutBonusPoints;
				}

				var cappedFor = Math.Min(goalsFor, MaxGoalBonusPerGame);
				var cappedAgainst = Math.Min(goalsAgainst, MaxGoalBonusPerGame);
				dto.CappedGoalsAgainst += cappedAgainst;
				dto.CappedGoalDifferential += cappedFor - cappedAgainst;
			}

			dto.Points -= (redCards * RedCardDeduction) + (yellowCards * YellowCardDeduction);
		}

		dto.GoalDifferential = dto.GoalsFor - dto.GoalsAgainst;
		return dto;
	}

	private static (List<PoolPlayStandingDto> Sorted, bool RequiresKftm, List<int> TiedTeamIds) SortWithTiebreakers(
		List<PoolPlayStandingDto> rows,
		IReadOnlyList<PoolPlayGame> completedPoolGames,
		bool anyForfeitInScope,
		bool allPoolGamesCompleted,
		IReadOnlyList<TiebreakerOverride> kftmOverrides)
	{
		var sorted = new List<PoolPlayStandingDto>(rows.Count);
		var statisticalSignatureByTeamId = new Dictionary<int, (int Points, int H2H, int EffectiveWins, int RedCards, int YellowCards, int CappedGA, int EffectiveCappedGD)>();

		foreach (var group in rows.GroupBy(r => r.Points).OrderByDescending(g => g.Key))
		{
			var groupTeams = group.ToList();
			if (groupTeams.Count == 1)
			{
				var single = groupTeams[0];
				statisticalSignatureByTeamId[single.TeamId] = BuildSignature(single, h2h: 0, anyForfeitInScope);
				sorted.Add(single);
				continue;
			}

			// Head-to-head is ONLY used in a 2-way tie.
			var h2hScoreByTeamId = groupTeams.ToDictionary(t => t.TeamId, _ => 0);
			if (groupTeams.Count == 2)
			{
				ApplyHeadToHead(groupTeams[0].TeamId, groupTeams[1].TeamId, completedPoolGames, h2hScoreByTeamId);
			}

			foreach (var team in groupTeams)
			{
				statisticalSignatureByTeamId[team.TeamId] = BuildSignature(
					team,
					h2hScoreByTeamId[team.TeamId],
					anyForfeitInScope);
			}

			var tiedIds = groupTeams.Select(t => t.TeamId).ToList();
			var orderedGroup = groupTeams
				.OrderByDescending(t => h2hScoreByTeamId[t.TeamId])
				.ThenByDescending(t => anyForfeitInScope ? 0 : t.Wins)
				.ThenBy(t => t.RedCards)
				.ThenBy(t => t.YellowCards)
				.ThenBy(t => t.CappedGoalsAgainst)
				.ThenByDescending(t => anyForfeitInScope ? 0 : t.CappedGoalDifferential)
				.ThenByDescending(t => CountKftmWinsInGroup(t.TeamId, tiedIds, kftmOverrides))
				.ThenBy(t => t.TeamNumber)
				.ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
				.ToList();

			sorted.AddRange(orderedGroup);
		}

		var requiresKftm = false;
		var tiedTeamIds = new List<int>();

		if (allPoolGamesCompleted && sorted.Count >= 2)
		{
			var unresolvedGroups = sorted
				.Select((team, index) => (Team: team, Rank: index + 1, Signature: statisticalSignatureByTeamId[team.TeamId]))
				.GroupBy(x => x.Signature)
				.Where(g => g.Count() > 1)
				.ToList();

			foreach (var sigGroup in unresolvedGroups)
			{
				var groupTeamIds = sigGroup.Select(x => x.Team.TeamId).ToList();
				var kftmResolved = groupTeamIds
					.Select(id => CountKftmWinsInGroup(id, groupTeamIds, kftmOverrides))
					.Distinct()
					.Count() > 1;

				if (!kftmResolved)
				{
					requiresKftm = true;
					tiedTeamIds.AddRange(groupTeamIds);
				}
			}
		}

		return (sorted, requiresKftm, tiedTeamIds.Distinct().ToList());
	}

	private static void ApplyHeadToHead(
		int teamAId,
		int teamBId,
		IReadOnlyList<PoolPlayGame> completedPoolGames,
		Dictionary<int, int> h2hScoreByTeamId)
	{
		var h2hGame = completedPoolGames.FirstOrDefault(g =>
			(g.HomeTeamId == teamAId && g.AwayTeamId == teamBId) ||
			(g.HomeTeamId == teamBId && g.AwayTeamId == teamAId));

		if (h2hGame?.HomeGoals is not { } homeScore || h2hGame.AwayGoals is not { } awayScore)
		{
			return;
		}

		if (homeScore > awayScore)
		{
			h2hScoreByTeamId[h2hGame.HomeTeamId] = 1;
		}
		else if (awayScore > homeScore)
		{
			h2hScoreByTeamId[h2hGame.AwayTeamId] = 1;
		}
	}

	private static (int Points, int H2H, int EffectiveWins, int RedCards, int YellowCards, int CappedGA, int EffectiveCappedGD) BuildSignature(
		PoolPlayStandingDto team,
		int h2h,
		bool anyForfeitInScope) =>
		(
			team.Points,
			h2h,
			anyForfeitInScope ? 0 : team.Wins,
			team.RedCards,
			team.YellowCards,
			team.CappedGoalsAgainst,
			anyForfeitInScope ? 0 : team.CappedGoalDifferential);

	private static int CountKftmWinsInGroup(
		int teamId,
		IReadOnlyList<int> tiedTeamIds,
		IReadOnlyList<TiebreakerOverride> kftmOverrides)
	{
		if (kftmOverrides.Count == 0)
		{
			return 0;
		}

		return kftmOverrides.Count(o =>
			o.WinningTeamId == teamId &&
			tiedTeamIds.Contains(o.TeamAId) &&
			tiedTeamIds.Contains(o.TeamBId) &&
			(o.TeamAId == teamId || o.TeamBId == teamId));
	}

	private static IReadOnlyList<TiebreakerOverride> FilterPoolPlayOverrides(
		IReadOnlyList<TiebreakerOverride>? kftmOverrides)
	{
		if (kftmOverrides is null || kftmOverrides.Count == 0)
		{
			return Array.Empty<TiebreakerOverride>();
		}

		return kftmOverrides
			.Where(o => o.Scope == TiebreakerScope.PoolPlayStandings)
			.ToList();
	}

	private static bool IsCompletedPoolGame(PoolPlayGame game) =>
		game.HomeGoals is not null && game.AwayGoals is not null;

	private static PoolPlayStandingDto CloneStanding(PoolPlayStandingDto source) => new()
	{
		Rank = source.Rank,
		TeamId = source.TeamId,
		TeamName = source.TeamName,
		TeamDesignation = source.TeamDesignation,
		RegionNumber = source.RegionNumber,
		RegionName = source.RegionName,
		CoachName = source.CoachName,
		PoolId = source.PoolId,
		PoolName = source.PoolName,
		TeamNumber = source.TeamNumber,
		GamesPlayed = source.GamesPlayed,
		Wins = source.Wins,
		Losses = source.Losses,
		Draws = source.Draws,
		Points = source.Points,
		GoalsFor = source.GoalsFor,
		GoalsAgainst = source.GoalsAgainst,
		GoalDifferential = source.GoalDifferential,
		CappedGoalsAgainst = source.CappedGoalsAgainst,
		CappedGoalDifferential = source.CappedGoalDifferential,
		YellowCards = source.YellowCards,
		RedCards = source.RedCards,
		HasForfeit = source.HasForfeit,
	};
}
