using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Documents;

namespace Region42.PantherShootout.Application.Helpers;

/// <summary>
/// Maps calculated PSO standings DTOs into <see cref="PsoStandingsSnapshotDocument"/>.
/// </summary>
public static class PsoStandingsSnapshotMapper
{
	public static PsoStandingsSnapshotDocument ToSnapshot(PsoStandingsResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		return new PsoStandingsSnapshotDocument
		{
			IsFinal = result.IsFinal,
			WinnerTeamId = result.WinnerTeamId,
			RequiresKftmTiebreaker = result.RequiresKftmTiebreaker,
			TiedTeamIdsForKftm = result.TiedTeamIdsForKftm.ToList(),
			Teams = result.Standings.Select(ToTeamSnapshot).ToList(),
		};
	}

	private static TeamPsoStandingDocument ToTeamSnapshot(PsoStandingDto standing) => new()
	{
		Rank = standing.Rank,
		TeamId = standing.TeamId,
		RoundScores = standing.RoundScores.ToList(),
		TotalPkGoalsFor = standing.TotalPkGoalsFor,
		TotalPkGoalsAgainst = standing.TotalPkGoalsAgainst,
		RoundsCompleted = standing.RoundsCompleted,
	};
}
