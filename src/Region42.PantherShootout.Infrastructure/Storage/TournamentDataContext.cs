using Microsoft.Extensions.Options;
using Region42.PantherShootout.Domain.Common;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Infrastructure.Storage;

/// <summary>
/// Scoped in-memory tournament data context that lazily hydrates domain entities from JSON documents,
/// wires navigation properties, stamps audit metadata via <see cref="ICurrentUserService"/>, tracks
/// dirty documents per index/year/division, and supports snapshot rollback transactions.
/// </summary>
public class TournamentDataContext : ITournamentDataContext
{
	private readonly ITournamentDataStore _store;
	private readonly ICurrentUserService _currentUserService;
	private readonly string _competitionSlug;

	private readonly List<TournamentYear> _tournamentYears = new();
	private readonly List<Division> _divisions = new();
	private readonly List<Pool> _pools = new();
	private readonly List<Team> _teams = new();
	private readonly List<PoolPlayGame> _poolPlayGames = new();
	private readonly List<ChampionshipGame> _championshipGames = new();
	private readonly List<ShootoutRound> _shootoutRounds = new();
	private readonly List<TiebreakerOverride> _tiebreakerOverrides = new();
	private readonly Dictionary<int, DivisionStandingsSnapshotDocument> _standingsByDivision = new();

	private readonly Dictionary<int, (int Year, string Slug)> _tournamentPathByYearId = new();
	private readonly Dictionary<int, string> _divisionKeyById = new();
	private readonly Dictionary<int, (int Year, string Slug)> _divisionTournamentPath = new();

	private readonly Dictionary<string, StorageDocument<TournamentDocument>> _tournamentStorage = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, StorageDocument<DivisionDataDocument>> _divisionStorage = new(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<(int Year, string Slug)> _dirtyTournamentPaths = new();
	private readonly HashSet<(int Year, string Slug, string DivisionKey)> _dirtyDivisionPaths = new();

	private TournamentIndexDocument _index = new();
	private StorageDocument<TournamentIndexDocument>? _indexStorage;
	private bool _isLoaded;
	private bool _indexDirty;
	private int _nextId = 1;
	private JsonDataTransaction? _activeTransaction;
	private JsonDataTransactionSnapshot? _transactionSnapshot;

	public TournamentDataContext(
		ITournamentDataStore store,
		IOptions<StorageOptions> options,
		ICurrentUserService currentUserService)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(currentUserService);

		_store = store;
		_currentUserService = currentUserService;
		_competitionSlug = string.IsNullOrWhiteSpace(options.Value.CompetitionSlug)
			? DivisionKeyHelper.DefaultCompetitionSlug
			: options.Value.CompetitionSlug;
	}

	public TournamentIndexDocument Index => _index;

	public IReadOnlyList<TournamentYear> TournamentYears => _tournamentYears;

	public IReadOnlyList<Division> Divisions => _divisions;

	public IReadOnlyList<Pool> Pools => _pools;

	public IReadOnlyList<Team> Teams => _teams;

	public IReadOnlyList<PoolPlayGame> PoolPlayGames => _poolPlayGames;

	public IReadOnlyList<ChampionshipGame> ChampionshipGames => _championshipGames;

	public IReadOnlyList<ShootoutRound> ShootoutRounds => _shootoutRounds;

	public IReadOnlyList<TiebreakerOverride> TiebreakerOverrides => _tiebreakerOverrides;

	public async Task LoadAllAsync(CancellationToken cancellationToken = default)
	{
		if (_isLoaded)
		{
			return;
		}

		try
		{
			_indexStorage = await _store.GetIndexAsync(cancellationToken);
			_index = _indexStorage.Content;
		}
		catch (FileNotFoundException)
		{
			_index = new TournamentIndexDocument();
			_indexStorage = new StorageDocument<TournamentIndexDocument>
			{
				Content = _index,
				Generation = null,
				ObjectPath = DivisionKeyHelper.GetIndexObjectPath(_competitionSlug),
			};
		}

		foreach (var entry in _index.Tournaments.ToList())
		{
			var slug = string.IsNullOrWhiteSpace(entry.Slug) ? _competitionSlug : entry.Slug;
			await LoadTournamentYearInternalAsync(entry.Year, slug, cancellationToken);
		}

		WireNavigationProperties();
		RecalculateNextId();
		_isLoaded = true;
	}

	public Task LoadTournamentYearAsync(int year, string competitionSlug, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);

