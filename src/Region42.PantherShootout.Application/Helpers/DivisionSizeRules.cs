using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.Helpers;

/// <summary>
/// Division-size configuration matrix helpers (teams per pool, expected game counts, bracket flags)
/// derived from <c>docs/BusinessRules.md</c>.
/// </summary>
public static class DivisionSizeRules
{
	public static int GetPoolCount(DivisionSize size) => size switch
	{
		DivisionSize.Four => 1,
		DivisionSize.Five => 1,
		DivisionSize.Six => 2,
		DivisionSize.Eight => 2,
		DivisionSize.Ten => 2,
		DivisionSize.Twelve => 3,
		_ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported division size."),
	};

	public static int GetTeamsPerPool(DivisionSize size) => size switch
	{
		DivisionSize.Four => 4,
		DivisionSize.Five => 5,
		DivisionSize.Six => 3,
		DivisionSize.Eight => 4,
		DivisionSize.Ten => 5,
		DivisionSize.Twelve => 4,
		_ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported division size."),
	};

	/// <summary>
	/// Games each team plays in pool-play round robin (<c>teamsPerPool - 1</c>).
	/// </summary>
	public static int GetExpectedGamesPerTeam(DivisionSize size) => GetTeamsPerPool(size) - 1;

	/// <summary>
	/// Total round-robin games in a single pool: <c>n * (n - 1) / 2</c>.
	/// </summary>
	public static int GetExpectedGamesPerPool(DivisionSize size)
	{
		var teamsPerPool = GetTeamsPerPool(size);
		return teamsPerPool * (teamsPerPool - 1) / 2;
	}

	public static bool HasChampionshipBracket(DivisionSize size) =>
		size is not (DivisionSize.Five or DivisionSize.Ten);

	public static bool UsesCombinedDivisionStandings(DivisionSize size) => size == DivisionSize.Ten;

	public static bool UsesPoolWinnerRunnerUpRanking(DivisionSize size) => size == DivisionSize.Twelve;

	/// <summary>
	/// For 10-team divisions, forfeit nullification of wins/GD tiebreakers is evaluated
	/// across the whole division rather than a single pool.
	/// </summary>
	public static bool UsesDivisionWideForfeitScope(DivisionSize size) => size == DivisionSize.Ten;
}
