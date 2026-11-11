using System.Net;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Infrastructure.Storage;

/// <summary>
/// Google Cloud Storage implementation of <see cref="IStorageWriteLock"/> using
/// <c>IfGenerationMatch = 0</c> atomic object creation under <c>{competitionSlug}/locks/{lockName}.json</c>.
/// </summary>
public class GcsStorageWriteLock : IStorageWriteLock
{
	private const int MaxAcquireAttempts = 3;

	private readonly StorageClient _storageClient;
	private readonly string _bucketName;
	private readonly string _competitionSlug;

	public GcsStorageWriteLock(StorageClient storageClient, IOptions<StorageOptions> options)
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

	public Task<bool> IsLockedAsync(string lockName, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(lockName);

		return IsLockedInternalAsync(lockName, cancellationToken);
	}

	private async Task<bool> IsLockedInternalAsync(string lockName, CancellationToken cancellationToken)
	{
		var existing = await TryReadLockDocumentAsync(lockName, cancellationToken);
		if (existing is null)
		{
			return false;
		}

		return !StorageWriteLockHelper.IsExpired(existing.Value.Document, DateTime.UtcNow);
	}

	private async Task<(StorageWriteLockDocument Document, long? Generation)?> TryReadLockDocumentAsync(
		string lockName,
		CancellationToken cancellationToken)
	{
		var objectPath = StorageWriteLockHelper.GetLockObjectPath(lockName, _competitionSlug);

		try
		{
			var obj = await _storageClient.GetObjectAsync(_bucketName, objectPath, cancellationToken: cancellationToken);
			await using var stream = new MemoryStream();
			await _storageClient.DownloadObjectAsync(_bucketName, objectPath, stream, cancellationToken: cancellationToken);
			stream.Position = 0;

			var document = await TournamentJsonSerializer.DeserializeAsync<StorageWriteLockDocument>(stream, cancellationToken);
			return (document, obj.Generation);
		}
		catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public Task<IStorageWriteLockHandle?> TryAcquireAsync(
		string lockName,
		string holder,
		TimeSpan ttl,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(lockName);
		ArgumentException.ThrowIfNullOrWhiteSpace(holder);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

		return TryAcquireInternalAsync(lockName, holder, ttl, cancellationToken);
	}

	private async Task<IStorageWriteLockHandle?> TryAcquireInternalAsync(
		string lockName,
		string holder,
		TimeSpan ttl,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < MaxAcquireAttempts; attempt++)
		{
			var utcNow = DateTime.UtcNow;
			var existing = await TryReadLockDocumentAsync(lockName, cancellationToken);
			if (existing is not null)
			{
				if (!StorageWriteLockHelper.IsExpired(existing.Value.Document, utcNow))
				{
					return null;
				}

				await DeleteLockAsync(lockName, existing.Value.Generation, cancellationToken);
			}

			var created = await TryCreateLockAsync(lockName, holder, ttl, utcNow, cancellationToken);
			if (created.Acquired)
			{
				return new StorageWriteLockHandle(
					lockName,
					() => ReleaseLockBestEffortAsync(lockName, created.Generation));
			}
		}

		return null;
	}

	private async Task<(bool Acquired, long? Generation)> TryCreateLockAsync(
		string lockName,
		string holder,
		TimeSpan ttl,
		DateTime utcNow,
		CancellationToken cancellationToken)
	{
		var objectPath = StorageWriteLockHelper.GetLockObjectPath(lockName, _competitionSlug);
		var document = StorageWriteLockHelper.CreateDocument(holder, ttl, utcNow);

		try
		{
			await using var stream = new MemoryStream(TournamentJsonSerializer.SerializeToUtf8Bytes(document), writable: false);
			var uploadOptions = new UploadObjectOptions
			{
				IfGenerationMatch = 0,
			};

			var uploaded = await _storageClient.UploadObjectAsync(
				_bucketName,
				objectPath,
				"application/json",
				stream,
				uploadOptions,
				cancellationToken);

			return (true, uploaded?.Generation);
		}
		catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.PreconditionFailed)
		{
			return (false, null);
		}
	}

	private async Task DeleteLockAsync(string lockName, long? generation, CancellationToken cancellationToken)
	{
		var objectPath = StorageWriteLockHelper.GetLockObjectPath(lockName, _competitionSlug);

		try
		{
			var deleteOptions = generation.HasValue
				? new DeleteObjectOptions { IfGenerationMatch = generation.Value }
				: null;

			await _storageClient.DeleteObjectAsync(
				_bucketName,
				objectPath,
				deleteOptions,
				cancellationToken);
		}
		catch (GoogleApiException ex) when (
			ex.HttpStatusCode == HttpStatusCode.NotFound ||
			ex.HttpStatusCode == HttpStatusCode.PreconditionFailed)
		{
		}
	}

	private async Task ReleaseLockBestEffortAsync(string lockName, long? createdGeneration)
	{
		try
		{
			var generationToDelete = createdGeneration;
			if (!generationToDelete.HasValue)
			{
				var existing = await TryReadLockDocumentAsync(lockName, CancellationToken.None);
				if (existing is null)
				{
					return;
				}

				generationToDelete = existing.Value.Generation;
			}

			await DeleteLockAsync(lockName, generationToDelete, CancellationToken.None);
		}
		catch (GoogleApiException)
		{
		}
	}
}