		return LoadTournamentYearAndWireAsync(year, competitionSlug, cancellationToken);
	}

	private async Task LoadTournamentYearAndWireAsync(int year, string competitionSlug, CancellationToken cancellationToken)
	{
		await LoadTournamentYearInternalAsync(year, competitionSlug, cancellationToken);
		WireNavigationProperties();
		RecalculateNextId();
	}

	private async Task LoadTournamentYearInternalAsync(int year, string competitionSlug, CancellationToken cancellationToken)
	{
		var tournamentDirPath = DivisionKeyHelper.GetTournamentDirectoryPath(year, competitionSlug);
		if (_tournamentStorage.ContainsKey(tournamentDirPath))
		{
			return;
		}

		StorageDocument<TournamentDocument> tournamentDoc;
		try
		{
			tournamentDoc = await _store.GetTournamentDocumentAsync(year, competitionSlug, cancellationToken);
		}
		catch (FileNotFoundException)
		{
			return;
		}

		_tournamentStorage[tournamentDirPath] = tournamentDoc;
		HydrateTournamentDocument(tournamentDoc.Content, year, competitionSlug);

		foreach (var divisionDoc in tournamentDoc.Content.Divisions)
		{
			var divisionKey = string.IsNullOrWhiteSpace(divisionDoc.Key)
				? DivisionKeyHelper.ToKey(
					DivisionKeyHelper.ParseAgeGroup(divisionDoc.AgeGroup),
					DivisionKeyHelper.ParseGender(divisionDoc.Gender))
				: divisionDoc.Key;

			await LoadDivisionDataInternalAsync(year, competitionSlug, divisionKey, divisionDoc.Id, cancellationToken);
		}
	}

	private void HydrateTournamentDocument(TournamentDocument document, int year, string competitionSlug)
	{
		var tournamentYear = DocumentEntityMapper.ToEntity(document.Tournament);
		StampHydratedAudit(tournamentYear, document.ModifiedAt, document.ModifiedBy);
		ReplaceOrAdd(_tournamentYears, tournamentYear);
		_tournamentPathByYearId[tournamentYear.Id] = (year, competitionSlug);
		SyncIndexEntry(tournamentYear, year, competitionSlug, markDirty: false);

		foreach (var divisionDoc in document.Divisions)
		{
			var division = DocumentEntityMapper.ToEntity(divisionDoc, tournamentYear.Id, tournamentYear);
			StampHydratedAudit(division, document.ModifiedAt, document.ModifiedBy);
			var divisionKey = string.IsNullOrWhiteSpace(divisionDoc.Key)
				? DivisionKeyHelper.ToKey(division.AgeGroup, division.Gender)
				: divisionDoc.Key;

			ReplaceOrAdd(_divisions, division);
			_divisionKeyById[division.Id] = divisionKey;
			_divisionTournamentPath[division.Id] = (year, competitionSlug);

			foreach (var poolDoc in divisionDoc.Pools)
			{
				var pool = DocumentEntityMapper.ToEntity(poolDoc, division.Id, division);
				StampHydratedAudit(pool, document.ModifiedAt, document.ModifiedBy);
				ReplaceOrAdd(_pools, pool);

				foreach (var teamDoc in poolDoc.Teams)
				{
					var team = DocumentEntityMapper.ToEntity(teamDoc, pool.Id, pool);
					StampHydratedAudit(team, document.ModifiedAt, document.ModifiedBy);
					ReplaceOrAdd(_teams, team);
				}
			}
		}
	}

	private static void StampHydratedAudit(AuditableEntity entity, DateTime modifiedAt, string modifiedBy)
	{
		entity.ModifiedAt = modifiedAt;
		entity.ModifiedBy = modifiedBy;
		if (entity.CreatedAt == default)
		{
			entity.CreatedAt = modifiedAt;
		}
		if (string.IsNullOrEmpty(entity.CreatedBy))
		{
			entity.CreatedBy = modifiedBy;
		}
	}

	public async Task LoadDivisionDataAsync(int divisionId, CancellationToken cancellationToken = default)
	{
		if (!_divisionTournamentPath.TryGetValue(divisionId, out var tournamentPath) ||
			!_divisionKeyById.TryGetValue(divisionId, out var divisionKey))
		{
			throw new InvalidOperationException($"Division {divisionId} is not loaded.");
		}

		await LoadDivisionDataInternalAsync(tournamentPath.Year, tournamentPath.Slug, divisionKey, divisionId, cancellationToken);
		WireNavigationProperties();
		RecalculateNextId();
	}

	private async Task LoadDivisionDataInternalAsync(
		int year,
		string competitionSlug,
		string divisionKey,
		int divisionId,
		CancellationToken cancellationToken)
	{
		var storageKey = GetDivisionStorageKey(year, competitionSlug, divisionKey);
		if (_divisionStorage.ContainsKey(storageKey))
		{
			return;
		}

		StorageDocument<DivisionDataDocument> divisionDoc;
		try
		{
			divisionDoc = await _store.GetDivisionDataAsync(year, competitionSlug, divisionKey, cancellationToken);
		}
		catch (FileNotFoundException)
		{
			divisionDoc = new StorageDocument<DivisionDataDocument>
			{
				Content = new DivisionDataDocument
				{
					DivisionKey = divisionKey,
					DivisionId = divisionId,
				},
				Generation = null,
				ObjectPath = DivisionKeyHelper.GetDivisionObjectPath(year, divisionKey: divisionKey, competitionSlug: competitionSlug),
			};
		}

		_divisionStorage[storageKey] = divisionDoc;
		HydrateDivisionDocument(divisionDoc.Content, divisionId);
	}

	private static string GetDivisionStorageKey(int year, string competitionSlug, string divisionKey)
	{
		return $"{year}/{competitionSlug}/{divisionKey}";
	}

	private void HydrateDivisionDocument(DivisionDataDocument document, int divisionId)
	{
		if (document.Standings is not null)
		{
			_standingsByDivision[divisionId] = CloneStandingsSnapshot(document.Standings);
		}
		else
		{
			_standingsByDivision.Remove(divisionId);
		}

		var division = _divisions.FirstOrDefault(d => d.Id == divisionId);
		var poolsById = _pools.Where(p => p.DivisionId == divisionId).ToDictionary(p => p.Id);
		var divisionPoolIds = poolsById.Keys.ToHashSet();
		var teamsById = _teams.Where(t => divisionPoolIds.Contains(t.PoolId)).ToDictionary(t => t.Id);

		var hydratedPoolGames = new Dictionary<int, PoolPlayGame>();
		foreach (var gameDoc in document.PoolPlayGames)
		{
			poolsById.TryGetValue(gameDoc.PoolId, out var pool);
			teamsById.TryGetValue(gameDoc.HomeTeamId, out var homeTeam);
			teamsById.TryGetValue(gameDoc.AwayTeamId, out var awayTeam);

			var game = DocumentEntityMapper.ToEntity(gameDoc, divisionId, division, pool, homeTeam, awayTeam);
			StampHydratedAudit(game, gameDoc.ModifiedAt, gameDoc.ModifiedBy);
			ReplaceOrAdd(_poolPlayGames, game);
			hydratedPoolGames[game.Id] = game;
		}

		var hydratedChampGames = new Dictionary<int, ChampionshipGame>();
		foreach (var champDoc in document.ChampionshipGames)
		{
			Team? homeTeam = null;
			if (champDoc.HomeTeamId.HasValue)
			{
				teamsById.TryGetValue(champDoc.HomeTeamId.Value, out homeTeam);
			}

			Team? awayTeam = null;
			if (champDoc.AwayTeamId.HasValue)
			{
				teamsById.TryGetValue(champDoc.AwayTeamId.Value, out awayTeam);
			}

			var champGame = DocumentEntityMapper.ToEntity(champDoc, divisionId, division, homeTeam, awayTeam);
			StampHydratedAudit(champGame, champDoc.ModifiedAt, champDoc.ModifiedBy);
			ReplaceOrAdd(_championshipGames, champGame);
			hydratedChampGames[champGame.Id] = champGame;
		}

		foreach (var roundDoc in document.ShootoutRounds)
		{
			PoolPlayGame? poolPlayGame = null;
			if (roundDoc.PoolPlayGameId.HasValue)
			{
				hydratedPoolGames.TryGetValue(roundDoc.PoolPlayGameId.Value, out poolPlayGame);
			}

			ChampionshipGame? championshipGame = null;
			if (roundDoc.ChampionshipGameId.HasValue)
			{
				hydratedChampGames.TryGetValue(roundDoc.ChampionshipGameId.Value, out championshipGame);
			}

			Team? homeTeam = null;
			if (roundDoc.HomeTeamId.HasValue)
			{
				teamsById.TryGetValue(roundDoc.HomeTeamId.Value, out homeTeam);
			}

			Team? awayTeam = null;
			if (roundDoc.AwayTeamId.HasValue)
			{
				teamsById.TryGetValue(roundDoc.AwayTeamId.Value, out awayTeam);
			}

			var shootoutRound = DocumentEntityMapper.ToEntity(
				roundDoc,
				divisionId,
				division,
				poolPlayGame,
				championshipGame,
				homeTeam,
				awayTeam);
			StampHydratedAudit(shootoutRound, roundDoc.ModifiedAt, roundDoc.ModifiedBy);
			ReplaceOrAdd(_shootoutRounds, shootoutRound);
		}

		foreach (var overrideDoc in document.TiebreakerOverrides)
		{
			teamsById.TryGetValue(overrideDoc.TeamAId, out var teamA);
			teamsById.TryGetValue(overrideDoc.TeamBId, out var teamB);
			teamsById.TryGetValue(overrideDoc.WinningTeamId, out var winningTeam);

			var overrideEntity = DocumentEntityMapper.ToEntity(
				overrideDoc,
				divisionId,
				division,
				teamA,
				teamB,
				winningTeam);
			StampHydratedAudit(overrideEntity, overrideDoc.ModifiedAt, overrideDoc.ModifiedBy);
			ReplaceOrAdd(_tiebreakerOverrides, overrideEntity);
		}
	}

	public DivisionStandingsSnapshotDocument? GetStandingsForDivision(int divisionId)
	{
		EnsureLoaded();

		return _standingsByDivision.TryGetValue(divisionId, out var standings)
			? standings
			: null;
	}

	public void SetStandingsForDivision(int divisionId, DivisionStandingsSnapshotDocument standings)
	{
		ArgumentNullException.ThrowIfNull(standings);

		EnsureLoaded();
		_standingsByDivision[divisionId] = CloneStandingsSnapshot(standings);
		MarkDivisionDirty(divisionId);
	}

	public (int Year, string Slug)? GetTournamentPathForYear(int tournamentYearId)
	{
		EnsureLoaded();

		return _tournamentPathByYearId.TryGetValue(tournamentYearId, out var path) ? path : null;
	}

	public string? GetDivisionKey(int divisionId)
	{
		EnsureLoaded();

		return _divisionKeyById.TryGetValue(divisionId, out var key) ? key : null;
	}

	public int AllocateNextId()
	{
		EnsureLoaded();

		return _nextId++;
	}

	public void Add<T>(T entity) where T : AuditableEntity
	{
		ArgumentNullException.ThrowIfNull(entity);

		EnsureLoaded();

		if (entity.Id == 0)
		{
			entity.Id = _nextId++;
		}
		else
		{
			_nextId = Math.Max(_nextId, entity.Id + 1);
		}

		ApplyAuditFields(entity, isNew: true);

		switch (entity)
		{
			case TournamentYear tournamentYear:
				ReplaceOrAdd(_tournamentYears, tournamentYear);
				var yearVal = tournamentYear.Year > 0 ? tournamentYear.Year : DateTime.UtcNow.Year;
				if (!_tournamentPathByYearId.TryGetValue(tournamentYear.Id, out var existingYearPath))
				{
					existingYearPath = (yearVal, _competitionSlug);
					_tournamentPathByYearId[tournamentYear.Id] = existingYearPath;
				}
				SyncIndexEntry(tournamentYear, existingYearPath.Year, existingYearPath.Slug, markDirty: true);
				MarkTournamentDirty(tournamentYear.Id);
				break;

			case Division division:
				ReplaceOrAdd(_divisions, division);
				var divisionKey = DivisionKeyHelper.ToKey(division.AgeGroup, division.Gender);
				_divisionKeyById[division.Id] = divisionKey;
				if (_tournamentPathByYearId.TryGetValue(division.TournamentYearId, out var tournamentPath))
				{
					_divisionTournamentPath[division.Id] = tournamentPath;
				}
				MarkTournamentDirty(division.TournamentYearId);
				break;

			case Pool pool:
				ReplaceOrAdd(_pools, pool);
				MarkTournamentDirtyForDivision(pool.DivisionId);
				break;

			case Team team:
				ReplaceOrAdd(_teams, team);
				MarkTournamentDirtyForPool(team.PoolId);
				break;

			case PoolPlayGame poolPlayGame:
				ReplaceOrAdd(_poolPlayGames, poolPlayGame);
				MarkDivisionDirty(poolPlayGame.DivisionId);
				break;

			case ChampionshipGame championshipGame:
				ReplaceOrAdd(_championshipGames, championshipGame);
				MarkDivisionDirty(championshipGame.DivisionId);
				break;

			case ShootoutRound shootoutRound:
				ReplaceOrAdd(_shootoutRounds, shootoutRound);
				MarkDivisionDirty(shootoutRound.DivisionId);
				break;

			case TiebreakerOverride tiebreakerOverride:
				ReplaceOrAdd(_tiebreakerOverrides, tiebreakerOverride);
				MarkDivisionDirty(tiebreakerOverride.DivisionId);
				break;

			default:
				throw new NotSupportedException($"Entity type {typeof(T).Name} is not supported.");
		}

		WireNavigationProperties();
	}

	public void Update<T>(T entity) where T : AuditableEntity
	{
		ArgumentNullException.ThrowIfNull(entity);

		EnsureLoaded();
		ApplyAuditFields(entity, isNew: false);

		switch (entity)
		{
			case TournamentYear tournamentYear:
				ReplaceOrAdd(_tournamentYears, tournamentYear);
				if (_tournamentPathByYearId.TryGetValue(tournamentYear.Id, out var yearPath))
				{
					SyncIndexEntry(tournamentYear, yearPath.Year, yearPath.Slug, markDirty: true);
				}
				MarkTournamentDirty(tournamentYear.Id);
				break;

			case Division division:
				ReplaceOrAdd(_divisions, division);
				_divisionKeyById[division.Id] = DivisionKeyHelper.ToKey(division.AgeGroup, division.Gender);
				MarkTournamentDirty(division.TournamentYearId);
				break;

			case Pool pool:
				ReplaceOrAdd(_pools, pool);
				MarkTournamentDirtyForDivision(pool.DivisionId);
				break;

			case Team team:
				ReplaceOrAdd(_teams, team);
				MarkTournamentDirtyForPool(team.PoolId);
				break;

			case PoolPlayGame poolPlayGame:
				ReplaceOrAdd(_poolPlayGames, poolPlayGame);
				MarkDivisionDirty(poolPlayGame.DivisionId);
				break;

			case ChampionshipGame championshipGame:
				ReplaceOrAdd(_championshipGames, championshipGame);
				MarkDivisionDirty(championshipGame.DivisionId);
				break;

			case ShootoutRound shootoutRound:
				ReplaceOrAdd(_shootoutRounds, shootoutRound);
				MarkDivisionDirty(shootoutRound.DivisionId);
				break;

			case TiebreakerOverride tiebreakerOverride:
				ReplaceOrAdd(_tiebreakerOverrides, tiebreakerOverride);
				MarkDivisionDirty(tiebreakerOverride.DivisionId);
				break;

			default:
				throw new NotSupportedException($"Entity type {typeof(T).Name} is not supported.");
		}

		WireNavigationProperties();
	}

	public void Remove<T>(T entity) where T : AuditableEntity
	{
		ArgumentNullException.ThrowIfNull(entity);

		EnsureLoaded();

		switch (entity)
		{
			case TournamentYear tournamentYear:
				var divisionIds = _divisions
					.Where(d => d.TournamentYearId == tournamentYear.Id)
					.Select(d => d.Id)
					.ToList();

				foreach (var divId in divisionIds)
				{
					RemoveDivisionChildrenInMemory(divId);
					if (_divisionTournamentPath.TryGetValue(divId, out var divPath) &&
						_divisionKeyById.TryGetValue(divId, out var divKey))
					{
						_dirtyDivisionPaths.Remove((divPath.Year, divPath.Slug, divKey));
					}
					_divisionKeyById.Remove(divId);
					_divisionTournamentPath.Remove(divId);
				}

				_divisions.RemoveAll(d => d.TournamentYearId == tournamentYear.Id);
				_tournamentYears.RemoveAll(y => y.Id == tournamentYear.Id);
				_index.Tournaments.RemoveAll(t => t.TournamentYearId == tournamentYear.Id);
				_indexDirty = true;

				if (_tournamentPathByYearId.Remove(tournamentYear.Id, out var removedYearPath))
				{
					_dirtyTournamentPaths.Remove(removedYearPath);
				}
				break;

			case Division division:
				RemoveDivisionChildrenInMemory(division.Id);
				_divisions.RemoveAll(d => d.Id == division.Id);
				if (_divisionTournamentPath.TryGetValue(division.Id, out var removedDivPath) &&
					_divisionKeyById.TryGetValue(division.Id, out var removedDivKey))
				{
					_dirtyDivisionPaths.Remove((removedDivPath.Year, removedDivPath.Slug, removedDivKey));
				}
				_divisionKeyById.Remove(division.Id);
				_divisionTournamentPath.Remove(division.Id);
				MarkTournamentDirty(division.TournamentYearId);
				break;

			case Pool pool:
				_teams.RemoveAll(t => t.PoolId == pool.Id);
				_pools.RemoveAll(p => p.Id == pool.Id);
				MarkTournamentDirtyForDivision(pool.DivisionId);
				break;

			case Team team:
				_teams.RemoveAll(t => t.Id == team.Id);
				MarkTournamentDirtyForPool(team.PoolId);
				break;

			case PoolPlayGame poolPlayGame:
				_poolPlayGames.RemoveAll(g => g.Id == poolPlayGame.Id);
				foreach (var sr in _shootoutRounds.Where(r => r.PoolPlayGameId == poolPlayGame.Id))
				{
					sr.PoolPlayGame = null;
				}
				MarkDivisionDirty(poolPlayGame.DivisionId);
				break;

			case ChampionshipGame championshipGame:
				_championshipGames.RemoveAll(g => g.Id == championshipGame.Id);
				foreach (var sr in _shootoutRounds.Where(r => r.ChampionshipGameId == championshipGame.Id))
				{
					sr.ChampionshipGame = null;
				}
				MarkDivisionDirty(championshipGame.DivisionId);
				break;

			case ShootoutRound shootoutRound:
				_shootoutRounds.RemoveAll(r => r.Id == shootoutRound.Id);
				MarkDivisionDirty(shootoutRound.DivisionId);
				break;

			case TiebreakerOverride tiebreakerOverride:
				_tiebreakerOverrides.RemoveAll(o => o.Id == tiebreakerOverride.Id);
				MarkDivisionDirty(tiebreakerOverride.DivisionId);
				break;

			default:
				throw new NotSupportedException($"Entity type {typeof(T).Name} is not supported.");
		}

		WireNavigationProperties();
	}

	private void RemoveDivisionChildrenInMemory(int divisionId)
	{
		var poolIds = _pools.Where(p => p.DivisionId == divisionId).Select(p => p.Id).ToHashSet();
		_teams.RemoveAll(t => poolIds.Contains(t.PoolId));
		_pools.RemoveAll(p => p.DivisionId == divisionId);
		_poolPlayGames.RemoveAll(g => g.DivisionId == divisionId);
		_championshipGames.RemoveAll(g => g.DivisionId == divisionId);
		_shootoutRounds.RemoveAll(r => r.DivisionId == divisionId);
		_tiebreakerOverrides.RemoveAll(o => o.DivisionId == divisionId);
		_standingsByDivision.Remove(divisionId);
	}

	public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		EnsureLoaded();

		var currentUser = GetCurrentUserIdentifier();
		var now = DateTime.UtcNow;
		var changes = 0;

		if (_indexDirty)
		{
			await _store.SaveIndexAsync(_index, _indexStorage?.Generation, cancellationToken);
			_indexStorage = await _store.GetIndexAsync(cancellationToken);
			_index = _indexStorage.Content;
			_indexDirty = false;
			changes++;
		}

		foreach (var (year, slug) in _dirtyTournamentPaths.ToList())
		{
			var tournamentYearId = _tournamentPathByYearId
				.FirstOrDefault(kvp => kvp.Value.Year == year && string.Equals(kvp.Value.Slug, slug, StringComparison.OrdinalIgnoreCase))
				.Key;

			var tournamentYear = _tournamentYears.FirstOrDefault(y => y.Id == tournamentYearId);
			if (tournamentYear is null)
			{
				_dirtyTournamentPaths.Remove((year, slug));
				continue;
			}

			var dirPath = DivisionKeyHelper.GetTournamentDirectoryPath(year, slug);
			var existingStorage = _tournamentStorage.GetValueOrDefault(dirPath);
			var document = BuildTournamentDocument(tournamentYear, existingStorage?.Content, currentUser, now);

			await _store.SaveTournamentDocumentAsync(year, slug, document, existingStorage?.Generation, cancellationToken);
			_tournamentStorage[dirPath] = await _store.GetTournamentDocumentAsync(year, slug, cancellationToken);
			_dirtyTournamentPaths.Remove((year, slug));
			changes++;
		}

		foreach (var (year, slug, divisionKey) in _dirtyDivisionPaths.ToList())
		{
			var division = _divisions.FirstOrDefault(d =>
				_divisionTournamentPath.TryGetValue(d.Id, out var divPath) &&
				divPath.Year == year &&
				string.Equals(divPath.Slug, slug, StringComparison.OrdinalIgnoreCase) &&
				_divisionKeyById.TryGetValue(d.Id, out var key) &&
				string.Equals(key, divisionKey, StringComparison.OrdinalIgnoreCase));

			if (division is null)
			{
				_dirtyDivisionPaths.Remove((year, slug, divisionKey));
				continue;
			}

			var storageKey = GetDivisionStorageKey(year, slug, divisionKey);
			var existingStorage = _divisionStorage.GetValueOrDefault(storageKey);
			var document = BuildDivisionDataDocument(division.Id, divisionKey, existingStorage?.Content, currentUser, now);

			await _store.SaveDivisionDataAsync(year, slug, divisionKey, document, existingStorage?.Generation, cancellationToken);
			_divisionStorage[storageKey] = await _store.GetDivisionDataAsync(year, slug, divisionKey, cancellationToken);
			_dirtyDivisionPaths.Remove((year, slug, divisionKey));
			changes++;
		}

		return changes;
	}

	private TournamentDocument BuildTournamentDocument(
		TournamentYear tournamentYear,
		TournamentDocument? existing,
		string currentUser,
		DateTime now)
	{
		var divisionsForYear = _divisions
			.Where(d => d.TournamentYearId == tournamentYear.Id)
			.OrderBy(d => d.AgeGroup)
			.ThenBy(d => d.Gender)
			.ToList();

		var divisionDocs = new List<DivisionDocument>(divisionsForYear.Count);
		foreach (var division in divisionsForYear)
		{
			var divisionPools = _pools.Where(p => p.DivisionId == division.Id).ToList();
			var poolIds = divisionPools.Select(p => p.Id).ToHashSet();
			var divisionTeams = _teams.Where(t => poolIds.Contains(t.PoolId)).ToList();

			divisionDocs.Add(DocumentEntityMapper.ToDocument(division, divisionPools, divisionTeams));
		}

		return new TournamentDocument
		{
			SchemaVersion = TournamentDocument.CurrentSchemaVersion,
			Version = (existing?.Version ?? 0) + 1,
			ModifiedAt = now,
			ModifiedBy = currentUser,
			Tournament = DocumentEntityMapper.ToDocument(tournamentYear),
			Divisions = divisionDocs,
		};
	}

	private DivisionDataDocument BuildDivisionDataDocument(
		int divisionId,
		string divisionKey,
		DivisionDataDocument? existing,
		string currentUser,
		DateTime now)
	{
		return new DivisionDataDocument
		{
			SchemaVersion = DivisionDataDocument.CurrentSchemaVersion,
			Version = (existing?.Version ?? 0) + 1,
			DivisionKey = divisionKey,
			DivisionId = divisionId,
			ModifiedAt = now,
			ModifiedBy = currentUser,
			PoolPlayGames = _poolPlayGames
				.Where(g => g.DivisionId == divisionId)
				.OrderBy(g => g.Round)
				.ThenBy(g => g.ScheduledDateTime)
				.ThenBy(g => g.Id)
				.Select(DocumentEntityMapper.ToDocument)
				.ToList(),
			ChampionshipGames = _championshipGames
				.Where(g => g.DivisionId == divisionId)
				.OrderBy(g => g.Slot)
				.ThenBy(g => g.ScheduledDateTime)
				.ThenBy(g => g.Id)
				.Select(DocumentEntityMapper.ToDocument)
				.ToList(),
			ShootoutRounds = _shootoutRounds
				.Where(r => r.DivisionId == divisionId)
				.OrderBy(r => r.Round)
				.ThenBy(r => r.Id)
				.Select(DocumentEntityMapper.ToDocument)
				.ToList(),
			TiebreakerOverrides = _tiebreakerOverrides
				.Where(o => o.DivisionId == divisionId)
				.OrderBy(o => o.Scope)
				.ThenBy(o => o.Id)
				.Select(DocumentEntityMapper.ToDocument)
				.ToList(),
			Standings = _standingsByDivision.TryGetValue(divisionId, out var standings)
				? CloneStandingsSnapshot(standings)
				: null,
		};
	}

	public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
	{
		EnsureLoaded();

		if (_activeTransaction is not null)
		{
			throw new InvalidOperationException("A transaction is already in progress on this data context.");
		}

		_transactionSnapshot = CaptureSnapshot();
		_activeTransaction = new JsonDataTransaction(this);
		return Task.FromResult<ITransaction>(_activeTransaction);
	}

	internal void CommitTransaction()
	{
		_transactionSnapshot = null;
		_activeTransaction = null;
	}

	internal void RollbackTransaction()
	{
		if (_transactionSnapshot is not null)
		{
			RestoreSnapshot(_transactionSnapshot);
			_transactionSnapshot = null;
		}

		_activeTransaction = null;
	}

	private JsonDataTransactionSnapshot CaptureSnapshot()
	{
		return new JsonDataTransactionSnapshot
		{
			TournamentYears = _tournamentYears.Select(CloneTournamentYear).ToList(),
			Divisions = _divisions.Select(CloneDivision).ToList(),
			Pools = _pools.Select(ClonePool).ToList(),
			Teams = _teams.Select(CloneTeam).ToList(),
			PoolPlayGames = _poolPlayGames.Select(ClonePoolPlayGame).ToList(),
			ChampionshipGames = _championshipGames.Select(CloneChampionshipGame).ToList(),
			ShootoutRounds = _shootoutRounds.Select(CloneShootoutRound).ToList(),
			TiebreakerOverrides = _tiebreakerOverrides.Select(CloneTiebreakerOverride).ToList(),
			StandingsByDivision = _standingsByDivision.ToDictionary(
				kvp => kvp.Key,
				kvp => CloneStandingsSnapshot(kvp.Value)),
			Index = new TournamentIndexDocument
			{
				SchemaVersion = _index.SchemaVersion,
				Tournaments = _index.Tournaments.Select(t => new TournamentIndexEntryDocument
				{
					TournamentYearId = t.TournamentYearId,
					Year = t.Year,
					Slug = t.Slug,
					Name = t.Name,
					IsActive = t.IsActive,
					TournamentPath = t.TournamentPath,
				}).ToList(),
			},
			TournamentPathByYearId = new Dictionary<int, (int Year, string Slug)>(_tournamentPathByYearId),
			DivisionKeyById = new Dictionary<int, string>(_divisionKeyById),
			DivisionTournamentPath = new Dictionary<int, (int Year, string Slug)>(_divisionTournamentPath),
			DirtyTournamentPaths = new HashSet<(int Year, string Slug)>(_dirtyTournamentPaths),
			DirtyDivisionPaths = new HashSet<(int Year, string Slug, string DivisionKey)>(_dirtyDivisionPaths),
			IndexDirty = _indexDirty,
			NextId = _nextId,
		};
	}

	private void RestoreSnapshot(JsonDataTransactionSnapshot snapshot)
	{
		_tournamentYears.Clear();
		_tournamentYears.AddRange(snapshot.TournamentYears);

		_divisions.Clear();
		_divisions.AddRange(snapshot.Divisions);

		_pools.Clear();
		_pools.AddRange(snapshot.Pools);

		_teams.Clear();
		_teams.AddRange(snapshot.Teams);

		_poolPlayGames.Clear();
		_poolPlayGames.AddRange(snapshot.PoolPlayGames);

		_championshipGames.Clear();
		_championshipGames.AddRange(snapshot.ChampionshipGames);

		_shootoutRounds.Clear();
		_shootoutRounds.AddRange(snapshot.ShootoutRounds);

		_tiebreakerOverrides.Clear();
		_tiebreakerOverrides.AddRange(snapshot.TiebreakerOverrides);

		_standingsByDivision.Clear();
		foreach (var (divisionId, standings) in snapshot.StandingsByDivision)
		{
			_standingsByDivision[divisionId] = CloneStandingsSnapshot(standings);
		}

		_index = snapshot.Index;

		_tournamentPathByYearId.Clear();
		foreach (var kvp in snapshot.TournamentPathByYearId)
		{
			_tournamentPathByYearId[kvp.Key] = kvp.Value;
		}

		_divisionKeyById.Clear();
		foreach (var kvp in snapshot.DivisionKeyById)
		{
			_divisionKeyById[kvp.Key] = kvp.Value;
		}

		_divisionTournamentPath.Clear();
		foreach (var kvp in snapshot.DivisionTournamentPath)
		{
			_divisionTournamentPath[kvp.Key] = kvp.Value;
		}

		_dirtyTournamentPaths.Clear();
		foreach (var path in snapshot.DirtyTournamentPaths)
		{
			_dirtyTournamentPaths.Add(path);
		}

		_dirtyDivisionPaths.Clear();
		foreach (var path in snapshot.DirtyDivisionPaths)
		{
			_dirtyDivisionPaths.Add(path);
		}

		_indexDirty = snapshot.IndexDirty;
		_nextId = snapshot.NextId;

		WireNavigationProperties();
	}

	private void SyncIndexEntry(TournamentYear tournamentYear, int year, string slug, bool markDirty)
	{
		var entry = _index.Tournaments.FirstOrDefault(t => t.TournamentYearId == tournamentYear.Id);
		if (entry is null)
		{
			entry = new TournamentIndexEntryDocument();
			_index.Tournaments.Add(entry);
		}

		entry.TournamentYearId = tournamentYear.Id;
		entry.Year = year;
		entry.Slug = slug;
		entry.Name = tournamentYear.Name;
		entry.IsActive = tournamentYear.IsActive;
		entry.TournamentPath = DivisionKeyHelper.GetTournamentObjectPath(year, slug);

		if (markDirty)
		{
			_indexDirty = true;
		}
	}

	private void MarkTournamentDirty(int tournamentYearId)
	{
		if (_tournamentPathByYearId.TryGetValue(tournamentYearId, out var path))
		{
			_dirtyTournamentPaths.Add(path);
		}
	}

	private void MarkTournamentDirtyForDivision(int divisionId)
	{
		var division = _divisions.FirstOrDefault(d => d.Id == divisionId);
		if (division is not null)
		{
			MarkTournamentDirty(division.TournamentYearId);
		}
	}

	private void MarkTournamentDirtyForPool(int poolId)
	{
		var pool = _pools.FirstOrDefault(p => p.Id == poolId);
		if (pool is not null)
		{
			MarkTournamentDirtyForDivision(pool.DivisionId);
		}
	}

	private void MarkDivisionDirty(int divisionId)
	{
		if (_divisionTournamentPath.TryGetValue(divisionId, out var tournamentPath) &&
			_divisionKeyById.TryGetValue(divisionId, out var divisionKey))
		{
			_dirtyDivisionPaths.Add((tournamentPath.Year, tournamentPath.Slug, divisionKey));
		}
	}

	private void ApplyAuditFields(AuditableEntity entity, bool isNew)
	{
		var currentUser = GetCurrentUserIdentifier();
		var now = DateTime.UtcNow;

		if (isNew)
		{
			entity.CreatedAt = now;
			entity.CreatedBy = currentUser;
			entity.ModifiedAt = now;
			entity.ModifiedBy = currentUser;
			entity.RowVersion = 1;
		}
		else
		{
			entity.ModifiedAt = now;
			entity.ModifiedBy = currentUser;
			entity.RowVersion++;
		}
	}

	private string GetCurrentUserIdentifier()
	{
		return string.IsNullOrWhiteSpace(_currentUserService.UserIdentifier)
			? "System"
			: _currentUserService.UserIdentifier;
	}

	private void EnsureLoaded()
	{
		if (!_isLoaded)
		{
			LoadAllAsync().GetAwaiter().GetResult();
		}
	}

	private void RecalculateNextId()
	{
		var maxId = 0;
		maxId = Math.Max(maxId, MaxIdOrZero(_tournamentYears));
		maxId = Math.Max(maxId, MaxIdOrZero(_divisions));
		maxId = Math.Max(maxId, MaxIdOrZero(_pools));
		maxId = Math.Max(maxId, MaxIdOrZero(_teams));
		maxId = Math.Max(maxId, MaxIdOrZero(_poolPlayGames));
		maxId = Math.Max(maxId, MaxIdOrZero(_championshipGames));
		maxId = Math.Max(maxId, MaxIdOrZero(_shootoutRounds));
		maxId = Math.Max(maxId, MaxIdOrZero(_tiebreakerOverrides));
		_nextId = Math.Max(_nextId, maxId + 1);
	}

	private static int MaxIdOrZero<T>(List<T> entities) where T : AuditableEntity
	{
		var max = 0;
		foreach (var entity in entities)
		{
			if (entity.Id > max)
			{
				max = entity.Id;
			}
		}

		return max;
	}

	private void WireNavigationProperties()
	{
		var yearsById = _tournamentYears.ToDictionary(y => y.Id);
		var divisionsById = _divisions.ToDictionary(d => d.Id);
		var poolsById = _pools.ToDictionary(p => p.Id);
		var teamsById = _teams.ToDictionary(t => t.Id);
		var poolGamesById = _poolPlayGames.ToDictionary(g => g.Id);
		var champGamesById = _championshipGames.ToDictionary(g => g.Id);

		foreach (var year in _tournamentYears)
		{
			year.Divisions = new List<Division>();
		}

		foreach (var division in _divisions)
		{
			division.Pools = new List<Pool>();
			if (yearsById.TryGetValue(division.TournamentYearId, out var year))
			{
				division.TournamentYear = year;
				year.Divisions.Add(division);
			}
			else
			{
				division.TournamentYear = null;
			}
		}

		foreach (var pool in _pools)
		{
			pool.Teams = new List<Team>();
			if (divisionsById.TryGetValue(pool.DivisionId, out var division))
			{
				pool.Division = division;
				division.Pools.Add(pool);
			}
			else
			{
				pool.Division = null;
			}
		}

		foreach (var team in _teams)
		{
			if (poolsById.TryGetValue(team.PoolId, out var pool))
			{
				team.Pool = pool;
				pool.Teams.Add(team);
			}
			else
			{
				team.Pool = null;
			}
		}

		foreach (var game in _poolPlayGames)
		{
			divisionsById.TryGetValue(game.DivisionId, out var division);
			poolsById.TryGetValue(game.PoolId, out var pool);
			teamsById.TryGetValue(game.HomeTeamId, out var homeTeam);
			teamsById.TryGetValue(game.AwayTeamId, out var awayTeam);

			game.Division = division;
			game.Pool = pool;
			game.HomeTeam = homeTeam;
			game.AwayTeam = awayTeam;
			game.ShootoutRound = null;
		}

		foreach (var champGame in _championshipGames)
		{
			divisionsById.TryGetValue(champGame.DivisionId, out var division);
			Team? homeTeam = null;
			if (champGame.HomeTeamId.HasValue)
			{
				teamsById.TryGetValue(champGame.HomeTeamId.Value, out homeTeam);
			}

			Team? awayTeam = null;
			if (champGame.AwayTeamId.HasValue)
			{
				teamsById.TryGetValue(champGame.AwayTeamId.Value, out awayTeam);
			}

			champGame.Division = division;
			champGame.HomeTeam = homeTeam;
			champGame.AwayTeam = awayTeam;
			champGame.ShootoutRound = null;
		}

		foreach (var round in _shootoutRounds)
		{
			divisionsById.TryGetValue(round.DivisionId, out var division);
			round.Division = division;

			if (round.PoolPlayGameId.HasValue && poolGamesById.TryGetValue(round.PoolPlayGameId.Value, out var poolGame))
			{
				round.PoolPlayGame = poolGame;
				poolGame.ShootoutRound = round;
			}
			else
			{
				round.PoolPlayGame = null;
			}

			if (round.ChampionshipGameId.HasValue && champGamesById.TryGetValue(round.ChampionshipGameId.Value, out var champGame))
			{
				round.ChampionshipGame = champGame;
				champGame.ShootoutRound = round;
			}
			else
			{
				round.ChampionshipGame = null;
			}

			Team? homeTeam = null;
			if (round.HomeTeamId.HasValue)
			{
				teamsById.TryGetValue(round.HomeTeamId.Value, out homeTeam);
			}

			Team? awayTeam = null;
			if (round.AwayTeamId.HasValue)
			{
				teamsById.TryGetValue(round.AwayTeamId.Value, out awayTeam);
			}

			round.HomeTeam = homeTeam;
			round.AwayTeam = awayTeam;
		}

		foreach (var overrideEntity in _tiebreakerOverrides)
		{
			divisionsById.TryGetValue(overrideEntity.DivisionId, out var division);
			teamsById.TryGetValue(overrideEntity.TeamAId, out var teamA);
			teamsById.TryGetValue(overrideEntity.TeamBId, out var teamB);
			teamsById.TryGetValue(overrideEntity.WinningTeamId, out var winningTeam);

			overrideEntity.Division = division;
			overrideEntity.TeamA = teamA;
			overrideEntity.TeamB = teamB;
			overrideEntity.WinningTeam = winningTeam;
		}
	}

	private static void ReplaceOrAdd<T>(List<T> list, T entity) where T : AuditableEntity
	{
		list.RemoveAll(e => e.Id == entity.Id);
		list.Add(entity);
	}

	private static DivisionStandingsSnapshotDocument CloneStandingsSnapshot(DivisionStandingsSnapshotDocument standings)
	{
		return TournamentJsonSerializer.Deserialize<DivisionStandingsSnapshotDocument>(
			TournamentJsonSerializer.Serialize(standings));
	}

	private static TournamentYear CloneTournamentYear(TournamentYear entity) => new()
	{
		Id = entity.Id,
		Year = entity.Year,
		Name = entity.Name,
		IsActive = entity.IsActive,
		CustomMessage = entity.CustomMessage,
		CreatedAt = entity.CreatedAt,
		CreatedBy = entity.CreatedBy,
		ModifiedAt = entity.ModifiedAt,
		ModifiedBy = entity.ModifiedBy,
		RowVersion = entity.RowVersion,
	};

	private static Division CloneDivision(Division entity) => new()
	{
		Id = entity.Id,
		TournamentYearId = entity.TournamentYearId,
		AgeGroup = entity.AgeGroup,
		Gender = entity.Gender,
		Name = entity.Name,
		Size = entity.Size,
		CustomMessage = entity.CustomMessage,
		CreatedAt = entity.CreatedAt,
		CreatedBy = entity.CreatedBy,
		ModifiedAt = entity.ModifiedAt,
		ModifiedBy = entity.ModifiedBy,
		RowVersion = entity.RowVersion,
	};

	private static Pool ClonePool(Pool entity) => new()
	{
		Id = entity.Id,
		DivisionId = entity.DivisionId,
		Name = entity.Name,
		CreatedAt = entity.CreatedAt,
		CreatedBy = entity.CreatedBy,
		ModifiedAt = entity.ModifiedAt,
		ModifiedBy = entity.ModifiedBy,
		RowVersion = entity.RowVersion,
	};

	private static Team CloneTeam(Team entity) => new()
	{
		Id = entity.Id,
		PoolId = entity.PoolId,
		Name = entity.Name,
		TeamNumber = entity.TeamNumber,
		CreatedAt = entity.CreatedAt,
		CreatedBy = entity.CreatedBy,
		ModifiedAt = entity.ModifiedAt,
		ModifiedBy = entity.ModifiedBy,
		RowVersion = entity.RowVersion,
	};

	private static PoolPlayGame ClonePoolPlayGame(PoolPlayGame entity) => new()
	{
		Id = entity.Id,
		DivisionId = entity.DivisionId,
		PoolId = entity.PoolId,
		Round = entity.Round,
		ScheduledDateTime = entity.ScheduledDateTime,
		Location = entity.Location,
		HomeTeamId = entity.HomeTeamId,
		AwayTeamId = entity.AwayTeamId,
		HomeGoals = entity.HomeGoals,
		AwayGoals = entity.AwayGoals,
		IsForfeit = entity.IsForfeit,
		HomeYellowCards = entity.HomeYellowCards,
		HomeRedCards = entity.HomeRedCards,
		AwayYellowCards = entity.AwayYellowCards,
		AwayRedCards = entity.AwayRedCards,
		CreatedAt = entity.CreatedAt,
		CreatedBy = entity.CreatedBy,
		ModifiedAt = entity.ModifiedAt,
		ModifiedBy = entity.ModifiedBy,
		RowVersion = entity.RowVersion,
	};

	private static ChampionshipGame CloneChampionshipGame(ChampionshipGame entity) => new()
	{
		Id = entity.Id,
		DivisionId = entity.DivisionId,
		Slot = entity.Slot,
		ScheduledDateTime = entity.ScheduledDateTime,
		Location = entity.Location,
		HomeSourceDescription = entity.HomeSourceDescription,
		AwaySourceDescription = entity.AwaySourceDescription,
		HomeTeamId = entity.HomeTeamId,
		AwayTeamId = entity.AwayTeamId,
		HomeGoals = entity.HomeGoals,
		AwayGoals = entity.AwayGoals,
		IsForfeit = entity.IsForfeit,
		CreatedAt = entity.CreatedAt,
		CreatedBy = entity.CreatedBy,
		ModifiedAt = entity.ModifiedAt,
		ModifiedBy = entity.ModifiedBy,
		RowVersion = entity.RowVersion,
	};

	private static ShootoutRound CloneShootoutRound(ShootoutRound entity) => new()
	{
		Id = entity.Id,
		DivisionId = entity.DivisionId,
		Round = entity.Round,
		PoolPlayGameId = entity.PoolPlayGameId,
		ChampionshipGameId = entity.ChampionshipGameId,
		HomeTeamId = entity.HomeTeamId,
		AwayTeamId = entity.AwayTeamId,
		HomePkGoals = entity.HomePkGoals,
		AwayPkGoals = entity.AwayPkGoals,
		CreatedAt = entity.CreatedAt,
		CreatedBy = entity.CreatedBy,
		ModifiedAt = entity.ModifiedAt,
		ModifiedBy = entity.ModifiedBy,
		RowVersion = entity.RowVersion,
	};

	private static TiebreakerOverride CloneTiebreakerOverride(TiebreakerOverride entity) => new()
	{
		Id = entity.Id,
		DivisionId = entity.DivisionId,
		Scope = entity.Scope,
		TeamAId = entity.TeamAId,
		TeamBId = entity.TeamBId,
		WinningTeamId = entity.WinningTeamId,
		CreatedAt = entity.CreatedAt,
		CreatedBy = entity.CreatedBy,
		ModifiedAt = entity.ModifiedAt,
		ModifiedBy = entity.ModifiedBy,
		RowVersion = entity.RowVersion,
	};
}

