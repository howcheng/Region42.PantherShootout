using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A penalty-shootout (PSO) matchup for a given round in a division. Normally linked to a
/// <see cref="PoolPlayGame"/>, or (for 6-team divisions in PSO Round 4) linked to one of the
/// opening <see cref="ChampionshipGame"/> slots (<c>Consolation</c>, <c>Semifinal1</c>, <c>Semifinal2</c>).
/// A round is only "final"/counted once both teams are known and both PK scores are entered.
/// </summary>
public class ShootoutRound : AuditableEntity
{
	public int DivisionId { get; set; }

	public Division? Division { get; set; }

	/// <summary>1-based PSO round number (1..3, or 1..4 for divisions with odd-sized pools).</summary>
	public int Round { get; set; }

	public int? PoolPlayGameId { get; set; }

	public PoolPlayGame? PoolPlayGame { get; set; }

	public int? ChampionshipGameId { get; set; }

	public ChampionshipGame? ChampionshipGame { get; set; }

	public int? HomeTeamId { get; set; }

	public Team? HomeTeam { get; set; }

	public int? AwayTeamId { get; set; }

	public Team? AwayTeam { get; set; }

	public int? HomePkGoals { get; set; }

	public int? AwayPkGoals { get; set; }

	public bool IsComplete =>
		HomeTeamId is not null &&
		AwayTeamId is not null &&
		HomePkGoals is not null &&
		AwayPkGoals is not null;
}
