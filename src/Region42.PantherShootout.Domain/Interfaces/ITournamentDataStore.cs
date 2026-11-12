using Region42.PantherShootout.Domain.Documents;

namespace Region42.PantherShootout.Domain.Interfaces;

/// <summary>
/// Low-level JSON document persistence abstraction (implemented over local files in development
/// and Google Cloud Storage in production).
/// </summary>
public interface ITournamentDataStore
{
	Task<StorageDocument<TournamentIndexDocument>> GetIndexAsync(CancellationToken cancellationToken = default);

	Task SaveIndexAsync(TournamentIndexDocument document, long? expectedGeneration, CancellationToken cancellationToken = default);

	Task<StorageDocument<RegionsDocument>> GetRegionsDocumentAsync(CancellationToken cancellationToken = default);

	Task SaveRegionsDocumentAsync(RegionsDocument document, long? expectedGeneration, CancellationToken cancellationToken = default);

	Task<StorageDocument<TournamentDocument>> GetTournamentDocumentAsync(int year, string competitionSlug, CancellationToken cancellationToken = default);

	Task SaveTournamentDocumentAsync(int year, string competitionSlug, TournamentDocument document, long? expectedGeneration, CancellationToken cancellationToken = default);

	Task<StorageDocument<DivisionDataDocument>> GetDivisionDataAsync(int year, string competitionSlug, string divisionKey, CancellationToken cancellationToken = default);

	Task SaveDivisionDataAsync(int year, string competitionSlug, string divisionKey, DivisionDataDocument document, long? expectedGeneration, CancellationToken cancellationToken = default);

	Task<IReadOnlyList<string>> ListTournamentPathsAsync(CancellationToken cancellationToken = default);
}
