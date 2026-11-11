using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Documents;

namespace Region42.PantherShootout.Application.Interfaces;

/// <summary>
/// Loads division roster/game/KFTM data and calculates pool-play standings
/// (including 10-team combined and 12-team pool-winner/runner-up rankings).
/// </summary>
public interface IPoolPlayStandingsService
{
	/// <summary>
	/// Calculates live pool-play standings for a division that has already been loaded
	/// into the scoped <c>ITournamentDataContext</c> (or loads division data first).
	/// </summary>
	Task<DivisionPoolPlayStandingsResult> CalculateForDivisionAsync(
		int divisionId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Calculates standings and maps them into a <see cref="DivisionStandingsSnapshotDocument"/>
	/// suitable for persisting on the division data document (PSO section left empty/default).
	/// </summary>
	Task<DivisionStandingsSnapshotDocument> BuildPoolPlaySnapshotAsync(
		int divisionId,
		CancellationToken cancellationToken = default);
}
