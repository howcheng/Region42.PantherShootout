using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.DTOs;

/// <summary>
/// Optional admin mapping override for a specific cell on the <c>Schedule</c> worksheet when
/// bracket labels or colors vary in a future tournament year.
/// </summary>
public class ScheduleCellOverrideDto
{
	public string CellReference { get; set; } = string.Empty;

	public bool Ignore { get; set; }

	public string? DivisionKey { get; set; }

	public BracketSlot? Slot { get; set; }
}

/// <summary>
/// Structural or business-rule validation error discovered while inspecting a schedule workbook.
/// </summary>
public class ScheduleImportValidationErrorDto
{
	public string Code { get; set; } = string.Empty;

	public string SheetName { get; set; } = string.Empty;

	public string CellReference { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;

	public int? MissingRegionNumber { get; set; }
}

/// <summary>
/// Preview row showing how a non-pool-play cell on the <c>Schedule</c> sheet was interpreted
/// (or left unresolved for admin override selection).
/// </summary>
public class BracketCellMappingPreviewDto
{
	public string CellReference { get; set; } = string.Empty;

	public string RawText { get; set; } = string.Empty;

	public DateTime ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public string? FillRgb { get; set; }

	public string? ResolvedDivisionKey { get; set; }

	public string? ResolvedDivisionName { get; set; }

	public BracketSlot? ResolvedSlot { get; set; }

	public bool IsIgnored { get; set; }

	public bool IsAutoResolved { get; set; }
}

public class ImportedTeamPreviewDto
{
	public string Code { get; set; } = string.Empty;

	public string PoolName { get; set; } = string.Empty;

	public int TeamNumber { get; set; }

	public int RegionNumber { get; set; }

	public string RegionName { get; set; } = string.Empty;

	public string CoachName { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;
}

public class ImportedPoolPreviewDto
{
	public string PoolName { get; set; } = string.Empty;

	public List<ImportedTeamPreviewDto> Teams { get; set; } = new();
}

public class ImportedPoolPlayGamePreviewDto
{
	public string CellReference { get; set; } = string.Empty;

	public string PoolName { get; set; } = string.Empty;

	public int Round { get; set; }

	public DateTime ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public string HomeTeamCode { get; set; } = string.Empty;

	public string AwayTeamCode { get; set; } = string.Empty;
}

public class ImportedChampionshipGamePreviewDto
{
	public string CellReference { get; set; } = string.Empty;

	public BracketSlot Slot { get; set; }

	public DateTime? ScheduledDateTime { get; set; }

	public string Location { get; set; } = string.Empty;

	public string HomeSourceDescription { get; set; } = string.Empty;

	public string AwaySourceDescription { get; set; } = string.Empty;
}

public class ImportedDivisionPreviewDto
{
	public string DivisionKey { get; set; } = string.Empty;

	public string DivisionName { get; set; } = string.Empty;

	public AgeGroup AgeGroup { get; set; }

	public Gender Gender { get; set; }

	public DivisionSize Size { get; set; }

	public int ShootoutRoundCount { get; set; }

	public int ShootoutMatchupCount { get; set; }

	public List<ImportedPoolPreviewDto> Pools { get; set; } = new();

	public List<ImportedPoolPlayGamePreviewDto> PoolPlayGames { get; set; } = new();

	public List<ImportedChampionshipGamePreviewDto> ChampionshipGames { get; set; } = new();
}

/// <summary>
/// Result of Step 1 (Validate &amp; Preview) of the Excel schedule import workflow.
/// </summary>
public class ScheduleImportPreviewResult
{
	public int TournamentYearId { get; set; }

	public int Year { get; set; }

	public bool IsValid => Errors.Count == 0;

	public bool HasExistingData { get; set; }

	public bool HasExistingScores { get; set; }

	public List<int> MissingRegionNumbers { get; set; } = new();

	public List<ScheduleImportValidationErrorDto> Errors { get; set; } = new();

	public List<ImportedDivisionPreviewDto> Divisions { get; set; } = new();

	public List<BracketCellMappingPreviewDto> BracketCellMappings { get; set; } = new();
}

/// <summary>
/// Summary returned after Step 2 (Confirm Import) commits divisions, pools, teams, games, and PSO shells.
/// </summary>
public class ScheduleImportCommitResult
{
	public int TournamentYearId { get; set; }

	public int DivisionsImported { get; set; }

	public int PoolsImported { get; set; }

	public int TeamsImported { get; set; }

	public int PoolPlayGamesImported { get; set; }

	public int ChampionshipGamesImported { get; set; }

	public int ShootoutRoundsImported { get; set; }
}
