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
}
