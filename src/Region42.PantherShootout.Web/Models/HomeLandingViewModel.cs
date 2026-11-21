using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Web.Models;

public class DivisionHubCardViewModel
{
	public int DivisionId { get; set; }

	public string DivisionKey { get; set; } = string.Empty;

	public string ShortCode { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public AgeGroup AgeGroup { get; set; }

	public Gender Gender { get; set; }

	public DivisionSize Size { get; set; }

	public int PoolCount { get; set; }

	public int TeamCount { get; set; }

	public int CompletedPoolGames { get; set; }

	public int TotalPoolGames { get; set; }

	public bool HasBracket { get; set; }

	public string PhaseLabel { get; set; } = "Scheduled";

	public string PhaseBadgeClass { get; set; } = "bg-secondary-subtle text-secondary-emphasis";

	public string? ChampionTeamDisplay { get; set; }

	public string? PsoLeaderDisplay { get; set; }

	public bool IsPsoFinal { get; set; }

	public bool IsPreferred { get; set; }

	public string? CustomMessage { get; set; }
}

public class HomeLandingViewModel
{
	public bool HasTournamentData => SelectedYear is not null && Divisions.Count > 0;

	public TournamentYearDto? SelectedYear { get; set; }

	public List<TournamentYearDto> AvailableYears { get; set; } = new();

	public string? PreferredDivisionKey { get; set; }

	public DivisionHubCardViewModel? PreferredDivision =>
		string.IsNullOrWhiteSpace(PreferredDivisionKey)
			? null
			: Divisions.FirstOrDefault(d => string.Equals(d.DivisionKey, PreferredDivisionKey, StringComparison.OrdinalIgnoreCase));

	public List<DivisionHubCardViewModel> Divisions { get; set; } = new();
}
