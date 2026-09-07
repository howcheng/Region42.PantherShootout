using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A team competing in a pool within a division for a given tournament year.
/// </summary>
public class Team : AuditableEntity
{
	public int PoolId { get; set; }

	public Pool? Pool { get; set; }

	public required string Name { get; set; }

	/// <summary>
	/// The team's 1-based position/number within its pool. Combined with the pool's letter
	/// designation (e.g. "A"), this forms the team's short designation (e.g. "A1").
	/// </summary>
	public int TeamNumber { get; set; }

	/// <summary>
	/// The short designation for this team, e.g. "A1" (pool name "A" + team number 1).
	/// Replaces the old spreadsheet convention of parsing this out of the team name via substring.
	/// </summary>
	public string GetDesignation() => $"{Pool?.Name}{TeamNumber}";
}
