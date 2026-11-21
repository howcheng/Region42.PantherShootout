using Region42.PantherShootout.Application.DTOs;

namespace Region42.PantherShootout.Web.Models;

public class PsoMatchupItemViewModel
{
	public int ShootoutRoundId { get; set; }

	public int Round { get; set; }

	public string SourceLabel { get; set; } = string.Empty;

	public DateTime? ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public bool IsSeeded { get; set; }

	public string HomeCode { get; set; } = string.Empty;

	public string HomeShortDisplay { get; set; } = string.Empty;

	public string HomeFullDisplay { get; set; } = string.Empty;

	public string AwayCode { get; set; } = string.Empty;

	public string AwayShortDisplay { get; set; } = string.Empty;

	public string AwayFullDisplay { get; set; } = string.Empty;

	public int? HomePkGoals { get; set; }

	public int? AwayPkGoals { get; set; }

	public bool HomePsoDoesNotCount { get; set; }

	public bool AwayPsoDoesNotCount { get; set; }

	public string? NonCountingPsoTeamCode =>
		HomePsoDoesNotCount ? HomeCode : (AwayPsoDoesNotCount ? AwayCode : null);

	public bool HasScore => HomePkGoals.HasValue && AwayPkGoals.HasValue;
}

public class ShootoutPageViewModel
{
	public bool HasData => SelectedYear is not null && SelectedDivision is not null;

	public bool HasFourthShootoutRound { get; set; }

	public TournamentYearDto? SelectedYear { get; set; }

	public List<TournamentYearDto> AvailableYears { get; set; } = new();

	public DivisionSummaryDto? SelectedDivision { get; set; }

	public List<DivisionSummaryDto> Divisions { get; set; } = new();

	public DivisionPsoTableViewModel? PsoTable { get; set; }

	public List<PsoMatchupItemViewModel> Matchups { get; set; } = new();
}
