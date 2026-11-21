using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Web.Models;

public enum BracketTopologyKind
{
	NoneChampionBannerOnly = 0,
	DirectFinals = 1,
	SemifinalsToFinal = 2,
}

public class PoolStandingsTableViewModel
{
	public string Title { get; set; } = string.Empty;

	public string? Subtitle { get; set; }

	public PoolPlayStandingsResult Result { get; set; } = new();

	public HashSet<int> AdvancingTeamIds { get; set; } = new();

	public int? ChampionTeamId { get; set; }

	public bool ShowPoolBadgeOnTeam { get; set; }

	public string? PrintPoolUrl { get; set; }
}

public class GameScheduleCardItemViewModel
{
	public int GameId { get; set; }

	public int Round { get; set; }

	public string PoolName { get; set; } = string.Empty;

	public DateTime? ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public string FieldShortLabel { get; set; } = string.Empty;

	public int HomeTeamId { get; set; }

	public string HomeCode { get; set; } = string.Empty;

	public string HomeShortName { get; set; } = string.Empty;

	public string HomeFullName { get; set; } = string.Empty;

	public string HomePrintName { get; set; } = string.Empty;

	public int AwayTeamId { get; set; }

	public string AwayCode { get; set; } = string.Empty;

	public string AwayShortName { get; set; } = string.Empty;

	public string AwayFullName { get; set; } = string.Empty;

	public string AwayPrintName { get; set; } = string.Empty;

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	public int HomeYellowCards { get; set; }

	public int HomeRedCards { get; set; }

	public int AwayYellowCards { get; set; }

	public int AwayRedCards { get; set; }

	public int? HomePkGoals { get; set; }

	public int? AwayPkGoals { get; set; }

	public bool HasPairedShootoutRound { get; set; }

	public bool HomePsoDoesNotCount { get; set; }

	public bool AwayPsoDoesNotCount { get; set; }

	public string? NonCountingPsoTeamCode =>
		HomePsoDoesNotCount ? HomeCode : (AwayPsoDoesNotCount ? AwayCode : null);

	public bool HasScore => HomeGoals.HasValue && AwayGoals.HasValue;

	public bool HasPsoScore => HomePkGoals.HasValue && AwayPkGoals.HasValue;

