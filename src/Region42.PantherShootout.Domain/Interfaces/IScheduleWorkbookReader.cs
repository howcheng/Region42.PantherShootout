using Region42.PantherShootout.Domain.Models;

namespace Region42.PantherShootout.Domain.Interfaces;

/// <summary>
/// Extracts raw team-pool blocks and schedule grid cells from the <c>Pools</c> and <c>Schedule</c>
/// worksheets of an <c>.xlsx</c> workbook stream, ignoring all other tabs.
/// </summary>
public interface IScheduleWorkbookReader
{
	ParsedWorkbookData ReadWorkbook(Stream workbookStream);
}
