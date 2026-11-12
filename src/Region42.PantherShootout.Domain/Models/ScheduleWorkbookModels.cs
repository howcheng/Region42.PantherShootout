namespace Region42.PantherShootout.Domain.Models;

/// <summary>
/// Raw data extracted from the <c>Pools</c> and <c>Schedule</c> worksheets of a tournament
/// schedule <c>.xlsx</c> workbook. All other sheets in the workbook are ignored.
/// </summary>
public class ParsedWorkbookData
{
	public bool HasPoolsSheet { get; init; }

	public bool HasScheduleSheet { get; init; }

	public List<ParsedDivisionPoolBlock> DivisionBlocks { get; init; } = new();

	public List<ParsedColorKeyEntry> ColorKeyEntries { get; init; } = new();

	public List<ParsedScheduleCell> ScheduleCells { get; init; } = new();

	public List<ParsedWorkbookIssue> ReaderIssues { get; init; } = new();
}

public class ParsedDivisionPoolBlock
{
	public string HeaderCellReference { get; init; } = string.Empty;

	public string DivisionHeaderText { get; init; } = string.Empty;

	/// <summary>6-character uppercase hex RGB fill on the division header cell (if any).</summary>
	public string? HeaderFillRgb { get; init; }

	public List<ParsedPoolColumn> Pools { get; init; } = new();
}

public class ParsedPoolColumn
{
	/// <summary>1-based pool column position within the division block (e.g. 1 for Pool 1).</summary>
	public int PoolColumnOrdinal { get; init; }

	public string HeaderText { get; init; } = string.Empty;

	public List<ParsedPoolTeamRow> Teams { get; init; } = new();
}

public class ParsedPoolTeamRow
{
	public string CodeCellReference { get; init; } = string.Empty;

	public string RawCode { get; init; } = string.Empty;

	public string TeamCellReference { get; init; } = string.Empty;

	public string RawTeamToken { get; init; } = string.Empty;
}

public class ParsedColorKeyEntry
{
	public string CellReference { get; init; } = string.Empty;

	public string Label { get; init; } = string.Empty;

	/// <summary>6-character uppercase hex RGB fill on the color-key cell (if any).</summary>
	public string? FillRgb { get; init; }
}

public class ParsedScheduleCell
{
	public string CellReference { get; init; } = string.Empty;

	public DateTime ScheduledDateTime { get; init; }

	public string Location { get; init; } = string.Empty;

	public string? ColumnAgeHint { get; init; }

	public string RawText { get; init; } = string.Empty;

	/// <summary>6-character uppercase hex RGB fill on the matchup cell (if any).</summary>
	public string? FillRgb { get; init; }
}

public class ParsedWorkbookIssue
{
	public string SheetName { get; init; } = string.Empty;

	public string CellReference { get; init; } = string.Empty;

	public string Message { get; init; } = string.Empty;
}
