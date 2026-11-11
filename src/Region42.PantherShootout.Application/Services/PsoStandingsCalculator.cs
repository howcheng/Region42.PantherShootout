using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Pure division-wide Penalty Shootout (PSO) standings calculator per <c>docs/BusinessRules.md</c>:
/// primary sort by total PK goals for; tiebreakers are PK goals against (ascending) then
/// <see cref="TiebreakerScope.PsoStandings"/> KFTM overrides. 5-team pools apply a 4th-round
/// cap so a team that already completed 3 matchups in rounds 1–3 does not also count round 4.
/// </summary>
public static class PsoStandingsCalculator
{
	public static PsoStandingsResult Calculate(
		Division division,
		IReadOnlyList<Team> teams,
		IReadOnlyList<ShootoutRound> shootoutRounds,
		IReadOnlyList<TiebreakerOverride>? kftmOverrides = null)
	{
		ArgumentNullException.ThrowIfNull(division);
		ArgumentNullException.ThrowIfNull(teams);
		ArgumentNullException.ThrowIfNull(shootoutRounds);

		var maxRounds = DivisionSizeRules.GetShootoutRoundCount(division.Size);
		var useFourthRoundCap = DivisionSizeRules.UsesPsoFourthRoundCap(division.Size);
		var psoOverrides = FilterPsoOverrides(kftmOverrides);

		var completedRounds = shootoutRounds.Where(r => r.IsComplete).ToList();
		var rows = teams
			.Select(team => CalculateTeamStanding(team, completedRounds, maxRounds, useFourthRoundCap))
			.ToList();

		var (sorted, requiresKftm, tiedTeamIds) = SortWithTiebreakers(rows, psoOverrides);

		for (var i = 0; i < sorted.Count; i++)
		{
			sorted[i].Rank = i + 1;
		}

		var isFinal = sorted.Count > 0
			&& sorted.All(s => s.RoundsCompleted >= DivisionSizeRules.RequiredPsoMatchupsPerTeam);

		return new PsoStandingsResult
		{
			DivisionId = division.Id,
			DivisionName = division.Name,
			Standings = sorted,
			IsFinal = isFinal,
			WinnerTeamId = isFinal && !requiresKftm ? sorted[0].TeamId : null,
			RequiresKftmTiebreaker = requiresKftm,
			TiedTeamIdsForKftm = tiedTeamIds,
		};
	}

	private static PsoStandingDto CalculateTeamStanding(
		Team team,
		IReadOnlyList<ShootoutRound> completedRounds,
		int maxRounds,
		bool useFourthRoundCap)
	{
		var dto = new PsoStandingDto
		{
			TeamId = team.Id,
			TeamName = team.Name,
			TeamDesignation = team.GetDesignation(),
			RoundScores = Enumerable.Repeat<int?>(null, maxRounds).ToList(),
		};

		var teamRounds = completedRounds
			.Where(r => r.HomeTeamId == team.Id || r.AwayTeamId == team.Id)
			.OrderBy(r => r.Round)
			.ToList();

		var countedRounds = SelectCountedRounds(teamRounds, useFourthRoundCap);

		foreach (var round in countedRounds)
		{
			if (round.Round < 1 || round.Round > maxRounds)
			{
				continue;
			}

			var isHome = round.HomeTeamId == team.Id;
			var goalsFor = isHome ? round.HomePkGoals!.Value : round.AwayPkGoals!.Value;
			var goalsAgainst = isHome ? round.AwayPkGoals!.Value : round.HomePkGoals!.Value;

			dto.RoundScores[round.Round - 1] = goalsFor;
			dto.TotalPkGoalsFor += goalsFor;
			dto.TotalPkGoalsAgainst += goalsAgainst;
			dto.RoundsCompleted++;
		}

		return dto;
	}

	/// <summary>
	/// For 5-team pools, omit round 4 when the team already has 3 counted results from rounds 1–3.
	/// Other division sizes count every completed round the team participated in.
	/// </summary>
	internal static List<ShootoutRound> SelectCountedRounds(
		IReadOnlyList<ShootoutRound> teamRoundsOrdered,
		bool useFourthRoundCap)
	{
		if (!useFourthRoundCap)
		{
			return teamRoundsOrdered.ToList();
		}

		var earlyRounds = teamRoundsOrdered.Where(r => r.Round <= 3).ToList();
		if (earlyRounds.Count >= DivisionSizeRules.RequiredPsoMatchupsPerTeam)
		{
			return earlyRounds
				.Take(DivisionSizeRules.RequiredPsoMatchupsPerTeam)
				.ToList();
		}

		var counted = earlyRounds.ToList();
		var round4 = teamRoundsOrdered.FirstOrDefault(r => r.Round == 4);
		if (round4 is not null)
		{
			counted.Add(round4);
		}

		return counted;
	}

	private static (List<PsoStandingDto> Sorted, bool RequiresKftm, List<int> TiedTeamIds) SortWithTiebreakers(
		List<PsoStandingDto> rows,
		IReadOnlyList<TiebreakerOverride> kftmOverrides)
	{
		var sorted = new List<PsoStandingDto>(rows.Count);
		var signatureByTeamId = new Dictionary<int, (int GoalsFor, int GoalsAgainst)>();

		foreach (var group in rows.GroupBy(r => r.TotalPkGoalsFor).OrderByDescending(g => g.Key))
		{
			var groupTeams = group.ToList();
			foreach (var team in groupTeams)
			{
				signatureByTeamId[team.TeamId] = (team.TotalPkGoalsFor, team.TotalPkGoalsAgainst);
			}

			if (groupTeams.Count == 1)
			{
				sorted.Add(groupTeams[0]);
				continue;
			}

			var tiedIds = groupTeams.Select(t => t.TeamId).ToList();
			var ordered = groupTeams
				.OrderBy(t => t.TotalPkGoalsAgainst)
				.ThenByDescending(t => CountKftmWinsInGroup(t.TeamId, tiedIds, kftmOverrides))
				.ThenBy(t => t.TeamDesignation, StringComparer.OrdinalIgnoreCase)
				.ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
				.ToList();

			sorted.AddRange(ordered);
		}

		var requiresKftm = false;
		var tiedTeamIds = new List<int>();

		var unresolved = sorted
			.Select((team, index) => (Team: team, Rank: index + 1, Signature: signatureByTeamId[team.TeamId]))
			.GroupBy(x => x.Signature)
			.Where(g => g.Count() > 1);

		foreach (var sigGroup in unresolved)
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

		return (sorted, requiresKftm, tiedTeamIds.Distinct().ToList());
	}

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

	private static IReadOnlyList<TiebreakerOverride> FilterPsoOverrides(
		IReadOnlyList<TiebreakerOverride>? kftmOverrides)
	{
		if (kftmOverrides is null || kftmOverrides.Count == 0)
		{
			return Array.Empty<TiebreakerOverride>();
		}

		return kftmOverrides
			.Where(o => o.Scope == TiebreakerScope.PsoStandings)
			.ToList();
	}
}
