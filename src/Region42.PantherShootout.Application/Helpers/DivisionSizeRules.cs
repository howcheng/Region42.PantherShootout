using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.Helpers;

/// <summary>
/// Division-size configuration matrix helpers (teams per pool, expected game counts, bracket flags)
/// derived from <c>docs/BusinessRules.md</c>.
/// </summary>
public static class DivisionSizeRules
{
	/// <summary>
	/// Every team needs this many completed PSO matchups for division PSO standings to be final.
	/// </summary>
	public const int RequiredPsoMatchupsPerTeam = 3;

	public static bool TryFromTeamCount(int teamCount, out DivisionSize size)
	{
		switch (teamCount)
		{
			case 4:
				size = DivisionSize.Four;
				return true;
			case 5:
				size = DivisionSize.Five;
				return true;
			case 6:
				size = DivisionSize.Six;
				return true;
			case 8:
				size = DivisionSize.Eight;
				return true;
			case 10:
				size = DivisionSize.Ten;
				return true;
			case 12:
				size = DivisionSize.Twelve;
				return true;
			default:
				size = default;
				return false;
		}
	}

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

	public static int GetPoolPlayRoundCount(DivisionSize size) => size switch
	{
		DivisionSize.Four or DivisionSize.Six or DivisionSize.Eight or DivisionSize.Twelve => 3,
		DivisionSize.Five or DivisionSize.Ten => 5,
		_ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported division size."),
	};

	public static int GetGamesPerPoolRound(DivisionSize size) => size switch
	{
		DivisionSize.Six => 1,
		DivisionSize.Four or DivisionSize.Five or DivisionSize.Eight or DivisionSize.Ten or DivisionSize.Twelve => 2,
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

	/// <summary>
	/// Maximum PSO round numbers scheduled for the division (3 for even-sized pools;
	/// 4 when pools have an odd team count / bye rotation, including 6-team Round 4).
	/// </summary>
	public static int GetShootoutRoundCount(DivisionSize size) => size switch
	{
		DivisionSize.Four or DivisionSize.Eight or DivisionSize.Twelve => 3,
		DivisionSize.Five or DivisionSize.Six or DivisionSize.Ten => 4,
		_ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported division size."),
	};

	/// <summary>
	/// 5-team pools (5- and 10-team divisions) may schedule a 4th PSO round that only counts
	/// when the team has fewer than <see cref="RequiredPsoMatchupsPerTeam"/> results from rounds 1–3.
	/// </summary>
	public static bool UsesPsoFourthRoundCap(DivisionSize size) =>
		size is DivisionSize.Five or DivisionSize.Ten;
}
