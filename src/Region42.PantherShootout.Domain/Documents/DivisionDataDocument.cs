namespace Region42.PantherShootout.Domain.Documents;

/// <summary>
/// Per-division game schedules, scores, PSO shootout rounds, manual KFTM tiebreaker overrides,
/// and pre-calculated standings snapshots, stored at <c>{year}/{slug}/{division-key}.json</c>.
/// </summary>
public class DivisionDataDocument
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public int Version { get; set; } = 1;

	public string DivisionKey { get; set; } = string.Empty;

	public int DivisionId { get; set; }

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;

	public List<PoolPlayGameDocument> PoolPlayGames { get; set; } = new();

	public List<ChampionshipGameDocument> ChampionshipGames { get; set; } = new();

	public List<ShootoutRoundDocument> ShootoutRounds { get; set; } = new();

	public List<TiebreakerOverrideDocument> TiebreakerOverrides { get; set; } = new();

	public DivisionStandingsSnapshotDocument? Standings { get; set; }
}

public class PoolPlayGameDocument
{
	public int Id { get; set; }

	public int PoolId { get; set; }

	public int Round { get; set; }

	public DateTime? ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public int HomeTeamId { get; set; }

	public int AwayTeamId { get; set; }

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	public int HomeYellowCards { get; set; }

	public int HomeRedCards { get; set; }

	public int AwayYellowCards { get; set; }

	public int AwayRedCards { get; set; }

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;
}

public class ChampionshipGameDocument
{
	public int Id { get; set; }

	public string Slot { get; set; } = string.Empty;

	public DateTime? ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public string HomeSourceDescription { get; set; } = string.Empty;

	public string AwaySourceDescription { get; set; } = string.Empty;

	public int? HomeTeamId { get; set; }

	public int? AwayTeamId { get; set; }

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;
}

public class ShootoutRoundDocument
{
	public int Id { get; set; }

	public int Round { get; set; }

	public int? PoolPlayGameId { get; set; }

	public int? ChampionshipGameId { get; set; }

	public int? HomeTeamId { get; set; }

	public int? AwayTeamId { get; set; }

	public int? HomePkGoals { get; set; }

	public int? AwayPkGoals { get; set; }

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;
}

public class TiebreakerOverrideDocument
{
	public int Id { get; set; }

	public string Scope { get; set; } = string.Empty;

	public int TeamAId { get; set; }

	public int TeamBId { get; set; }

	public int WinningTeamId { get; set; }

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;
}

public class DivisionStandingsSnapshotDocument
{
	public DateTime CalculatedAt { get; set; }

	public int? ChampionTeamId { get; set; }

	public List<PoolStandingsSnapshotDocument> Pools { get; set; } = new();

	/// <summary>
	/// Populated for 10-team divisions (ranked combined across both 5-team pools).
	/// </summary>
	public PoolStandingsSnapshotDocument? CombinedDivisionStandings { get; set; }

	public PsoStandingsSnapshotDocument PsoStandings { get; set; } = new();
}

public class PoolStandingsSnapshotDocument
{
	public int? PoolId { get; set; }

	public string PoolName { get; set; } = string.Empty;

	public bool IsFinal { get; set; }

	public bool AnyForfeitInScope { get; set; }

	public bool RequiresKftmTiebreaker { get; set; }

	public List<int> TiedTeamIdsForKftm { get; set; } = new();

	public List<TeamPoolStandingDocument> Teams { get; set; } = new();
}

public class TeamPoolStandingDocument
{
	public int Rank { get; set; }

	public int TeamId { get; set; }

	public int GamesPlayed { get; set; }

	public int Wins { get; set; }

	public int Losses { get; set; }

	public int Draws { get; set; }

	public int Points { get; set; }

	public int GoalsFor { get; set; }

	public int GoalsAgainst { get; set; }

	public int GoalDifferential { get; set; }

	public int CappedGoalsAgainst { get; set; }

	public int CappedGoalDifferential { get; set; }

	public int YellowCards { get; set; }

	public int RedCards { get; set; }

	public bool HasForfeit { get; set; }
}

public class PsoStandingsSnapshotDocument
{
	public bool IsFinal { get; set; }

	public int? WinnerTeamId { get; set; }

	public bool RequiresKftmTiebreaker { get; set; }

	public List<int> TiedTeamIdsForKftm { get; set; } = new();

	public List<TeamPsoStandingDocument> Teams { get; set; } = new();
}

public class TeamPsoStandingDocument
{
	public int Rank { get; set; }

	public int TeamId { get; set; }

	public List<int?> RoundScores { get; set; } = new();

	public int TotalPkGoalsFor { get; set; }

	public int TotalPkGoalsAgainst { get; set; }

	public int RoundsCompleted { get; set; }
}
