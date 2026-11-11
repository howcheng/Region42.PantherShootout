using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Documents;

namespace Region42.PantherShootout.Application.Interfaces;

/// <summary>
/// Loads division roster/PSO rounds/KFTM overrides and calculates division-wide PSO standings.
/// </summary>
public interface IPsoStandingsService
{
	Task<PsoStandingsResult> CalculateForDivisionAsync(
		int divisionId,
		CancellationToken cancellationToken = default);

	Task<PsoStandingsSnapshotDocument> BuildSnapshotAsync(
		int divisionId,
		CancellationToken cancellationToken = default);
}
