namespace Region42.PantherShootout.Application.DTOs;

/// <summary>
/// One team's calculated pool-play standing row (display stats + tiebreaker helper values).
/// </summary>
public class PoolPlayStandingDto
{
	public int Rank { get; set; }

	public int TeamId { get; set; }

	public string TeamName { get; set; } = string.Empty;

	/// <summary>Pool letter + team number, e.g. <c>A1</c>.</summary>
	public string TeamDesignation { get; set; } = string.Empty;

	public int RegionNumber { get; set; }

	public string RegionName { get; set; } = string.Empty;

	public string CoachName { get; set; } = string.Empty;

	public int PoolId { get; set; }

	public string PoolName { get; set; } = string.Empty;

	/// <summary>1-based team number within the pool (used as a stable sort fallback).</summary>
	public int TeamNumber { get; set; }

	public int GamesPlayed { get; set; }

	public int Wins { get; set; }

	public int Losses { get; set; }

	public int Draws { get; set; }

	public int Points { get; set; }

	public int GoalsFor { get; set; }

	public int GoalsAgainst { get; set; }

	public int GoalDifferential { get; set; }

	/// <summary>
	/// Tiebreaker-only goals against: per-game GA capped at 3, excluding forfeited games.
	/// </summary>
	public int CappedGoalsAgainst { get; set; }

	/// <summary>
	/// Tiebreaker-only goal differential: per-game GF and GA each capped at 3, excluding forfeits.
	/// </summary>
	public int CappedGoalDifferential { get; set; }

	public int YellowCards { get; set; }

	public int RedCards { get; set; }

	public bool HasForfeit { get; set; }

	public bool HasVolunteerPointDeduction { get; set; }
}

/// <summary>
/// Ranked standings for a single pool (or a combined/cross-pool ranking group).
/// </summary>
public class PoolPlayStandingsResult
{
	public int? PoolId { get; set; }

	public string PoolName { get; set; } = string.Empty;

	public List<PoolPlayStandingDto> Standings { get; set; } = new();

	public bool AnyForfeitInScope { get; set; }

	/// <summary>
	/// True when every expected round-robin game in this ranking scope has both scores entered.
	/// </summary>
	public bool IsFinal { get; set; }

	public bool RequiresKftmTiebreaker { get; set; }

	public List<int> TiedTeamIdsForKftm { get; set; } = new();
}

/// <summary>
/// Full division pool-play standings: per-pool tables plus optional 10-team combined
/// and 12-team pool-winner / runner-up sub-rankings.
/// </summary>
public class DivisionPoolPlayStandingsResult
{
	public int DivisionId { get; set; }

	public string DivisionName { get; set; } = string.Empty;

	public List<PoolPlayStandingsResult> Pools { get; set; } = new();

	public PoolPlayStandingsResult? CombinedDivisionStandings { get; set; }

	public PoolPlayStandingsResult? PoolWinnersStandings { get; set; }

	public PoolPlayStandingsResult? RunnersUpStandings { get; set; }

	public bool AnyForfeitInDivision { get; set; }

	public bool AllPoolsFinal { get; set; }
}
