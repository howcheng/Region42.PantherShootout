namespace Region42.PantherShootout.Domain.Enums;

/// <summary>
/// The computed result of a game based on a plain comparison of home vs. away goals.
/// </summary>
public enum GameWinner
{
	/// <summary>Neither score has been entered yet.</summary>
	Undetermined = 0,
	Home = 1,
	Away = 2,
	Draw = 3,
}
