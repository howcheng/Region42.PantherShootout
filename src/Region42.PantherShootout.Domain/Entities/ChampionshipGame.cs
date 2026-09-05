using Region42.PantherShootout.Domain.Common;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A championship-bracket game. The two participating teams may be fixed (once known) or
/// resolved dynamically from pool standings rank (e.g. "Pool A #1"); <see cref="HomeSourceDescription"/>
/// and <see cref="AwaySourceDescription"/> capture the human-readable source ("Pool A 1st place")
/// until the actual team is determined and stored in <see cref="HomeTeamId"/>/<see cref="AwayTeamId"/>.
/// </summary>
public class ChampionshipGame : AuditableEntity
{
	public int DivisionId { get; set; }

	public Division? Division { get; set; }

	public BracketSlot Slot { get; set; }

	public required string HomeSourceDescription { get; set; }

	public required string AwaySourceDescription { get; set; }

	public int? HomeTeamId { get; set; }

	public Team? HomeTeam { get; set; }

	public int? AwayTeamId { get; set; }

	public Team? AwayTeam { get; set; }

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	public GameWinner GetWinner()
	{
		if (HomeGoals is null || AwayGoals is null)
		{
			return GameWinner.Undetermined;
		}

		if (HomeGoals > AwayGoals)
		{
			return GameWinner.Home;
		}

		if (AwayGoals > HomeGoals)
		{
			return GameWinner.Away;
		}

		return GameWinner.Draw;
	}
}
