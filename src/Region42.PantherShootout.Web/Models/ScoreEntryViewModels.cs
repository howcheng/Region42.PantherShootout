using Microsoft.AspNetCore.Mvc.Rendering;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Web.Models;

public class PoolPlayScoreEntryCardViewModel
{
	public int GameId { get; set; }

	public int Round { get; set; }

	public string PoolName { get; set; } = string.Empty;

	public DateTime? ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public string HomeCode { get; set; } = string.Empty;

	public string HomeFullName { get; set; } = string.Empty;

	public string AwayCode { get; set; } = string.Empty;

	public string AwayFullName { get; set; } = string.Empty;

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	public int HomeYellowCards { get; set; }

	public int HomeRedCards { get; set; }

	public int AwayYellowCards { get; set; }

	public int AwayRedCards { get; set; }

	public int? PairedShootoutRoundId { get; set; }

	public int? PairedShootoutRoundNumber { get; set; }

	public int? HomePkGoals { get; set; }

	public int? AwayPkGoals { get; set; }

	public bool HomePsoDoesNotCount { get; set; }

	public bool AwayPsoDoesNotCount { get; set; }

	public string? NonCountingPsoTeamCode =>
		HomePsoDoesNotCount ? HomeCode : (AwayPsoDoesNotCount ? AwayCode : null);

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;
}

public class ChampionshipScoreEntryCardViewModel
{
	public int GameId { get; set; }

	public BracketSlot Slot { get; set; }

	public string SlotLabel { get; set; } = string.Empty;

	public DateTime? ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public bool IsSeeded { get; set; }

	public string HomeDisplay { get; set; } = string.Empty;

	public string AwayDisplay { get; set; } = string.Empty;

	public int? HomeGoals { get; set; }

	public int? AwayGoals { get; set; }

	public bool IsForfeit { get; set; }

	public int? PairedRound4ShootoutId { get; set; }

	public int? HomePkGoals { get; set; }

	public int? AwayPkGoals { get; set; }

	public bool HomePsoDoesNotCount { get; set; }

	public bool AwayPsoDoesNotCount { get; set; }

	public string? NonCountingPsoTeamDisplay =>
		HomePsoDoesNotCount ? HomeDisplay : (AwayPsoDoesNotCount ? AwayDisplay : null);
}

public class ActiveKftmTiePromptViewModel
{
	public TiebreakerScope Scope { get; set; }

	public string ContextLabel { get; set; } = string.Empty;

	public List<SelectListItem> TiedTeams { get; set; } = new();
}

public class SavedKftmOverrideViewModel
{
	public int Id { get; set; }

	public TiebreakerScope Scope { get; set; }

	public string TeamADisplay { get; set; } = string.Empty;

	public string TeamBDisplay { get; set; } = string.Empty;

	public string WinnerDisplay { get; set; } = string.Empty;

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;
}

public class ScoresEntryPageViewModel
{
	public bool HasTournamentData => SelectedYear is not null && SelectedDivision is not null;

	public TournamentYearDto? SelectedYear { get; set; }

	public List<TournamentYearDto> AvailableYears { get; set; } = new();

	public DivisionSummaryDto? SelectedDivision { get; set; }

	public List<DivisionSummaryDto> Divisions { get; set; } = new();

	public int? SelectedRound { get; set; }

	public List<int> AvailableRounds { get; set; } = new();

	public string ActiveTab { get; set; } = "pool";

	public bool HasChampionshipBracket { get; set; }

	public List<PoolPlayScoreEntryCardViewModel> PoolPlayCards { get; set; } = new();

	public List<ChampionshipScoreEntryCardViewModel> ChampionshipCards { get; set; } = new();

	public List<ActiveKftmTiePromptViewModel> ActiveKftmPrompts { get; set; } = new();

	public List<SavedKftmOverrideViewModel> SavedKftmOverrides { get; set; } = new();

	public List<SelectListItem> DivisionTeamOptions { get; set; } = new();

	public int ActiveKftmTieCount => ActiveKftmPrompts.Count;
}

public class DivisionScoresFormPostModel
{
	public int Year { get; set; }

	public string DivisionKey { get; set; } = string.Empty;

	public int DivisionId { get; set; }

	public int? SelectedRound { get; set; }

	public string ActiveTab { get; set; } = "pool";

	public List<PoolPlayScoreUpdateDto> PoolPlayScores { get; set; } = new();

	public List<ChampionshipScoreUpdateDto> ChampionshipScores { get; set; } = new();

	public List<PsoScoreUpdateDto> PsoScores { get; set; } = new();
}
