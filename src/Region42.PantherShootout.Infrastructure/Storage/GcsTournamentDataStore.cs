using System.Net;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Infrastructure.Storage;

/// <summary>
/// Google Cloud Storage implementation of <see cref="ITournamentDataStore"/> with
/// <c>IfGenerationMatch</c> optimistic concurrency and transient rate-limit retries.
/// </summary>
public class GcsTournamentDataStore : ITournamentDataStore
{
	private const int MaxRetryAttempts = 3;

	private readonly StorageClient _storageClient;
	private readonly string _bucketName;
	private readonly string _competitionSlug;

	public GcsTournamentDataStore(StorageClient storageClient, IOptions<StorageOptions> options)
	{
		ArgumentNullException.ThrowIfNull(storageClient);
		ArgumentNullException.ThrowIfNull(options);

		var opts = options.Value;
		_storageClient = storageClient;
		_bucketName = opts.BucketName;
		_competitionSlug = string.IsNullOrWhiteSpace(opts.CompetitionSlug)
			? DivisionKeyHelper.DefaultCompetitionSlug
			: opts.CompetitionSlug;
	}

	public Task<StorageDocument<TournamentIndexDocument>> GetIndexAsync(CancellationToken cancellationToken = default)
	{
		var path = DivisionKeyHelper.GetIndexObjectPath(_competitionSlug);
		return GetObjectAsync<TournamentIndexDocument>(path, cancellationToken);
	}

	private async Task<StorageDocument<T>> GetObjectAsync<T>(string objectPath, CancellationToken cancellationToken)
	{
		try
		{
			var obj = await _storageClient.GetObjectAsync(_bucketName, objectPath, cancellationToken: cancellationToken);
			await using var stream = new MemoryStream();
			await _storageClient.DownloadObjectAsync(_bucketName, objectPath, stream, cancellationToken: cancellationToken);
			stream.Position = 0;

			return new StorageDocument<T>
			{
				Content = await TournamentJsonSerializer.DeserializeAsync<T>(stream, cancellationToken),
				Generation = obj.Generation,
				ObjectPath = objectPath,
			};
		}
		catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
		{
			throw new FileNotFoundException($"Storage object not found: {objectPath}", ex);
		}
	}

	public Task SaveIndexAsync(TournamentIndexDocument document, long? expectedGeneration, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(document);

		var path = DivisionKeyHelper.GetIndexObjectPath(_competitionSlug);
		return SaveObjectAsync(path, document, expectedGeneration, cancellationToken);
	}

	public Task<StorageDocument<RegionsDocument>> GetRegionsDocumentAsync(CancellationToken cancellationToken = default)
	{
		var path = DivisionKeyHelper.GetRegionsObjectPath(_competitionSlug);
		return GetObjectAsync<RegionsDocument>(path, cancellationToken);
	}

	public Task SaveRegionsDocumentAsync(RegionsDocument document, long? expectedGeneration, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(document);

		var path = DivisionKeyHelper.GetRegionsObjectPath(_competitionSlug);
		return SaveObjectAsync(path, document, expectedGeneration, cancellationToken);
	}

	private async Task SaveObjectAsync<T>(string objectPath, T document, long? expectedGeneration, CancellationToken cancellationToken)
	{
		var payload = TournamentJsonSerializer.SerializeToUtf8Bytes(document);

		for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
		{
			try
			{
				await using var stream = new MemoryStream(payload, writable: false);
				var uploadOptions = new UploadObjectOptions
				{
					IfGenerationMatch = expectedGeneration,
				};

				await _storageClient.UploadObjectAsync(
					_bucketName,
					objectPath,
					"application/json",
					stream,
					uploadOptions,
					cancellationToken);

				return;
			}
			catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.PreconditionFailed)
			{
				if (expectedGeneration.HasValue || attempt == MaxRetryAttempts - 1)
				{
					throw new StorageConcurrencyException(objectPath, ex);
				}

				await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken);
			}
			catch (GoogleApiException ex) when (
				(ex.HttpStatusCode == HttpStatusCode.TooManyRequests || ex.HttpStatusCode == HttpStatusCode.ServiceUnavailable) &&
				attempt < MaxRetryAttempts - 1)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken);
			}
		}
	}

	public Task<StorageDocument<TournamentDocument>> GetTournamentDocumentAsync(int year, string competitionSlug, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);

		var path = DivisionKeyHelper.GetTournamentObjectPath(year, competitionSlug);
		return GetObjectAsync<TournamentDocument>(path, cancellationToken);
	}

	public Task SaveTournamentDocumentAsync(int year, string competitionSlug, TournamentDocument document, long? expectedGeneration, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
		ArgumentNullException.ThrowIfNull(document);

		var path = DivisionKeyHelper.GetTournamentObjectPath(year, competitionSlug);
		return SaveObjectAsync(path, document, expectedGeneration, cancellationToken);
	}

	public Task<StorageDocument<DivisionDataDocument>> GetDivisionDataAsync(int year, string competitionSlug, string divisionKey, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
		ArgumentException.ThrowIfNullOrWhiteSpace(divisionKey);

		var path = DivisionKeyHelper.GetDivisionObjectPath(year, divisionKey: divisionKey, competitionSlug: competitionSlug);
		return GetObjectAsync<DivisionDataDocument>(path, cancellationToken);
	}

	public Task SaveDivisionDataAsync(int year, string competitionSlug, string divisionKey, DivisionDataDocument document, long? expectedGeneration, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
		ArgumentException.ThrowIfNullOrWhiteSpace(divisionKey);
		ArgumentNullException.ThrowIfNull(document);

		var path = DivisionKeyHelper.GetDivisionObjectPath(year, divisionKey: divisionKey, competitionSlug: competitionSlug);
		return SaveObjectAsync(path, document, expectedGeneration, cancellationToken);
	}

	public async Task<IReadOnlyList<string>> ListTournamentPathsAsync(CancellationToken cancellationToken = default)
	{
		const string suffix = "/tournament.json";
		var paths = new List<string>();

		await foreach (var obj in _storageClient.ListObjectsAsync(_bucketName, prefix: null).WithCancellation(cancellationToken))
		{
			if (obj.Name is not null && obj.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				paths.Add(obj.Name[..^suffix.Length]);
			}
		}

		paths.Sort(StringComparer.OrdinalIgnoreCase);
		return paths;
	}
}
