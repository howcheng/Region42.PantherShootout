using System.Globalization;
using Microsoft.Extensions.Options;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Infrastructure.Storage;

/// <summary>
/// Local filesystem implementation of <see cref="ITournamentDataStore"/> that mirrors the GCS bucket
/// layout on disk using atomic <c>.tmp</c> renames and <c>.generation</c> sidecar files.
/// </summary>
public class LocalFileTournamentDataStore : ITournamentDataStore
{
	private const string GenerationSuffix = ".generation";

	private readonly string _rootPath;
	private readonly string _competitionSlug;
	private readonly object _syncRoot = new();

	public LocalFileTournamentDataStore(IOptions<StorageOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var opts = options.Value;
		_rootPath = Path.GetFullPath(opts.LocalRoot);
		_competitionSlug = string.IsNullOrWhiteSpace(opts.CompetitionSlug)
			? DivisionKeyHelper.DefaultCompetitionSlug
			: opts.CompetitionSlug;

		Directory.CreateDirectory(_rootPath);
	}

	public Task<StorageDocument<TournamentIndexDocument>> GetIndexAsync(CancellationToken cancellationToken = default)
	{
		var path = DivisionKeyHelper.GetIndexObjectPath(_competitionSlug);
		return Task.FromResult(ReadDocument<TournamentIndexDocument>(path));
	}

	private StorageDocument<T> ReadDocument<T>(string objectPath)
	{
		lock (_syncRoot)
		{
			var fullPath = GetFullPath(objectPath);
			if (!File.Exists(fullPath))
			{
				throw new FileNotFoundException($"Storage object not found: {objectPath}", fullPath);
			}

			var json = File.ReadAllText(fullPath, TournamentJsonSerializer.Utf8WithoutBom);
			var generation = ReadGeneration(objectPath);

			return new StorageDocument<T>
			{
				Content = TournamentJsonSerializer.Deserialize<T>(json),
				Generation = generation,
				ObjectPath = objectPath,
			};
		}
	}

	private long? ReadGeneration(string objectPath)
	{
		var generationPath = GetGenerationPath(objectPath);
		if (!File.Exists(generationPath))
		{
			return File.Exists(GetFullPath(objectPath)) ? 1L : null;
		}

		var raw = File.ReadAllText(generationPath, TournamentJsonSerializer.Utf8WithoutBom);
		return long.Parse(raw.Trim(), CultureInfo.InvariantCulture);
	}

	private string GetFullPath(string objectPath)
	{
		return Path.Combine(_rootPath, objectPath.Replace('/', Path.DirectorySeparatorChar));
	}

	private string GetGenerationPath(string objectPath)
	{
		return GetFullPath(objectPath + GenerationSuffix);
	}

	public Task SaveIndexAsync(TournamentIndexDocument document, long? expectedGeneration, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(document);

		var path = DivisionKeyHelper.GetIndexObjectPath(_competitionSlug);
		WriteDocument(path, document, expectedGeneration);
		return Task.CompletedTask;
	}

	public Task<StorageDocument<RegionsDocument>> GetRegionsDocumentAsync(CancellationToken cancellationToken = default)
	{
		var path = DivisionKeyHelper.GetRegionsObjectPath(_competitionSlug);
		return Task.FromResult(ReadDocument<RegionsDocument>(path));
	}

	public Task SaveRegionsDocumentAsync(RegionsDocument document, long? expectedGeneration, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(document);

		var path = DivisionKeyHelper.GetRegionsObjectPath(_competitionSlug);
		WriteDocument(path, document, expectedGeneration);
		return Task.CompletedTask;
	}

	private void WriteDocument<T>(string objectPath, T document, long? expectedGeneration)
	{
		lock (_syncRoot)
		{
			var fullPath = GetFullPath(objectPath);
			var generationPath = GetGenerationPath(objectPath);
			Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

			var currentGeneration = ReadGeneration(objectPath);
			if (expectedGeneration.HasValue)
			{
				var matches = expectedGeneration.Value == 0
					? currentGeneration is null
					: currentGeneration == expectedGeneration.Value;

				if (!matches)
				{
					throw new StorageConcurrencyException(objectPath);
				}
			}

			var nextGeneration = (currentGeneration ?? 0L) + 1L;
			var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";

			File.WriteAllText(tempPath, TournamentJsonSerializer.Serialize(document), TournamentJsonSerializer.Utf8WithoutBom);
			File.Move(tempPath, fullPath, overwrite: true);
			File.WriteAllText(generationPath, nextGeneration.ToString(CultureInfo.InvariantCulture), TournamentJsonSerializer.Utf8WithoutBom);
		}
	}

	public Task<StorageDocument<TournamentDocument>> GetTournamentDocumentAsync(int year, string competitionSlug, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);

		var path = DivisionKeyHelper.GetTournamentObjectPath(year, competitionSlug);
		return Task.FromResult(ReadDocument<TournamentDocument>(path));
	}

	public Task SaveTournamentDocumentAsync(int year, string competitionSlug, TournamentDocument document, long? expectedGeneration, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
		ArgumentNullException.ThrowIfNull(document);

		var path = DivisionKeyHelper.GetTournamentObjectPath(year, competitionSlug);
		WriteDocument(path, document, expectedGeneration);
		return Task.CompletedTask;
	}

	public Task<StorageDocument<DivisionDataDocument>> GetDivisionDataAsync(int year, string competitionSlug, string divisionKey, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
		ArgumentException.ThrowIfNullOrWhiteSpace(divisionKey);

		var path = DivisionKeyHelper.GetDivisionObjectPath(year, divisionKey: divisionKey, competitionSlug: competitionSlug);
		return Task.FromResult(ReadDocument<DivisionDataDocument>(path));
	}

	public Task SaveDivisionDataAsync(int year, string competitionSlug, string divisionKey, DivisionDataDocument document, long? expectedGeneration, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
		ArgumentException.ThrowIfNullOrWhiteSpace(divisionKey);
		ArgumentNullException.ThrowIfNull(document);

		var path = DivisionKeyHelper.GetDivisionObjectPath(year, divisionKey: divisionKey, competitionSlug: competitionSlug);
		WriteDocument(path, document, expectedGeneration);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<string>> ListTournamentPathsAsync(CancellationToken cancellationToken = default)
	{
		var paths = new List<string>();

		lock (_syncRoot)
		{
			if (!Directory.Exists(_rootPath))
			{
				return Task.FromResult<IReadOnlyList<string>>(paths);
			}

			foreach (var yearDir in Directory.GetDirectories(_rootPath))
			{
				var yearName = Path.GetFileName(yearDir);
				if (!int.TryParse(yearName, CultureInfo.InvariantCulture, out _))
				{
					continue;
				}

				foreach (var competitionDir in Directory.GetDirectories(yearDir))
				{
					var tournamentFile = Path.Combine(competitionDir, "tournament.json");
					if (File.Exists(tournamentFile))
					{
						paths.Add($"{yearName}/{Path.GetFileName(competitionDir)}");
					}
				}
			}
		}

		paths.Sort(StringComparer.OrdinalIgnoreCase);
		return Task.FromResult<IReadOnlyList<string>>(paths);
	}
}
