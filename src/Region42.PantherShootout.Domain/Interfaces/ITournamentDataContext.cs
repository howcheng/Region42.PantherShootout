using Region42.PantherShootout.Domain.Common;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Domain.Interfaces;

/// <summary>
/// Scoped in-memory tournament data context that hydrates domain entities from JSON documents,
/// tracks dirty files per tournament year and division, manages snapshot transactions, and flushes
/// changes back to <see cref="ITournamentDataStore"/>.
/// </summary>
public interface ITournamentDataContext
{
	TournamentIndexDocument Index { get; }

	IReadOnlyList<TournamentYear> TournamentYears { get; }

	IReadOnlyList<Division> Divisions { get; }

	IReadOnlyList<Pool> Pools { get; }

	IReadOnlyList<Team> Teams { get; }

	IReadOnlyList<PoolPlayGame> PoolPlayGames { get; }

	IReadOnlyList<ChampionshipGame> ChampionshipGames { get; }

	IReadOnlyList<ShootoutRound> ShootoutRounds { get; }

	IReadOnlyList<TiebreakerOverride> TiebreakerOverrides { get; }

	Task LoadAllAsync(CancellationToken cancellationToken = default);

	Task LoadTournamentYearAsync(int year, string competitionSlug, CancellationToken cancellationToken = default);

	Task LoadDivisionDataAsync(int divisionId, CancellationToken cancellationToken = default);

	DivisionStandingsSnapshotDocument? GetStandingsForDivision(int divisionId);

	void SetStandingsForDivision(int divisionId, DivisionStandingsSnapshotDocument standings);

	(int Year, string Slug)? GetTournamentPathForYear(int tournamentYearId);

	string? GetDivisionKey(int divisionId);

	int AllocateNextId();

	void Add<T>(T entity) where T : AuditableEntity;

	void Update<T>(T entity) where T : AuditableEntity;

	void Remove<T>(T entity) where T : AuditableEntity;

	Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

	Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
