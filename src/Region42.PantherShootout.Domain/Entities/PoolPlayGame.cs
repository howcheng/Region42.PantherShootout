using Region42.PantherShootout.Domain.Common;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A single pool-play (round-robin) game between two teams in a pool, for a given round.
/// </summary>
public class PoolPlayGame : AuditableEntity
{
	public int PoolId { get; set; }

	public Pool? Pool { get; set; }

	/// <summary>1-based round number within the pool.</summary>
	public int Round { get; set; }

	public int HomeTeamId { get; set; }

	public Team? HomeTeam { get; set; }

	public int AwayTeamId { get; set; }

	public Team? AwayTeam { get; set; }

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	/// <summary>
	/// When true, the game was decided by forfeit. Score must still be entered as 1-0, but the
	/// scoring goal does not count as a real goal for tiebreaker/shutout-bonus purposes.
	/// </summary>
	public bool IsForfeit { get; set; }

	public int HomeYellowCards { get; set; }

	public int HomeRedCards { get; set; }

	public int AwayYellowCards { get; set; }

	public int AwayRedCards { get; set; }

	/// <summary>
	/// Manual "kicks from the mark" (KFTM) live tiebreaker result for pool-play standings,
	/// independent of the PSO competition's own KFTM result. Null unless staff recorded one.
	/// </summary>
	public int? KftmWinningTeamId { get; set; }

	public ShootoutRound? ShootoutRound { get; set; }

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
