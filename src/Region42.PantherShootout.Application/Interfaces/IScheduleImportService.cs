using Region42.PantherShootout.Application.DTOs;

namespace Region42.PantherShootout.Application.Interfaces;

/// <summary>
/// Two-step Excel (<c>.xlsx</c>) tournament roster and schedule import service:
/// Step 1 validates the entire workbook and produces a full preview; Step 2 acquires an exclusive
/// write lock and atomically commits divisions, pools, teams, games, championship shells, and PSO rounds.
/// </summary>
public interface IScheduleImportService
{
	Task<ScheduleImportPreviewResult> ValidateWorkbookAsync(
		Stream workbookStream,
		int tournamentYearId,
		IReadOnlyList<ScheduleCellOverrideDto>? cellOverrides = null,
		CancellationToken cancellationToken = default);

	Task<ScheduleImportCommitResult> ImportWorkbookAsync(
		Stream workbookStream,
		int tournamentYearId,
		bool allowOverwriteScores = false,
		IReadOnlyList<ScheduleCellOverrideDto>? cellOverrides = null,
		CancellationToken cancellationToken = default);
}