internal sealed class JsonDataTransaction : ITransaction
{
	private readonly TournamentDataContext _context;
	private bool _completed;

	public JsonDataTransaction(TournamentDataContext context)
	{
		_context = context;
	}

	public Task CommitAsync(CancellationToken cancellationToken = default)
	{
		_completed = true;
		_context.CommitTransaction();
		return Task.CompletedTask;
	}

	public Task RollbackAsync(CancellationToken cancellationToken = default)
	{
		_completed = true;
		_context.RollbackTransaction();
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		if (!_completed)
		{
			_completed = true;
			_context.RollbackTransaction();
		}

		return ValueTask.CompletedTask;
	}
}

internal sealed class JsonDataTransactionSnapshot
{
	public List<TournamentYear> TournamentYears { get; init; } = new();

	public List<Division> Divisions { get; init; } = new();

	public List<Pool> Pools { get; init; } = new();

	public List<Team> Teams { get; init; } = new();

	public List<PoolPlayGame> PoolPlayGames { get; init; } = new();

	public List<ChampionshipGame> ChampionshipGames { get; init; } = new();

	public List<ShootoutRound> ShootoutRounds { get; init; } = new();

	public List<TiebreakerOverride> TiebreakerOverrides { get; init; } = new();

	public Dictionary<int, DivisionStandingsSnapshotDocument> StandingsByDivision { get; init; } = new();

	public TournamentIndexDocument Index { get; init; } = new();

	public Dictionary<int, (int Year, string Slug)> TournamentPathByYearId { get; init; } = new();

	public Dictionary<int, string> DivisionKeyById { get; init; } = new();

	public Dictionary<int, (int Year, string Slug)> DivisionTournamentPath { get; init; } = new();

	public HashSet<(int Year, string Slug)> DirtyTournamentPaths { get; init; } = new();

	public HashSet<(int Year, string Slug, string DivisionKey)> DirtyDivisionPaths { get; init; } = new();

	public bool IndexDirty { get; init; }

	public int NextId { get; init; }
}
