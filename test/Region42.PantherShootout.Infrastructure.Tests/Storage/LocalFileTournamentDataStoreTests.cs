using Microsoft.Extensions.Options;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Infrastructure.Storage;
using Xunit;

namespace Region42.PantherShootout.Infrastructure.Tests.Storage;

public sealed class LocalFileTournamentDataStoreTests : IDisposable
{
	private readonly string _tempRoot;
	private readonly LocalFileTournamentDataStore _store;

	public LocalFileTournamentDataStoreTests()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), "region42-pso-infra-tests", Guid.NewGuid().ToString("N"));
		_store = new LocalFileTournamentDataStore(Options.Create(new StorageOptions
		{
			LocalRoot = _tempRoot,
			CompetitionSlug = "panther-shootout",
		}));
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempRoot))
		{
			Directory.Delete(_tempRoot, recursive: true);
		}
	}

	[Fact]
	public async Task SaveAndGetIndexAsync_RoundTripsUnderIsolatedSlugWithoutUtf8Bom()
	{
		var index = new TournamentIndexDocument
		{
			Tournaments =
			[
				new TournamentIndexEntryDocument
				{
					TournamentYearId = 1,
					Year = 2026,
					Slug = "panther-shootout",
					Name = "2026 Panther Shootout",
					IsActive = true,
					TournamentPath = "2026/panther-shootout/tournament.json",
				},
			],
		};

		await _store.SaveIndexAsync(index, expectedGeneration: null);
		var loaded = await _store.GetIndexAsync();

		Assert.Equal("panther-shootout/index.json", loaded.ObjectPath);
		Assert.Equal(1L, loaded.Generation);
		Assert.Single(loaded.Content.Tournaments);
		Assert.Equal("2026 Panther Shootout", loaded.Content.Tournaments[0].Name);

		var expectedFilePath = Path.Combine(_tempRoot, "panther-shootout", "index.json");
		Assert.True(File.Exists(expectedFilePath));
		Assert.False(File.Exists(Path.Combine(_tempRoot, "index.json")));

		var rawBytes = await File.ReadAllBytesAsync(expectedFilePath);
		Assert.False(
			rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF,
			"JSON storage files must be UTF-8 without BOM.");

		var rawJson = TournamentJsonSerializer.Utf8WithoutBom.GetString(rawBytes);
		Assert.Contains("\"schemaVersion\": 1", rawJson, StringComparison.Ordinal);
		Assert.Contains("\"tournaments\":", rawJson, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SaveAndGetTournamentAndDivisionDocuments_IncrementsGenerationsAndRoundTrips()
	{
		var tournamentDoc = new TournamentDocument
		{
			Version = 1,
			ModifiedBy = "admin@aysoregion42.org",
			Tournament = new TournamentInfoDocument
			{
				Id = 1,
				Year = 2026,
				Name = "2026 Panther Shootout",
				IsActive = true,
			},
			Divisions =
			[
				new DivisionDocument
				{
					Id = 10,
					Key = "10u-girls",
					Name = "10U Girls",
					AgeGroup = "10U",
					Gender = "Girls",
					Size = 6,
				},
			],
		};

		await _store.SaveTournamentDocumentAsync(2026, "panther-shootout", tournamentDoc, expectedGeneration: null);
		var loadedTournament = await _store.GetTournamentDocumentAsync(2026, "panther-shootout");

		Assert.Equal("2026/panther-shootout/tournament.json", loadedTournament.ObjectPath);
		Assert.Equal(1L, loadedTournament.Generation);
		Assert.Single(loadedTournament.Content.Divisions);

		tournamentDoc.Version = 2;
		await _store.SaveTournamentDocumentAsync(2026, "panther-shootout", tournamentDoc, expectedGeneration: 1L);
		var updatedTournament = await _store.GetTournamentDocumentAsync(2026, "panther-shootout");
		Assert.Equal(2L, updatedTournament.Generation);
		Assert.Equal(2, updatedTournament.Content.Version);

		var divisionData = new DivisionDataDocument
		{
			DivisionId = 10,
			DivisionKey = "10u-girls",
			PoolPlayGames =
			[
				new PoolPlayGameDocument
				{
					Id = 100,
					PoolId = 1,
					Round = 1,
					HomeTeamId = 1,
					AwayTeamId = 2,
					HomeGoals = 3,
					AwayGoals = 1,
				},
			],
		};

		await _store.SaveDivisionDataAsync(2026, "panther-shootout", "10u-girls", divisionData, expectedGeneration: null);
		var loadedDivision = await _store.GetDivisionDataAsync(2026, "panther-shootout", "10u-girls");

		Assert.Equal("2026/panther-shootout/10u-girls.json", loadedDivision.ObjectPath);
		Assert.Equal(1L, loadedDivision.Generation);
		Assert.Single(loadedDivision.Content.PoolPlayGames);
		Assert.Equal(3, loadedDivision.Content.PoolPlayGames[0].HomeGoals);
	}

	[Fact]
	public async Task SaveIndexAsync_WithStaleOrCreateOnlyGeneration_ThrowsStorageConcurrencyException()
	{
		var index = new TournamentIndexDocument();

		// expectedGeneration: 0 succeeds when the object does not exist yet
		await _store.SaveIndexAsync(index, expectedGeneration: 0L);

		// expectedGeneration: 0 fails once the object already exists
		var createAgainEx = await Assert.ThrowsAsync<StorageConcurrencyException>(
			() => _store.SaveIndexAsync(index, expectedGeneration: 0L));
		Assert.Equal("panther-shootout/index.json", createAgainEx.ObjectPath);

		// Advance to generation 2
		await _store.SaveIndexAsync(index, expectedGeneration: 1L);

		// Stale generation 1 fails
		var staleEx = await Assert.ThrowsAsync<StorageConcurrencyException>(
			() => _store.SaveIndexAsync(index, expectedGeneration: 1L));
		Assert.Equal("panther-shootout/index.json", staleEx.ObjectPath);
	}

	[Fact]
	public async Task GetMethods_WhenObjectMissing_ThrowFileNotFoundException()
	{
		await Assert.ThrowsAsync<FileNotFoundException>(() => _store.GetIndexAsync());
		await Assert.ThrowsAsync<FileNotFoundException>(() => _store.GetTournamentDocumentAsync(2026, "panther-shootout"));
		await Assert.ThrowsAsync<FileNotFoundException>(() => _store.GetDivisionDataAsync(2026, "panther-shootout", "10u-boys"));
	}

	[Fact]
	public async Task ListTournamentPathsAsync_ReturnsOnlyTournamentDirectories_IgnoringScoresStandingsFiles()
	{
		await _store.SaveTournamentDocumentAsync(2026, "panther-shootout", new TournamentDocument(), expectedGeneration: null);
		await _store.SaveTournamentDocumentAsync(2025, "panther-shootout", new TournamentDocument(), expectedGeneration: null);

		// Simulate coexisting Region42.ScoresStandings files in the same bucket root
		var siblingSeasonDir = Path.Combine(_tempRoot, "2026", "core-season");
		Directory.CreateDirectory(siblingSeasonDir);
		await File.WriteAllTextAsync(Path.Combine(siblingSeasonDir, "season.json"), "{}");
		await File.WriteAllTextAsync(Path.Combine(_tempRoot, "index.json"), "{}");

		var paths = await _store.ListTournamentPathsAsync();

		Assert.Equal(["2025/panther-shootout", "2026/panther-shootout"], paths);
	}
}