	public GameWinner Winner
	{
		get
		{
			if (!HasScore)
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
}

public class GameScheduleListViewModel
{
	public List<string> PoolNames { get; set; } = new();

	public List<GameScheduleCardItemViewModel> Games { get; set; } = new();
}

public class BracketMatchCardViewModel
{
	public int? GameId { get; set; }

	public BracketSlot Slot { get; set; }

	public string SlotLabel { get; set; } = string.Empty;

	public DateTime? ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public int? HomeTeamId { get; set; }

	public string HomeShortDisplay { get; set; } = string.Empty;

	public string HomeFullDisplay { get; set; } = string.Empty;

	public bool IsHomeSeeded { get; set; }

	public int? AwayTeamId { get; set; }

	public string AwayShortDisplay { get; set; } = string.Empty;

	public string AwayFullDisplay { get; set; } = string.Empty;

	public bool IsAwaySeeded { get; set; }

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	public int? WinnerTeamId { get; set; }

	public int? HomePkGoals { get; set; }

	public int? AwayPkGoals { get; set; }

	public bool HasPairedRound4Pso { get; set; }

	public bool HomePsoDoesNotCount { get; set; }

	public bool AwayPsoDoesNotCount { get; set; }

	public string? NonCountingPsoTeamDisplay =>
		HomePsoDoesNotCount ? HomeShortDisplay : (AwayPsoDoesNotCount ? AwayShortDisplay : null);

	public bool HasScore => HomeGoals.HasValue && AwayGoals.HasValue;

	public bool HasRound4PsoScore => HomePkGoals.HasValue && AwayPkGoals.HasValue;
}

public class ChampionshipBracketSectionViewModel
{
	public string DivisionName { get; set; } = string.Empty;

	public DivisionSize Size { get; set; }

	public BracketTopologyKind Topology { get; set; }

	public bool PoolPlayFinal { get; set; }

	public int? ChampionTeamId { get; set; }

	public string? ChampionTeamDisplay { get; set; }

	public List<BracketMatchCardViewModel> Matches { get; set; } = new();
}

public class DivisionPsoTableViewModel
{
	public int Year { get; set; }

	public string DivisionKey { get; set; } = string.Empty;

	public string DivisionName { get; set; } = string.Empty;

	public DivisionSize Size { get; set; }

	public int ScheduledRoundCount { get; set; }

	public bool HasFourthShootoutRound { get; set; }

	public bool UsesFourthRoundCap { get; set; }

	public PsoStandingsResult Result { get; set; } = new();

	public string? LeaderOrWinnerDisplay { get; set; }

	public bool ShowFullShootoutLink { get; set; }

	/// <summary>
	/// Set of <c>TeamId</c> values in Round 4 whose Round 4 shootout does not count because they already have 3 turns in Rounds 1–3.
	/// </summary>
	public HashSet<int> CappedFourthRoundTeamIds { get; set; } = new();

	/// <summary>
	/// Completed Round 4 PK scores that were excluded from PSO totals due to the 3-turn cap, keyed by <c>TeamId</c>.
	/// </summary>
	public Dictionary<int, int> ExcludedFourthRoundScoresByTeamId { get; set; } = new();
}

public class DivisionStandingsViewModel
{
	public TournamentYearDto SelectedYear { get; set; } = new();

	public List<TournamentYearDto> AvailableYears { get; set; } = new();

	public DivisionSummaryDto SelectedDivision { get; set; } = new();

	public List<DivisionSummaryDto> SiblingDivisions { get; set; } = new();

	public DivisionPoolPlayStandingsResult PoolStandings { get; set; } = new();

	public List<PoolStandingsTableViewModel> PoolTables { get; set; } = new();

	public PoolStandingsTableViewModel? CombinedTenTeamTable { get; set; }

	public PoolStandingsTableViewModel? TwelveTeamWinnersTable { get; set; }

	public PoolStandingsTableViewModel? TwelveTeamRunnersUpTable { get; set; }

	public GameScheduleListViewModel Schedule { get; set; } = new();

	public ChampionshipBracketSectionViewModel Bracket { get; set; } = new();

	public DivisionPsoTableViewModel Pso { get; set; } = new();

	public bool HasAnyUnresolvedPoolKftm =>
		PoolStandings.Pools.Any(p => p.RequiresKftmTiebreaker) ||
		(PoolStandings.CombinedDivisionStandings?.RequiresKftmTiebreaker ?? false) ||
		(PoolStandings.PoolWinnersStandings?.RequiresKftmTiebreaker ?? false) ||
		(PoolStandings.RunnersUpStandings?.RequiresKftmTiebreaker ?? false);

	public DateTime? CalculatedAtUtc { get; set; }
}

public class PoolPrintStandingRowViewModel
{
	public PoolPlayStandingDto Standing { get; set; } = new();

	public string PrintCoachAndRegion { get; set; } = string.Empty;

	public bool IsChampion { get; set; }

	public bool Advances { get; set; }

	public int PsoGoalsFor { get; set; }

	public int PsoGoalsAgainst { get; set; }

	public int PsoRoundsCompleted { get; set; }

	public bool IsPsoDivisionLeader { get; set; }
}

public class PoolPrintSheetViewModel
{
	public int? PoolId { get; set; }

	public string PoolName { get; set; } = string.Empty;

	public string HeaderTitle { get; set; } = string.Empty;

	public int TeamCount { get; set; }

	public int CompletedRounds { get; set; }

	public int TotalRounds { get; set; }

	public bool IsPoolFinal { get; set; }

	public bool RequiresKftmTiebreaker { get; set; }

	public bool AnyForfeitInScope { get; set; }

	public List<PoolPrintStandingRowViewModel> StandingsRows { get; set; } = new();

	public List<GameScheduleCardItemViewModel> Games { get; set; } = new();

	public bool IsHiddenByInitialFilter { get; set; }

	public bool IsLastVisibleSheet { get; set; }
}

public class DivisionPrintViewModel
{
	public TournamentYearDto SelectedYear { get; set; } = new();

	public DivisionSummaryDto SelectedDivision { get; set; } = new();

	public List<DivisionSummaryDto> SiblingDivisions { get; set; } = new();

	public string? SelectedPoolFilter { get; set; }

	public string? PsoLeaderDisplay { get; set; }

	public bool PsoIsFinal { get; set; }

	public bool HasFourthShootoutRound { get; set; }

	public string PublicDivisionUrl { get; set; } = string.Empty;

	public string PublicDivisionDisplayUrl { get; set; } = string.Empty;

	public string QrCodeDataUri { get; set; } = string.Empty;

	public DateTime? CalculatedAtUtc { get; set; }

	public List<PoolPrintSheetViewModel> PoolSheets { get; set; } = new();
}
