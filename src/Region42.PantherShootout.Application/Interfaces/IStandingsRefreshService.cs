using Region42.PantherShootout.Domain.Documents;

namespace Region42.PantherShootout.Application.Interfaces;

/// <summary>
/// Recalculates pool-play standings, advances championship brackets, refreshes PSO standings,
/// writes the combined snapshot onto the division document, and optionally persists dirty files.
/// </summary>
public interface IStandingsRefreshService
{
	/// <param name="saveChanges">
	/// When true (default), flushes dirty JSON documents via <c>ITournamentDataContext.SaveChangesAsync</c>.
	/// Pass false when the caller is batching multiple mutations inside a transaction.
	/// </param>
	Task<DivisionStandingsSnapshotDocument> RefreshDivisionAsync(
		int divisionId,
		bool saveChanges = true,
		CancellationToken cancellationToken = default);
}
