using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Web.Models;

public class ScheduleUploadViewModel
{
	public int SelectedTournamentYearId { get; set; }

	public int NewYear { get; set; } = DateTime.UtcNow.Year;

	public string? NewYearName { get; set; }

	public string DefaultNewYearName => $"{NewYear} Panther Shootout";

	public List<SelectListItem> YearOptions { get; set; } = new();

	public string? UploadedFileName { get; set; }

	public List<int> MissingRegionNumbers { get; set; } = new();

	public List<ScheduleImportValidationErrorDto> ValidationErrors { get; set; } = new();
}

public class ScheduleCellOverrideInputModel
{
	[Required]
	public string CellReference { get; set; } = string.Empty;

	/// <summary>
	/// Encodes either <c>auto</c>, <c>ignore</c>, or <c>{divisionKey}:{BracketSlot}</c>
	/// (e.g. <c>10u-girls:Championship</c>).
	/// </summary>
	public string Selection { get; set; } = "auto";
}

public class SchedulePreviewViewModel
{
	public string PreviewToken { get; set; } = string.Empty;

	public string FileName { get; set; } = string.Empty;

	public int TournamentYearId { get; set; }

	public int Year { get; set; }

	public string TournamentName { get; set; } = string.Empty;

	public ScheduleImportPreviewResult Preview { get; set; } = new();

	public List<ScheduleCellOverrideInputModel> Overrides { get; set; } = new();

	public List<SelectListItem> BracketOverrideOptions { get; set; } = new();

	public bool AllowOverwriteScores { get; set; }

	public int TotalPools => Preview.Divisions.Sum(d => d.Pools.Count);

	public int TotalTeams => Preview.Divisions.Sum(d => d.Pools.Sum(p => p.Teams.Count));

	public int TotalPoolPlayGames => Preview.Divisions.Sum(d => d.PoolPlayGames.Count);

	public int TotalChampionshipGames => Preview.Divisions.Sum(d => d.ChampionshipGames.Count);

	public int TotalShootoutMatchups => Preview.Divisions.Sum(d => d.ShootoutMatchupCount);
}
