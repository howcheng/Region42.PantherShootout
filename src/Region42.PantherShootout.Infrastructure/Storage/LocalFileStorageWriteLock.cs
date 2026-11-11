using Microsoft.Extensions.Options;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Infrastructure.Storage;

/// <summary>
/// Local filesystem implementation of <see cref="IStorageWriteLock"/> stored under
/// <c>{competitionSlug}/locks/{lockName}.json</c>.
/// </summary>
public class LocalFileStorageWriteLock : IStorageWriteLock
{
	private readonly string _localRoot;
	private readonly string _competitionSlug;
	private readonly object _syncRoot = new();

	public LocalFileStorageWriteLock(IOptions<StorageOptions> options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var opts = options.Value;
		_localRoot = Path.GetFullPath(opts.LocalRoot);
		_competitionSlug = string.IsNullOrWhiteSpace(opts.CompetitionSlug)
			? DivisionKeyHelper.DefaultCompetitionSlug
			: opts.CompetitionSlug;
	}

	public Task<bool> IsLockedAsync(string lockName, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(lockName);

		lock (_syncRoot)
		{
			var document = ReadLockDocument(lockName);
			if (document is null)
			{
				return Task.FromResult(false);
			}

			return Task.FromResult(!StorageWriteLockHelper.IsExpired(document, DateTime.UtcNow));
		}
	}

	private StorageWriteLockDocument? ReadLockDocument(string lockName)
	{
		var path = GetLockFilePath(lockName);
		if (!File.Exists(path))
		{
			return null;
		}

		var json = File.ReadAllText(path, TournamentJsonSerializer.Utf8WithoutBom);
		return TournamentJsonSerializer.Deserialize<StorageWriteLockDocument>(json);
	}

	private string GetLockFilePath(string lockName)
	{
		var objectPath = StorageWriteLockHelper.GetLockObjectPath(lockName, _competitionSlug);
		return Path.Combine(_localRoot, objectPath.Replace('/', Path.DirectorySeparatorChar));
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

		lock (_syncRoot)
		{
			var utcNow = DateTime.UtcNow;
			var existing = ReadLockDocument(lockName);
			if (existing is not null)
			{
				if (!StorageWriteLockHelper.IsExpired(existing, utcNow))
				{
					return Task.FromResult<IStorageWriteLockHandle?>(null);
				}

				DeleteLockFile(lockName);
			}

			var lockDocument = StorageWriteLockHelper.CreateDocument(holder, ttl, utcNow);
			WriteLockDocument(lockName, lockDocument);

			return Task.FromResult<IStorageWriteLockHandle?>(
				new StorageWriteLockHandle(
					lockName,
					() => ReleaseLockBestEffortAsync(lockName, lockDocument.Holder, lockDocument.AcquiredAtUtc)));
		}
	}

	private void WriteLockDocument(string lockName, StorageWriteLockDocument document)
	{
		var path = GetLockFilePath(lockName);
		var directory = Path.GetDirectoryName(path)!;
		Directory.CreateDirectory(directory);

		var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
		File.WriteAllText(tempPath, TournamentJsonSerializer.Serialize(document), TournamentJsonSerializer.Utf8WithoutBom);
		File.Move(tempPath, path, overwrite: true);
	}

	private void DeleteLockFile(string lockName)
	{
		var path = GetLockFilePath(lockName);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private Task ReleaseLockBestEffortAsync(string lockName, string holder, DateTime acquiredAtUtc)
	{
		lock (_syncRoot)
		{
			var existing = ReadLockDocument(lockName);
			if (existing is not null &&
				string.Equals(existing.Holder, holder, StringComparison.Ordinal) &&
				existing.AcquiredAtUtc == acquiredAtUtc)
			{
				DeleteLockFile(lockName);
			}
		}

		return Task.CompletedTask;
	}
}
