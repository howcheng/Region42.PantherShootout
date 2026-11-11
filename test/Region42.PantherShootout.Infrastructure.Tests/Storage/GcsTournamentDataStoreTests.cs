using System.Net;
using Google;
using Google.Api.Gax;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using Moq;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Infrastructure.Storage;
using Xunit;
using GcsObject = Google.Apis.Storage.v1.Data.Object;

namespace Region42.PantherShootout.Infrastructure.Tests.Storage;

public class GcsTournamentDataStoreTests
{
	private const string BucketName = "region42-storage";

	private static GcsTournamentDataStore CreateStore(Mock<StorageClient> mockClient)
	{
		var options = Options.Create(new StorageOptions
		{
			Provider = "Gcs",
			BucketName = BucketName,
			CompetitionSlug = "panther-shootout",
		});

		return new GcsTournamentDataStore(mockClient.Object, options);
	}

	private static GoogleApiException CreateGoogleApiException(HttpStatusCode statusCode)
	{
		return new GoogleApiException("storage", $"Simulated {statusCode}")
		{
			HttpStatusCode = statusCode,
		};
	}

	[Fact]
	public async Task GetAndSaveDivisionDataAsync_UsesIsolatedPathAndExpectedGeneration()
	{
		var mockClient = new Mock<StorageClient>();
		UploadObjectOptions? capturedUploadOptions = null;
		string? capturedPath = null;
		byte[]? uploadedBytes = null;

		mockClient
			.Setup(c => c.UploadObjectAsync(
				BucketName,
				It.IsAny<string>(),
				"application/json",
				It.IsAny<Stream>(),
				It.IsAny<UploadObjectOptions>(),
				It.IsAny<CancellationToken>(),
				null))
			.Callback<string, string, string, Stream, UploadObjectOptions, CancellationToken, IProgress<Google.Apis.Upload.IUploadProgress>>(
				(_, objectName, _, source, opts, _, _) =>
				{
					capturedPath = objectName;
					capturedUploadOptions = opts;
					using var ms = new MemoryStream();
					source.CopyTo(ms);
					uploadedBytes = ms.ToArray();
				})
			.ReturnsAsync(new GcsObject { Name = "2026/panther-shootout/12u-boys.json", Generation = 7L });

		var store = CreateStore(mockClient);
		var doc = new DivisionDataDocument
		{
			DivisionId = 12,
			DivisionKey = "12u-boys",
		};

		await store.SaveDivisionDataAsync(2026, "panther-shootout", "12u-boys", doc, expectedGeneration: 6L);

		Assert.Equal("2026/panther-shootout/12u-boys.json", capturedPath);
		Assert.NotNull(capturedUploadOptions);
		Assert.Equal(6L, capturedUploadOptions!.IfGenerationMatch);
		Assert.NotNull(uploadedBytes);

		mockClient
			.Setup(c => c.GetObjectAsync(
				BucketName,
				"2026/panther-shootout/12u-boys.json",
				null,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(new GcsObject { Name = "2026/panther-shootout/12u-boys.json", Generation = 7L });

		mockClient
			.Setup(c => c.DownloadObjectAsync(
				BucketName,
				"2026/panther-shootout/12u-boys.json",
				It.IsAny<Stream>(),
				null,
				It.IsAny<CancellationToken>(),
				null))
			.Callback<string, string, Stream, DownloadObjectOptions, CancellationToken, IProgress<Google.Apis.Download.IDownloadProgress>>(
				(_, _, destination, _, _, _) =>
				{
					destination.Write(uploadedBytes!, 0, uploadedBytes!.Length);
				})
			.ReturnsAsync(new GcsObject { Name = "2026/panther-shootout/12u-boys.json", Generation = 7L });

		var loaded = await store.GetDivisionDataAsync(2026, "panther-shootout", "12u-boys");
		Assert.Equal(7L, loaded.Generation);
		Assert.Equal("12u-boys", loaded.Content.DivisionKey);
	}

	[Fact]
	public async Task SaveIndexAsync_WhenPreconditionFailedWithExpectedGeneration_ThrowsStorageConcurrencyExceptionImmediately()
	{
		var mockClient = new Mock<StorageClient>();
		var uploadCalls = 0;

		mockClient
			.Setup(c => c.UploadObjectAsync(
				BucketName,
				"panther-shootout/index.json",
				"application/json",
				It.IsAny<Stream>(),
				It.IsAny<UploadObjectOptions>(),
				It.IsAny<CancellationToken>(),
				null))
			.Callback(() => uploadCalls++)
			.ThrowsAsync(CreateGoogleApiException(HttpStatusCode.PreconditionFailed));

		var store = CreateStore(mockClient);

		var ex = await Assert.ThrowsAsync<StorageConcurrencyException>(
			() => store.SaveIndexAsync(new TournamentIndexDocument(), expectedGeneration: 3L));

		Assert.Equal("panther-shootout/index.json", ex.ObjectPath);
		Assert.Equal(1, uploadCalls);
	}

	[Fact]
	public async Task GetIndexAsync_WhenNotFound_ThrowsFileNotFoundException()
	{
		var mockClient = new Mock<StorageClient>();
		mockClient
			.Setup(c => c.GetObjectAsync(
				BucketName,
				"panther-shootout/index.json",
				null,
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(CreateGoogleApiException(HttpStatusCode.NotFound));

		var store = CreateStore(mockClient);

		await Assert.ThrowsAsync<FileNotFoundException>(() => store.GetIndexAsync());
	}

	[Fact]
	public async Task ListTournamentPathsAsync_FiltersOnlyTournamentJsonObjects()
	{
		var objects = new[]
		{
			new GcsObject { Name = "index.json" },
			new GcsObject { Name = "panther-shootout/index.json" },
			new GcsObject { Name = "2026/core-season/season.json" },
			new GcsObject { Name = "2026/panther-shootout/tournament.json" },
			new GcsObject { Name = "2026/panther-shootout/10u-boys.json" },
			new GcsObject { Name = "2025/panther-shootout/tournament.json" },
		};

		var mockClient = new Mock<StorageClient>();
		mockClient
			.Setup(c => c.ListObjectsAsync(BucketName, null, null))
			.Returns(new FakePagedAsyncEnumerable(objects));

		var store = CreateStore(mockClient);
		var paths = await store.ListTournamentPathsAsync();

		Assert.Equal(["2025/panther-shootout", "2026/panther-shootout"], paths);
	}

	private sealed class FakePagedAsyncEnumerable(IEnumerable<GcsObject> items)
		: PagedAsyncEnumerable<Google.Apis.Storage.v1.Data.Objects, GcsObject>
	{
		public override IAsyncEnumerator<GcsObject> GetAsyncEnumerator(CancellationToken cancellationToken = default)
		{
			return items.ToAsyncEnumerable().GetAsyncEnumerator(cancellationToken);
		}
	}
}
