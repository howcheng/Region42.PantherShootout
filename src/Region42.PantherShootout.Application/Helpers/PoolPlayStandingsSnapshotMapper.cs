using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Documents;

namespace Region42.PantherShootout.Application.Helpers;

/// <summary>
/// Maps calculated pool-play standings DTOs into JSON snapshot documents stored on
/// <see cref="DivisionDataDocument"/>.
/// </summary>
public static class PoolPlayStandingsSnapshotMapper
{
	public static DivisionStandingsSnapshotDocument ToSnapshot(
		DivisionPoolPlayStandingsResult result,
		DateTime? calculatedAtUtc = null)
	{
		ArgumentNullException.ThrowIfNull(result);

		return new DivisionStandingsSnapshotDocument
		{
			CalculatedAt = calculatedAtUtc ?? DateTime.UtcNow,
			ChampionTeamId = ResolveChampionTeamId(result),
			Pools = result.Pools.Select(ToPoolSnapshot).ToList(),
			CombinedDivisionStandings = result.CombinedDivisionStandings is null
				? null
				: ToPoolSnapshot(result.CombinedDivisionStandings),
			PoolWinnersStandings = result.PoolWinnersStandings is null
				? null
				: ToPoolSnapshot(result.PoolWinnersStandings),
			RunnersUpStandings = result.RunnersUpStandings is null
				? null
				: ToPoolSnapshot(result.RunnersUpStandings),
			PsoStandings = new PsoStandingsSnapshotDocument(),
		};
	}

	public static PoolStandingsSnapshotDocument ToPoolSnapshot(PoolPlayStandingsResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		return new PoolStandingsSnapshotDocument
		{
			PoolId = result.PoolId,
			PoolName = result.PoolName,
			IsFinal = result.IsFinal,
			AnyForfeitInScope = result.AnyForfeitInScope,
			RequiresKftmTiebreaker = result.RequiresKftmTiebreaker,
			TiedTeamIdsForKftm = result.TiedTeamIdsForKftm.ToList(),
			Teams = result.Standings.Select(ToTeamSnapshot).ToList(),
		};
	}

	private static TeamPoolStandingDocument ToTeamSnapshot(PoolPlayStandingDto standing) => new()
	{
		Rank = standing.Rank,
		TeamId = standing.TeamId,
		GamesPlayed = standing.GamesPlayed,
		Wins = standing.Wins,
		Losses = standing.Losses,
		Draws = standing.Draws,
		Points = standing.Points,
		GoalsFor = standing.GoalsFor,
		GoalsAgainst = standing.GoalsAgainst,
		GoalDifferential = standing.GoalDifferential,
		CappedGoalsAgainst = standing.CappedGoalsAgainst,
		CappedGoalDifferential = standing.CappedGoalDifferential,
		YellowCards = standing.YellowCards,
		RedCards = standing.RedCards,
		HasForfeit = standing.HasForfeit,
		HasVolunteerPointDeduction = standing.HasVolunteerPointDeduction,
	};

	private static int? ResolveChampionTeamId(DivisionPoolPlayStandingsResult result)
	{
		if (!result.AllPoolsFinal)
		{
			return null;
		}

		if (result.CombinedDivisionStandings?.Standings is { Count: > 0 } combined)
		{
			return combined[0].TeamId;
		}

		if (result.Pools is { Count: 1 } && result.Pools[0].Standings is { Count: > 0 } singlePool)
		{
			return singlePool[0].TeamId;
		}

		return null;
	}
}
