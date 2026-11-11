using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A single tournament year (season). All divisions/teams/games belong to a year so that
/// future years can be added cleanly in storage.
/// </summary>
public class TournamentYear : AuditableEntity
{
	/// <summary>E.g. 2026.</summary>
	public int Year { get; set; }

	/// <summary>Display name for the tournament year, e.g. "2026 Panther Shootout".</summary>
	public string Name { get; set; } = string.Empty;

	public bool IsActive { get; set; }

	public string? CustomMessage { get; set; }

	public ICollection<Division> Divisions { get; set; } = new List<Division>();
}
