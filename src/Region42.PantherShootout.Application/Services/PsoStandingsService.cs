using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Async facade that loads division data and delegates PSO ranking to
/// <see cref="PsoStandingsCalculator"/>.
/// </summary>
public class PsoStandingsService : IPsoStandingsService
{
	private readonly ITournamentDataContext _dataContext;

	public PsoStandingsService(ITournamentDataContext dataContext)
	{
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
	}

	public Task<PsoStandingsResult> CalculateForDivisionAsync(
		int divisionId,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);
		return CalculateForDivisionInternalAsync(divisionId, cancellationToken);
	}

	public Task<PsoStandingsSnapshotDocument> BuildSnapshotAsync(
		int divisionId,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);
		return BuildSnapshotInternalAsync(divisionId, cancellationToken);
	}

	private async Task<PsoStandingsResult> CalculateForDivisionInternalAsync(
		int divisionId,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadDivisionDataAsync(divisionId, cancellationToken).ConfigureAwait(false);

		var division = _dataContext.Divisions.FirstOrDefault(d => d.Id == divisionId)
			?? throw new InvalidOperationException($"Division {divisionId} was not found in the data context.");

		var pools = _dataContext.Pools.Where(p => p.DivisionId == divisionId).ToList();
		var poolIds = pools.Select(p => p.Id).ToHashSet();
		var teams = _dataContext.Teams.Where(t => poolIds.Contains(t.PoolId)).ToList();

		foreach (var team in teams.Where(t => t.Pool is null))
		{
			team.Pool = pools.FirstOrDefault(p => p.Id == team.PoolId);
		}

		var rounds = _dataContext.ShootoutRounds.Where(r => r.DivisionId == divisionId).ToList();
		var overrides = _dataContext.TiebreakerOverrides.Where(o => o.DivisionId == divisionId).ToList();

		return PsoStandingsCalculator.Calculate(division, teams, rounds, overrides);
	}

	private async Task<PsoStandingsSnapshotDocument> BuildSnapshotInternalAsync(
		int divisionId,
		CancellationToken cancellationToken)
	{
		var result = await CalculateForDivisionInternalAsync(divisionId, cancellationToken).ConfigureAwait(false);
		return PsoStandingsSnapshotMapper.ToSnapshot(result);
	}
}
