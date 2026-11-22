using System.ComponentModel.DataAnnotations;
using Region42.PantherShootout.Application.DTOs;

namespace Region42.PantherShootout.Web.Models;

public class TournamentYearsIndexViewModel
{
	public List<TournamentYearDto> Years { get; set; } = new();

	[Range(2000, 2100)]
	public int NewYear { get; set; } = DateTime.UtcNow.Year;

	[MaxLength(120)]
	public string? NewName { get; set; }

	public bool NewIsActive { get; set; } = true;

	[MaxLength(500)]
	public string? NewCustomMessage { get; set; }
}

public class EditTournamentYearViewModel
{
	public int Id { get; set; }

	public int Year { get; set; }

	[Required]
	[MaxLength(120)]
	public string Name { get; set; } = string.Empty;

	public bool IsActive { get; set; }

	[MaxLength(500)]
	public string? CustomMessage { get; set; }
}

public class AysoRegionsIndexViewModel
{
	public List<AysoRegionDto> Regions { get; set; } = new();

	public int? EditId { get; set; }

	[Range(1, 99999)]
	public int RegionNumber { get; set; }

	[Required]
	[MaxLength(100)]
	public string Name { get; set; } = string.Empty;

	public string? ReturnToImport { get; set; }
}

public class DivisionBulletinsIndexViewModel
{
	public int SelectedTournamentYearId { get; set; }

	public int SelectedYear { get; set; }

	public string SelectedYearName { get; set; } = string.Empty;

	public List<TournamentYearDto> Years { get; set; } = new();

	public List<DivisionSummaryDto> Divisions { get; set; } = new();
}

public class EditDivisionBulletinViewModel
{
	public int DivisionId { get; set; }

	public int TournamentYearId { get; set; }

	public int Year { get; set; }

	public string DivisionName { get; set; } = string.Empty;

	public string ShortCode { get; set; } = string.Empty;

	[MaxLength(500)]
	public string? CustomMessage { get; set; }
}

public class AdminTeamsViewModel
{
	public int SelectedTournamentYearId { get; set; }

	public int SelectedYear { get; set; }

	public string SelectedYearName { get; set; } = string.Empty;

	public int SelectedDivisionId { get; set; }

	public string SelectedDivisionKey { get; set; } = string.Empty;

	public string SelectedDivisionShortCode { get; set; } = string.Empty;

	public string SelectedDivisionName { get; set; } = string.Empty;

	public List<TournamentYearDto> Years { get; set; } = new();

	public List<DivisionSummaryDto> Divisions { get; set; } = new();

	public List<AysoRegionDto> Regions { get; set; } = new();

	public List<AdminPoolTeamsViewModel> Pools { get; set; } = new();

	public List<AdminTeamEditRowViewModel> Teams { get; set; } = new();
}

public class AdminPoolTeamsViewModel
{
	public int PoolId { get; set; }

	public string PoolName { get; set; } = string.Empty;

	public List<int> TeamIndices { get; set; } = new();
}

public class AdminTeamEditRowViewModel
{
	public int TeamId { get; set; }

	public int PoolId { get; set; }

	public string PoolName { get; set; } = string.Empty;

	public int TeamNumber { get; set; }

	public string Code { get; set; } = string.Empty;

	[Range(1, 99999, ErrorMessage = "Please select a valid AYSO region.")]
	public int RegionNumber { get; set; }

	public string RegionName { get; set; } = string.Empty;

	[Required(ErrorMessage = "Coach name is required.")]
	[MaxLength(100)]
	public string CoachName { get; set; } = string.Empty;

	public bool HasVolunteerPointDeduction { get; set; }
}
