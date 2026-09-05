using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A pool (group) of teams within a division that plays round-robin against each other.
/// </summary>
public class Pool : AuditableEntity
{
	public int DivisionId { get; set; }

	public Division? Division { get; set; }

	/// <summary>Letter/number identifying the pool within the division, e.g. "A".</summary>
	public required string Name { get; set; }

	public ICollection<Team> Teams { get; set; } = new List<Team>();
}
