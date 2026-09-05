using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// The penalty-shootout (PSO) result for the same two teams/round as its linked
/// <see cref="PoolPlayGame"/>. A round is only "final"/counted once both scores are entered.
/// </summary>
public class ShootoutRound : AuditableEntity
{
	public int PoolPlayGameId { get; set; }

	public PoolPlayGame? PoolPlayGame { get; set; }

	public int? HomePkGoals { get; set; }

	public int? AwayPkGoals { get; set; }

	public bool IsComplete => HomePkGoals is not null && AwayPkGoals is not null;
}
