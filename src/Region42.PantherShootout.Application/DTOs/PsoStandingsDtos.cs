namespace Region42.PantherShootout.Application.DTOs;

/// <summary>
/// One team's division-wide Penalty Shootout (PSO) standing row.
/// </summary>
public class PsoStandingDto
{
	public int Rank { get; set; }

	public int TeamId { get; set; }

	public string TeamName { get; set; } = string.Empty;

	public string TeamDesignation { get; set; } = string.Empty;

	public int RegionNumber { get; set; }

	public string RegionName { get; set; } = string.Empty;

	public string CoachName { get; set; } = string.Empty;

	/// <summary>
	/// Per-round PK goals for this team (index 0 = round 1). Null means the team did not have a
	/// counted result in that round (bye, incomplete, or 5-team 4th-round cap exclusion).
	/// </summary>
	public List<int?> RoundScores { get; set; } = new();

	public int TotalPkGoalsFor { get; set; }

	public int TotalPkGoalsAgainst { get; set; }

	public int RoundsCompleted { get; set; }
}

/// <summary>
/// Division-wide PSO standings result (all pools ranked together).
/// </summary>
public class PsoStandingsResult
{
	public int DivisionId { get; set; }

	public string DivisionName { get; set; } = string.Empty;

	public List<PsoStandingDto> Standings { get; set; } = new();

	/// <summary>
	/// True when every team has completed <see cref="Helpers.DivisionSizeRules.RequiredPsoMatchupsPerTeam"/> counted matchups.
	/// </summary>
	public bool IsFinal { get; set; }

	public int? WinnerTeamId { get; set; }

	public bool RequiresKftmTiebreaker { get; set; }

	public List<int> TiedTeamIdsForKftm { get; set; } = new();
}
