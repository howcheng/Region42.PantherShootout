using System.Net;
using Google;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using Moq;
using Region42.PantherShootout.Domain;
using Region42.PantherShootout.Infrastructure.Storage;
using Xunit;
using GcsObject = Google.Apis.Storage.v1.Data.Object;

namespace Region42.PantherShootout.Infrastructure.Tests.Storage;

public sealed class StorageWriteLockTests : IDisposable
{
	private readonly string _tempRoot;
	private readonly LocalFileStorageWriteLock _localLock;

	public StorageWriteLockTests()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), "region42-pso-lock-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempRoot);

		_localLock = new LocalFileStorageWriteLock(Options.Create(new StorageOptions
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
	public async Task LocalLock_AcquireBlocksSecondHolder_AndReleasesOnDispose()
	{
		var handle = await _localLock.TryAcquireAsync(
			StorageWriteLockNames.CsvScheduleImport,
			"admin1@aysoregion42.org",
			TimeSpan.FromMinutes(5));

		Assert.NotNull(handle);
		Assert.Equal(StorageWriteLockNames.CsvScheduleImport, handle!.LockName);
		Assert.True(await _localLock.IsLockedAsync(StorageWriteLockNames.CsvScheduleImport));

		var expectedLockPath = Path.Combine(_tempRoot, "panther-shootout", "locks", "csv-schedule-import.json");
		Assert.True(File.Exists(expectedLockPath));

		var secondAttempt = await _localLock.TryAcquireAsync(
			StorageWriteLockNames.CsvScheduleImport,
			"admin2@aysoregion42.org",
			TimeSpan.FromMinutes(5));
		Assert.Null(secondAttempt);

		await handle.DisposeAsync();
		Assert.False(await _localLock.IsLockedAsync(StorageWriteLockNames.CsvScheduleImport));
		Assert.False(File.Exists(expectedLockPath));
	}

	[Fact]
	public async Task LocalLock_ExpiredLockCanBeReclaimed_AndStaleDisposeDoesNotDeleteActiveLock()
	{
		var firstHandle = await _localLock.TryAcquireAsync(
			StorageWriteLockNames.CsvScheduleImport,
			"holder-1",
			TimeSpan.FromMilliseconds(25));
		Assert.NotNull(firstHandle);

		await Task.Delay(75);
		Assert.False(await _localLock.IsLockedAsync(StorageWriteLockNames.CsvScheduleImport));

		await using var secondHandle = await _localLock.TryAcquireAsync(
			StorageWriteLockNames.CsvScheduleImport,
			"holder-2",
			TimeSpan.FromMinutes(5));
		Assert.NotNull(secondHandle);

		// Late dispose of the first expired handle must not delete holder-2's active lock
		await firstHandle!.DisposeAsync();
		Assert.True(await _localLock.IsLockedAsync(StorageWriteLockNames.CsvScheduleImport));
	}

	[Fact]
	public async Task GcsLock_UsesIfGenerationMatchZero_AndDeletesWithCapturedGeneration()
	{
		const string bucketName = "region42-storage";
		const string lockObjectPath = "panther-shootout/locks/csv-schedule-import.json";

		var mockClient = new Mock<StorageClient>();
		mockClient
			.Setup(c => c.GetObjectAsync(bucketName, lockObjectPath, null, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new GoogleApiException("storage", "Not Found") { HttpStatusCode = HttpStatusCode.NotFound });

		UploadObjectOptions? capturedUploadOpts = null;
		mockClient
			.Setup(c => c.UploadObjectAsync(
				bucketName,
				lockObjectPath,
				"application/json",
				It.IsAny<Stream>(),
				It.IsAny<UploadObjectOptions>(),
				It.IsAny<CancellationToken>(),
				null))
			.Callback<string, string, string, Stream, UploadObjectOptions, CancellationToken, IProgress<Google.Apis.Upload.IUploadProgress>>(
				(_, _, _, _, opts, _, _) => capturedUploadOpts = opts)
			.ReturnsAsync(new GcsObject { Name = lockObjectPath, Generation = 42L });

		DeleteObjectOptions? capturedDeleteOpts = null;
		mockClient
			.Setup(c => c.DeleteObjectAsync(
				bucketName,
				lockObjectPath,
				It.IsAny<DeleteObjectOptions>(),
				It.IsAny<CancellationToken>()))
			.Callback<string, string, DeleteObjectOptions, CancellationToken>(
				(_, _, opts, _) => capturedDeleteOpts = opts)
			.Returns(Task.CompletedTask);

		var gcsLock = new GcsStorageWriteLock(
			mockClient.Object,
			Options.Create(new StorageOptions { Provider = "Gcs", BucketName = bucketName, CompetitionSlug = "panther-shootout" }));

		var handle = await gcsLock.TryAcquireAsync(
			StorageWriteLockNames.CsvScheduleImport,
			"admin@aysoregion42.org",
			TimeSpan.FromMinutes(5));

		Assert.NotNull(handle);
		Assert.NotNull(capturedUploadOpts);
		Assert.Equal(0L, capturedUploadOpts!.IfGenerationMatch);

		await handle!.DisposeAsync();
		Assert.NotNull(capturedDeleteOpts);
		Assert.Equal(42L, capturedDeleteOpts!.IfGenerationMatch);
	}
}
