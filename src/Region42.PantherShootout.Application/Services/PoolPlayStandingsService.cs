using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Async facade that loads division data from <see cref="ITournamentDataContext"/> and
/// delegates ranking to the pure <see cref="PoolPlayStandingsCalculator"/>.
/// </summary>
public class PoolPlayStandingsService : IPoolPlayStandingsService
{
	private readonly ITournamentDataContext _dataContext;

	public PoolPlayStandingsService(ITournamentDataContext dataContext)
	{
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
	}

	public Task<DivisionPoolPlayStandingsResult> CalculateForDivisionAsync(
		int divisionId,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);
		return CalculateForDivisionInternalAsync(divisionId, cancellationToken);
	}

	public Task<DivisionStandingsSnapshotDocument> BuildPoolPlaySnapshotAsync(
		int divisionId,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);
		return BuildPoolPlaySnapshotInternalAsync(divisionId, cancellationToken);
	}

	private async Task<DivisionPoolPlayStandingsResult> CalculateForDivisionInternalAsync(
		int divisionId,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadDivisionDataAsync(divisionId, cancellationToken).ConfigureAwait(false);

		var division = _dataContext.Divisions.FirstOrDefault(d => d.Id == divisionId)
			?? throw new InvalidOperationException($"Division {divisionId} was not found in the data context.");

		var pools = _dataContext.Pools.Where(p => p.DivisionId == divisionId).ToList();
		var poolIds = pools.Select(p => p.Id).ToHashSet();
		var teams = _dataContext.Teams.Where(t => poolIds.Contains(t.PoolId)).ToList();
		var games = _dataContext.PoolPlayGames.Where(g => g.DivisionId == divisionId).ToList();
		var overrides = _dataContext.TiebreakerOverrides.Where(o => o.DivisionId == divisionId).ToList();

		return PoolPlayStandingsCalculator.CalculateDivision(
			division,
			pools,
			teams,
			games,
			overrides);
	}

	private async Task<DivisionStandingsSnapshotDocument> BuildPoolPlaySnapshotInternalAsync(
		int divisionId,
		CancellationToken cancellationToken)
	{
		var result = await CalculateForDivisionInternalAsync(divisionId, cancellationToken).ConfigureAwait(false);
		return PoolPlayStandingsSnapshotMapper.ToSnapshot(result);
	}
}
