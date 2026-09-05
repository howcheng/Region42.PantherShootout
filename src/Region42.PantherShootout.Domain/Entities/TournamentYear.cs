using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A single tournament year (season). All divisions/teams/games belong to a year so that
/// future years can be added without schema changes.
/// </summary>
public class TournamentYear : AuditableEntity
{
	/// <summary>E.g. 2026.</summary>
	public int Year { get; set; }

	public bool IsActive { get; set; }

	public ICollection<Division> Divisions { get; set; } = new List<Division>();
}
