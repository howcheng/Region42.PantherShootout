using Region42.PantherShootout.Domain.Common;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A manually-entered "kicks from the mark" (KFTM) live tiebreaker result between two teams,
/// used only when teams are still tied after all formula-based tiebreakers.
/// </summary>
public class TiebreakerOverride : AuditableEntity
{
	public int DivisionId { get; set; }

	public Division? Division { get; set; }

	public TiebreakerScope Scope { get; set; }

	public int TeamAId { get; set; }

	public Team? TeamA { get; set; }

	public int TeamBId { get; set; }

	public Team? TeamB { get; set; }

	/// <summary>The team credited with the KFTM win between TeamA and TeamB.</summary>
	public int WinningTeamId { get; set; }

	public Team? WinningTeam { get; set; }
}
