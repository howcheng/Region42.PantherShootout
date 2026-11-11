namespace Region42.PantherShootout.Domain.Enums;

/// <summary>
/// The kind of competition a manual KFTM tiebreaker override applies to. Pool-play standings
/// and the PSO competition each have their own independent KFTM result for the same pair of teams.
/// </summary>
public enum TiebreakerScope
{
	PoolPlayStandings = 0,
	PsoStandings = 1,
}
