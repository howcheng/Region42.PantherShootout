using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Validates score / KFTM inputs, updates tracked entities, refreshes division standings, and saves.
/// </summary>
public class ScoreEntryService : IScoreEntryService
{
	private readonly ITournamentDataContext _dataContext;
	private readonly IStandingsRefreshService _standingsRefreshService;

	public ScoreEntryService(
		ITournamentDataContext dataContext,
		IStandingsRefreshService standingsRefreshService)
	{
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
		_standingsRefreshService = standingsRefreshService
			?? throw new ArgumentNullException(nameof(standingsRefreshService));
	}

	public Task<DivisionStandingsSnapshotDocument> SavePoolPlayScoreAsync(
		PoolPlayScoreUpdateDto update,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(update.GameId);
		ValidateNonNegativeCards(update);
		ValidateForfeitScore(update.IsForfeit, update.HomeGoals, update.AwayGoals);
		ValidateGoalPair(update.HomeGoals, update.AwayGoals);
		return SavePoolPlayScoreInternalAsync(update, cancellationToken);
	}

	public Task<DivisionStandingsSnapshotDocument> SaveChampionshipScoreAsync(
		ChampionshipScoreUpdateDto update,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(update.GameId);
		ValidateForfeitScore(update.IsForfeit, update.HomeGoals, update.AwayGoals);
		ValidateGoalPair(update.HomeGoals, update.AwayGoals);
		return SaveChampionshipScoreInternalAsync(update, cancellationToken);
	}

	public Task<DivisionStandingsSnapshotDocument> SavePsoScoreAsync(
		PsoScoreUpdateDto update,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(update.ShootoutRoundId);
		ValidateGoalPair(update.HomePkGoals, update.AwayPkGoals);
		return SavePsoScoreInternalAsync(update, cancellationToken);
	}

	public Task<DivisionStandingsSnapshotDocument> UpsertKftmOverrideAsync(
		KftmOverrideUpsertDto update,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(update.DivisionId);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(update.TeamAId);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(update.TeamBId);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(update.WinningTeamId);

		if (update.TeamAId == update.TeamBId)
		{
			throw new ArgumentException("KFTM override requires two different teams.", nameof(update));
		}

		if (update.WinningTeamId != update.TeamAId && update.WinningTeamId != update.TeamBId)
		{
			throw new ArgumentException("WinningTeamId must be TeamAId or TeamBId.", nameof(update));
		}

		if (!Enum.IsDefined(update.Scope))
		{
			throw new ArgumentOutOfRangeException(nameof(update), update.Scope, "Unknown tiebreaker scope.");
		}

		return UpsertKftmOverrideInternalAsync(update, cancellationToken);
	}

	public Task<DivisionStandingsSnapshotDocument> RemoveKftmOverrideAsync(
		int overrideId,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(overrideId);
		return RemoveKftmOverrideInternalAsync(overrideId, cancellationToken);
	}

	public Task<DivisionStandingsSnapshotDocument> SaveDivisionScoresBatchAsync(
		DivisionScoreBatchUpdateDto batch,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(batch);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batch.DivisionId);

		foreach (var poolItem in batch.PoolPlayScores ?? [])
		{
			ArgumentNullException.ThrowIfNull(poolItem);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(poolItem.GameId);
			ValidateNonNegativeCards(poolItem);
			ValidateForfeitScore(poolItem.IsForfeit, poolItem.HomeGoals, poolItem.AwayGoals);
			ValidateGoalPair(poolItem.HomeGoals, poolItem.AwayGoals);
		}

		foreach (var champItem in batch.ChampionshipScores ?? [])
		{
			ArgumentNullException.ThrowIfNull(champItem);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(champItem.GameId);
			ValidateForfeitScore(champItem.IsForfeit, champItem.HomeGoals, champItem.AwayGoals);
			ValidateGoalPair(champItem.HomeGoals, champItem.AwayGoals);
		}

		foreach (var psoItem in batch.PsoScores ?? [])
		{
			ArgumentNullException.ThrowIfNull(psoItem);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(psoItem.ShootoutRoundId);
			ValidateGoalPair(psoItem.HomePkGoals, psoItem.AwayPkGoals);
		}

