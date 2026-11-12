using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Async facade that calculates pool standings, seeds/progresses championship games, and
/// syncs 6-team PSO Round 4 pairings against <see cref="ITournamentDataContext"/>.
/// </summary>
public class ChampionshipBracketService : IChampionshipBracketService
{
	private readonly ITournamentDataContext _dataContext;
	private readonly IPoolPlayStandingsService _poolPlayStandingsService;

	public ChampionshipBracketService(
		ITournamentDataContext dataContext,
		IPoolPlayStandingsService poolPlayStandingsService)
	{
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
		_poolPlayStandingsService = poolPlayStandingsService
			?? throw new ArgumentNullException(nameof(poolPlayStandingsService));
	}

	public IReadOnlyList<ChampionshipBracketSlotDefinition> GetSlotDefinitions(
		DivisionSize size,
		IReadOnlyList<string>? poolNames = null) =>
		ChampionshipBracketBuilder.GetSlotDefinitions(size, poolNames);

	public IReadOnlyList<ChampionshipGame> CreateBracketShells(
		int divisionId,
		DivisionSize size,
		IReadOnlyList<string>? poolNames = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);
		return ChampionshipBracketBuilder.CreateBracketShells(divisionId, size, poolNames);
	}

	public Task<ChampionshipBracketResult> SeedAndProgressAsync(
		int divisionId,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);
		return SeedAndProgressInternalAsync(divisionId, cancellationToken);
	}

	private async Task<ChampionshipBracketResult> SeedAndProgressInternalAsync(
		int divisionId,
		CancellationToken cancellationToken)
	{
		var poolStandings = await _poolPlayStandingsService
			.CalculateForDivisionAsync(divisionId, cancellationToken)
			.ConfigureAwait(false);

		var division = _dataContext.Divisions.FirstOrDefault(d => d.Id == divisionId)
			?? throw new InvalidOperationException($"Division {divisionId} was not found in the data context.");

		var championshipGames = _dataContext.ChampionshipGames
			.Where(g => g.DivisionId == divisionId)
			.ToList();

		var result = ChampionshipBracketBuilder.SeedAndProgress(
			division,
			poolStandings,
			championshipGames);

		if (division.Size == DivisionSize.Six)
		{
			var shootoutRounds = _dataContext.ShootoutRounds
				.Where(r => r.DivisionId == divisionId)
				.ToList();
			ChampionshipBracketBuilder.ApplyPsoRound4Assignments(shootoutRounds, championshipGames);
		}

		// Snapshot ChampionTeamId is written by StandingsRefreshService after PSO merge.
		return result;
	}
}
