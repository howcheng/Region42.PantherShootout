using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.DTOs;

public class PoolPlayScoreUpdateDto
{
	public int GameId { get; set; }

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	public int HomeYellowCards { get; set; }

	public int HomeRedCards { get; set; }

	public int AwayYellowCards { get; set; }

	public int AwayRedCards { get; set; }

	/// <summary>
	/// When set, updates the game's scheduled wall-clock date/time. Null leaves the existing value unchanged.
	/// </summary>
	public DateTime? NewScheduledDateTime { get; set; }

	/// <summary>
	/// When non-null, updates the game's field/location (trimmed). Null leaves the existing value unchanged.
	/// </summary>
	public string? NewLocation { get; set; }
}

public class ChampionshipScoreUpdateDto
{
	public int GameId { get; set; }

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	/// <summary>
	/// When set, updates the game's scheduled wall-clock date/time. Null leaves the existing value unchanged.
	/// </summary>
	public DateTime? NewScheduledDateTime { get; set; }

	/// <summary>
	/// When non-null, updates the game's field/location (trimmed). Null leaves the existing value unchanged.
	/// </summary>
	public string? NewLocation { get; set; }
}

public class PsoScoreUpdateDto
{
	public int ShootoutRoundId { get; set; }

	public int? HomePkGoals { get; set; }

	public int? AwayPkGoals { get; set; }
}

public class KftmOverrideUpsertDto
{
	public int DivisionId { get; set; }

	public TiebreakerScope Scope { get; set; }

	public int TeamAId { get; set; }

	public int TeamBId { get; set; }

	public int WinningTeamId { get; set; }
}

public class DivisionScoreBatchUpdateDto
{
	public int DivisionId { get; set; }

	public List<PoolPlayScoreUpdateDto> PoolPlayScores { get; set; } = new();

	public List<ChampionshipScoreUpdateDto> ChampionshipScores { get; set; } = new();

	public List<PsoScoreUpdateDto> PsoScores { get; set; } = new();
}
