namespace Region42.PantherShootout.Domain.Documents;

/// <summary>
/// Catalog of all Panther Shootout tournament years stored under <c>panther-shootout/index.json</c>.
/// </summary>
public class TournamentIndexDocument
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public List<TournamentIndexEntryDocument> Tournaments { get; set; } = new();
}

public class TournamentIndexEntryDocument
{
	public int TournamentYearId { get; set; }

	public int Year { get; set; }

	public string Slug { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public bool IsActive { get; set; }

	public string TournamentPath { get; set; } = string.Empty;
}
