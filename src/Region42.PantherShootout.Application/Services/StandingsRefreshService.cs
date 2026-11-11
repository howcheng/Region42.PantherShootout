using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Orchestrates full division standings refresh: pool-play snapshot, championship seeding/progression,
/// PSO standings, combined <see cref="DivisionStandingsSnapshotDocument"/> persistence.
/// </summary>
public class StandingsRefreshService : IStandingsRefreshService
{
	private readonly ITournamentDataContext _dataContext;
	private readonly IPoolPlayStandingsService _poolPlayStandingsService;
	private readonly IChampionshipBracketService _championshipBracketService;
	private readonly IPsoStandingsService _psoStandingsService;

	public StandingsRefreshService(
		ITournamentDataContext dataContext,
		IPoolPlayStandingsService poolPlayStandingsService,
		IChampionshipBracketService championshipBracketService,
		IPsoStandingsService psoStandingsService)
	{
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
		_poolPlayStandingsService = poolPlayStandingsService
			?? throw new ArgumentNullException(nameof(poolPlayStandingsService));
		_championshipBracketService = championshipBracketService
			?? throw new ArgumentNullException(nameof(championshipBracketService));
		_psoStandingsService = psoStandingsService
			?? throw new ArgumentNullException(nameof(psoStandingsService));
	}

	public Task<DivisionStandingsSnapshotDocument> RefreshDivisionAsync(
		int divisionId,
		bool saveChanges = true,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);
		return RefreshDivisionInternalAsync(divisionId, saveChanges, cancellationToken);
	}

	private async Task<DivisionStandingsSnapshotDocument> RefreshDivisionInternalAsync(
		int divisionId,
		bool saveChanges,
		CancellationToken cancellationToken)
	{
		var poolStandings = await _poolPlayStandingsService
			.CalculateForDivisionAsync(divisionId, cancellationToken)
			.ConfigureAwait(false);

		var bracketResult = await _championshipBracketService
			.SeedAndProgressAsync(divisionId, cancellationToken)
			.ConfigureAwait(false);

		var psoStandings = await _psoStandingsService
			.CalculateForDivisionAsync(divisionId, cancellationToken)
			.ConfigureAwait(false);

		var snapshot = PoolPlayStandingsSnapshotMapper.ToSnapshot(poolStandings);
		snapshot.PsoStandings = PsoStandingsSnapshotMapper.ToSnapshot(psoStandings);

		// Prefer the bracket/service-resolved champion (includes 5/10 no-bracket cases and
		// clears pool placeholders until a championship game winner exists).
		snapshot.ChampionTeamId = bracketResult.ChampionTeamId;

		_dataContext.SetStandingsForDivision(divisionId, snapshot);

		if (saveChanges)
		{
			await _dataContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return snapshot;
	}
}
