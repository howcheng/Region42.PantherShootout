using Region42.PantherShootout.Domain.Helpers;

namespace Region42.PantherShootout.Infrastructure.Storage;

/// <summary>
/// Configuration options for tournament JSON document storage.
/// </summary>
public class StorageOptions
{
	public const string SectionName = "Storage";

	public string Provider { get; set; } = "LocalFile";

	public string BucketName { get; set; } = "region42-storage";

	public string CompetitionSlug { get; set; } = DivisionKeyHelper.DefaultCompetitionSlug;

	public string LocalRoot { get; set; } = "../../.local-data/storage";
}
