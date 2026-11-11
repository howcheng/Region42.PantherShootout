using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Documents;

namespace Region42.PantherShootout.Application.Interfaces;

/// <summary>
/// Validates and persists pool-play / championship / PSO scores and KFTM overrides,
/// then refreshes standings snapshots for the affected division.
/// </summary>
public interface IScoreEntryService
{
	Task<DivisionStandingsSnapshotDocument> SavePoolPlayScoreAsync(
		PoolPlayScoreUpdateDto update,
		CancellationToken cancellationToken = default);

	Task<DivisionStandingsSnapshotDocument> SaveChampionshipScoreAsync(
		ChampionshipScoreUpdateDto update,
		CancellationToken cancellationToken = default);

	Task<DivisionStandingsSnapshotDocument> SavePsoScoreAsync(
		PsoScoreUpdateDto update,
		CancellationToken cancellationToken = default);

	Task<DivisionStandingsSnapshotDocument> UpsertKftmOverrideAsync(
		KftmOverrideUpsertDto update,
		CancellationToken cancellationToken = default);

	Task<DivisionStandingsSnapshotDocument> RemoveKftmOverrideAsync(
		int overrideId,
		CancellationToken cancellationToken = default);
}
