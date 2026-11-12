namespace Region42.PantherShootout.Domain.Documents;

/// <summary>
/// Year-independent catalog of AYSO region numbers and names, stored at
/// <c>{slug}/regions.json</c>.
/// </summary>
public class RegionsDocument
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public int Version { get; set; } = 1;

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;

	public List<RegionItemDocument> Regions { get; set; } = new();
}

public class RegionItemDocument
{
	public int Id { get; set; }

	public int RegionNumber { get; set; }

	public string Name { get; set; } = string.Empty;
}