		return SaveDivisionScoresBatchInternalAsync(batch, cancellationToken);
	}

	private async Task<DivisionStandingsSnapshotDocument> SavePoolPlayScoreInternalAsync(
		PoolPlayScoreUpdateDto update,
		CancellationToken cancellationToken)
	{
		await EnsureDivisionDataForPoolPlayGameAsync(update.GameId, cancellationToken).ConfigureAwait(false);

		var game = _dataContext.PoolPlayGames.FirstOrDefault(g => g.Id == update.GameId)
			?? throw new InvalidOperationException($"Pool-play game {update.GameId} was not found.");

		game.HomeGoals = update.HomeGoals;
		game.AwayGoals = update.AwayGoals;
		game.IsForfeit = update.IsForfeit;
		game.HomeYellowCards = update.HomeYellowCards;
		game.HomeRedCards = update.HomeRedCards;
		game.AwayYellowCards = update.AwayYellowCards;
		game.AwayRedCards = update.AwayRedCards;

		_dataContext.Update(game);

		return await _standingsRefreshService
			.RefreshDivisionAsync(game.DivisionId, saveChanges: true, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<DivisionStandingsSnapshotDocument> SaveChampionshipScoreInternalAsync(
		ChampionshipScoreUpdateDto update,
		CancellationToken cancellationToken)
	{
		await EnsureDivisionDataForChampionshipGameAsync(update.GameId, cancellationToken).ConfigureAwait(false);

		var game = _dataContext.ChampionshipGames.FirstOrDefault(g => g.Id == update.GameId)
			?? throw new InvalidOperationException($"Championship game {update.GameId} was not found.");

		if (game.HomeTeamId is null || game.AwayTeamId is null)
		{
			throw new InvalidOperationException(
				$"Championship game {update.GameId} cannot accept a score until both teams are seeded.");
		}

		game.HomeGoals = update.HomeGoals;
		game.AwayGoals = update.AwayGoals;
		game.IsForfeit = update.IsForfeit;

		_dataContext.Update(game);

		return await _standingsRefreshService
			.RefreshDivisionAsync(game.DivisionId, saveChanges: true, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<DivisionStandingsSnapshotDocument> SavePsoScoreInternalAsync(
		PsoScoreUpdateDto update,
		CancellationToken cancellationToken)
	{
		await EnsureDivisionDataForShootoutRoundAsync(update.ShootoutRoundId, cancellationToken).ConfigureAwait(false);

		var round = _dataContext.ShootoutRounds.FirstOrDefault(r => r.Id == update.ShootoutRoundId)
			?? throw new InvalidOperationException($"Shootout round {update.ShootoutRoundId} was not found.");

		if (round.HomeTeamId is null || round.AwayTeamId is null)
		{
			throw new InvalidOperationException(
				$"Shootout round {update.ShootoutRoundId} cannot accept PK scores until both teams are assigned.");
		}

		round.HomePkGoals = update.HomePkGoals;
		round.AwayPkGoals = update.AwayPkGoals;

		_dataContext.Update(round);

		return await _standingsRefreshService
			.RefreshDivisionAsync(round.DivisionId, saveChanges: true, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<DivisionStandingsSnapshotDocument> UpsertKftmOverrideInternalAsync(
		KftmOverrideUpsertDto update,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadDivisionDataAsync(update.DivisionId, cancellationToken).ConfigureAwait(false);

		if (_dataContext.Divisions.All(d => d.Id != update.DivisionId))
		{
			throw new InvalidOperationException($"Division {update.DivisionId} was not found.");
		}

		var teamAId = Math.Min(update.TeamAId, update.TeamBId);
		var teamBId = Math.Max(update.TeamAId, update.TeamBId);
		var winningTeamId = update.WinningTeamId;

		var existing = _dataContext.TiebreakerOverrides.FirstOrDefault(o =>
			o.DivisionId == update.DivisionId &&
			o.Scope == update.Scope &&
			((o.TeamAId == teamAId && o.TeamBId == teamBId) ||
			 (o.TeamAId == teamBId && o.TeamBId == teamAId)));

		if (existing is null)
		{
			var created = new TiebreakerOverride
			{
				Id = _dataContext.AllocateNextId(),
				DivisionId = update.DivisionId,
				Scope = update.Scope,
				TeamAId = teamAId,
				TeamBId = teamBId,
				WinningTeamId = winningTeamId,
			};
			_dataContext.Add(created);
		}
		else
		{
			existing.TeamAId = teamAId;
			existing.TeamBId = teamBId;
			existing.WinningTeamId = winningTeamId;
			existing.Scope = update.Scope;
			_dataContext.Update(existing);
		}

		return await _standingsRefreshService
			.RefreshDivisionAsync(update.DivisionId, saveChanges: true, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<DivisionStandingsSnapshotDocument> RemoveKftmOverrideInternalAsync(
		int overrideId,
		CancellationToken cancellationToken)
	{
		var existing = _dataContext.TiebreakerOverrides.FirstOrDefault(o => o.Id == overrideId);
		if (existing is null)
		{
			await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);
			existing = _dataContext.TiebreakerOverrides.FirstOrDefault(o => o.Id == overrideId);
		}

		if (existing is null)
		{
			throw new InvalidOperationException($"KFTM override {overrideId} was not found in the data context.");
		}

		var divisionId = existing.DivisionId;
		await _dataContext.LoadDivisionDataAsync(divisionId, cancellationToken).ConfigureAwait(false);

		existing = _dataContext.TiebreakerOverrides.FirstOrDefault(o => o.Id == overrideId)
			?? throw new InvalidOperationException($"KFTM override {overrideId} was not found.");

		_dataContext.Remove(existing);

		return await _standingsRefreshService
			.RefreshDivisionAsync(divisionId, saveChanges: true, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<DivisionStandingsSnapshotDocument> SaveDivisionScoresBatchInternalAsync(
		DivisionScoreBatchUpdateDto batch,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadDivisionDataAsync(batch.DivisionId, cancellationToken).ConfigureAwait(false);

		if (_dataContext.Divisions.All(d => d.Id != batch.DivisionId))
		{
			throw new InvalidOperationException($"Division {batch.DivisionId} was not found.");
		}

		await using var tx = await _dataContext.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

		foreach (var poolUpdate in batch.PoolPlayScores ?? [])
		{
			var game = _dataContext.PoolPlayGames.FirstOrDefault(g =>
				g.Id == poolUpdate.GameId && g.DivisionId == batch.DivisionId)
				?? throw new InvalidOperationException(
					$"Pool-play game {poolUpdate.GameId} was not found in division {batch.DivisionId}.");

			if (HasPoolPlayChanges(game, poolUpdate))
			{
				game.HomeGoals = poolUpdate.HomeGoals;
				game.AwayGoals = poolUpdate.AwayGoals;
				game.IsForfeit = poolUpdate.IsForfeit;
				game.HomeYellowCards = poolUpdate.HomeYellowCards;
				game.HomeRedCards = poolUpdate.HomeRedCards;
				game.AwayYellowCards = poolUpdate.AwayYellowCards;
				game.AwayRedCards = poolUpdate.AwayRedCards;

				_dataContext.Update(game);
			}
		}

		if ((batch.PoolPlayScores?.Count ?? 0) > 0 &&
			((batch.ChampionshipScores?.Count ?? 0) > 0 || (batch.PsoScores?.Count ?? 0) > 0))
		{
			// Seed opening bracket games and 6-team PSO Round 4 pairings in memory before applying bracket/PSO edits
			await _standingsRefreshService
				.RefreshDivisionAsync(batch.DivisionId, saveChanges: false, cancellationToken)
				.ConfigureAwait(false);
		}

		foreach (var champUpdate in batch.ChampionshipScores ?? [])
		{
			var game = _dataContext.ChampionshipGames.FirstOrDefault(g =>
				g.Id == champUpdate.GameId && g.DivisionId == batch.DivisionId)
				?? throw new InvalidOperationException(
					$"Championship game {champUpdate.GameId} was not found in division {batch.DivisionId}.");

			if (game.HomeTeamId is null || game.AwayTeamId is null)
			{
				if (champUpdate.HomeGoals is null && champUpdate.AwayGoals is null && !champUpdate.IsForfeit)
				{
					continue;
				}

				throw new InvalidOperationException(
					$"Championship game {champUpdate.GameId} cannot accept a score until both teams are seeded.");
			}

			if (HasChampionshipChanges(game, champUpdate))
			{
				game.HomeGoals = champUpdate.HomeGoals;
				game.AwayGoals = champUpdate.AwayGoals;
				game.IsForfeit = champUpdate.IsForfeit;

				_dataContext.Update(game);
			}
		}

		foreach (var psoUpdate in batch.PsoScores ?? [])
		{
			var round = _dataContext.ShootoutRounds.FirstOrDefault(r =>
				r.Id == psoUpdate.ShootoutRoundId && r.DivisionId == batch.DivisionId)
				?? throw new InvalidOperationException(
					$"Shootout round {psoUpdate.ShootoutRoundId} was not found in division {batch.DivisionId}.");

			if (round.HomeTeamId is null || round.AwayTeamId is null)
			{
				if (psoUpdate.HomePkGoals is null && psoUpdate.AwayPkGoals is null)
				{
					continue;
				}

				throw new InvalidOperationException(
					$"Shootout round {psoUpdate.ShootoutRoundId} cannot accept PK scores until both teams are assigned.");
			}

			if (HasPsoChanges(round, psoUpdate))
			{
				round.HomePkGoals = psoUpdate.HomePkGoals;
				round.AwayPkGoals = psoUpdate.AwayPkGoals;

				_dataContext.Update(round);
			}
		}

		var snapshot = await _standingsRefreshService
			.RefreshDivisionAsync(batch.DivisionId, saveChanges: true, cancellationToken)
			.ConfigureAwait(false);

		await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
		return snapshot;
	}

	private static bool HasPoolPlayChanges(PoolPlayGame game, PoolPlayScoreUpdateDto update)
	{
		return game.HomeGoals != update.HomeGoals ||
			game.AwayGoals != update.AwayGoals ||
			game.IsForfeit != update.IsForfeit ||
			game.HomeYellowCards != update.HomeYellowCards ||
			game.HomeRedCards != update.HomeRedCards ||
			game.AwayYellowCards != update.AwayYellowCards ||
			game.AwayRedCards != update.AwayRedCards;
	}

	private static bool HasChampionshipChanges(ChampionshipGame game, ChampionshipScoreUpdateDto update)
	{
		return game.HomeGoals != update.HomeGoals ||
			game.AwayGoals != update.AwayGoals ||
			game.IsForfeit != update.IsForfeit;
	}

	private static bool HasPsoChanges(ShootoutRound round, PsoScoreUpdateDto update)
	{
		return round.HomePkGoals != update.HomePkGoals ||
			round.AwayPkGoals != update.AwayPkGoals;
	}

	private async Task EnsureDivisionDataForPoolPlayGameAsync(int gameId, CancellationToken cancellationToken)
	{
		var game = _dataContext.PoolPlayGames.FirstOrDefault(g => g.Id == gameId);
		if (game is not null)
		{
			await _dataContext.LoadDivisionDataAsync(game.DivisionId, cancellationToken).ConfigureAwait(false);
			return;
		}

		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);
		game = _dataContext.PoolPlayGames.FirstOrDefault(g => g.Id == gameId)
			?? throw new InvalidOperationException($"Pool-play game {gameId} was not found.");
		await _dataContext.LoadDivisionDataAsync(game.DivisionId, cancellationToken).ConfigureAwait(false);
	}

	private async Task EnsureDivisionDataForChampionshipGameAsync(int gameId, CancellationToken cancellationToken)
	{
		var game = _dataContext.ChampionshipGames.FirstOrDefault(g => g.Id == gameId);
		if (game is not null)
		{
			await _dataContext.LoadDivisionDataAsync(game.DivisionId, cancellationToken).ConfigureAwait(false);
			return;
		}

		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);
		game = _dataContext.ChampionshipGames.FirstOrDefault(g => g.Id == gameId)
			?? throw new InvalidOperationException($"Championship game {gameId} was not found.");
		await _dataContext.LoadDivisionDataAsync(game.DivisionId, cancellationToken).ConfigureAwait(false);
	}

	private async Task EnsureDivisionDataForShootoutRoundAsync(int roundId, CancellationToken cancellationToken)
	{
		var round = _dataContext.ShootoutRounds.FirstOrDefault(r => r.Id == roundId);
		if (round is not null)
		{
			await _dataContext.LoadDivisionDataAsync(round.DivisionId, cancellationToken).ConfigureAwait(false);
			return;
		}

		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);
		round = _dataContext.ShootoutRounds.FirstOrDefault(r => r.Id == roundId)
			?? throw new InvalidOperationException($"Shootout round {roundId} was not found.");
		await _dataContext.LoadDivisionDataAsync(round.DivisionId, cancellationToken).ConfigureAwait(false);
	}

	private static void ValidateNonNegativeCards(PoolPlayScoreUpdateDto update)
	{
		if (update.HomeYellowCards < 0 || update.HomeRedCards < 0 ||
			update.AwayYellowCards < 0 || update.AwayRedCards < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(update), "Card counts cannot be negative.");
		}
	}

	private static void ValidateGoalPair(int? home, int? away)
	{
		if (home is null && away is null)
		{
			return;
		}

		if (home is null || away is null)
		{
			throw new ArgumentException("Home and away scores must both be set or both be cleared.");
		}

		if (home < 0 || away < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(home), "Scores cannot be negative.");
		}
	}

	private static void ValidateForfeitScore(bool isForfeit, int? homeGoals, int? awayGoals)
	{
		if (!isForfeit)
		{
			return;
		}

		if (homeGoals is null || awayGoals is null)
		{
			throw new ArgumentException("Forfeit games require a 1-0 score to be entered.");
		}

		var isValidForfeitScore =
			(homeGoals == 1 && awayGoals == 0) ||
			(homeGoals == 0 && awayGoals == 1);

		if (!isValidForfeitScore)
		{
			throw new ArgumentException("Forfeit games must be entered as 1-0 (or 0-1).");
		}
	}
}
