using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.DTOs;

/// <summary>
/// Static definition of one championship bracket slot for a given division size
/// (source descriptions only; team IDs are resolved later from standings/progression).
/// </summary>
public class ChampionshipBracketSlotDefinition
{
	public BracketSlot Slot { get; init; }

	public required string HomeSourceDescription { get; init; }

	public required string AwaySourceDescription { get; init; }

	/// <summary>
	/// True when both sides are seeded from pool-play standings once pool play is final.
	/// False for slots filled by progression from other bracket games (e.g. SF winners).
	/// </summary>
	public bool IsOpeningRound { get; init; }
}

/// <summary>
/// One championship game with resolved teams/scores for display or persistence updates.
/// </summary>
public class ChampionshipGameDto
{
	public int? GameId { get; set; }

	public BracketSlot Slot { get; set; }

	public string HomeSourceDescription { get; set; } = string.Empty;

	public string AwaySourceDescription { get; set; } = string.Empty;

	public int? HomeTeamId { get; set; }

	public int? AwayTeamId { get; set; }

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	public int? WinnerTeamId { get; set; }

	public int? LoserTeamId { get; set; }

	public bool IsComplete => HomeGoals is not null && AwayGoals is not null;

	public bool HasDeterminedWinner => WinnerTeamId is not null;
}

/// <summary>
/// Team pairing to copy onto a 6-team PSO Round 4 <c>ShootoutRound</c> linked to an opening bracket game.
/// </summary>
public class PsoRound4AssignmentDto
{
	public BracketSlot ChampionshipSlot { get; set; }

	public int? ChampionshipGameId { get; set; }

	public int? HomeTeamId { get; set; }

	public int? AwayTeamId { get; set; }
}

/// <summary>
/// Full championship bracket state for a division after seeding and progression.
/// </summary>
public class ChampionshipBracketResult
{
	public int DivisionId { get; set; }

	public string DivisionName { get; set; } = string.Empty;

	public DivisionSize Size { get; set; }

	public bool HasBracket { get; set; }

	public bool PoolPlayFinal { get; set; }

	public int? ChampionTeamId { get; set; }

	public List<ChampionshipGameDto> Games { get; set; } = new();

	/// <summary>Populated for 6-team divisions when opening bracket teams are known.</summary>
	public List<PsoRound4AssignmentDto> PsoRound4Assignments { get; set; } = new();
}
