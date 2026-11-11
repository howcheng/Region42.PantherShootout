namespace Region42.PantherShootout.Domain.Documents;

/// <summary>
/// Authoritative tournament metadata, division setup, pools, and team rosters for a single year,
/// stored at <c>{year}/{slug}/tournament.json</c>.
/// </summary>
public class TournamentDocument
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	public int Version { get; set; } = 1;

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;

	public TournamentInfoDocument Tournament { get; set; } = new();

	public List<DivisionDocument> Divisions { get; set; } = new();
}

public class TournamentInfoDocument
{
	public int Id { get; set; }

	public int Year { get; set; }

	public string Name { get; set; } = string.Empty;

	public bool IsActive { get; set; }

	public string? CustomMessage { get; set; }
}

public class DivisionDocument
{
	public int Id { get; set; }

	public string Key { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public string AgeGroup { get; set; } = string.Empty;

	public string Gender { get; set; } = string.Empty;

	public int Size { get; set; }

	public string? CustomMessage { get; set; }

	public List<PoolDocument> Pools { get; set; } = new();
}

public class PoolDocument
{
	public int Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public List<TeamDocument> Teams { get; set; } = new();
}

public class TeamDocument
{
	public int Id { get; set; }

	public int TeamNumber { get; set; }

	public string Name { get; set; } = string.Empty;
}
