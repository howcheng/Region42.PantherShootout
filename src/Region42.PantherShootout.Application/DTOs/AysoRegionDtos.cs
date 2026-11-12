namespace Region42.PantherShootout.Application.DTOs;

/// <summary>
/// Display/read DTO for a year-independent AYSO region number-to-name mapping.
/// </summary>
public class AysoRegionDto
{
	public int Id { get; set; }

	public int RegionNumber { get; set; }

	public string Name { get; set; } = string.Empty;

	/// <summary>Number of teams across loaded tournament years currently referencing this region.</summary>
	public int TeamUsageCount { get; set; }
}

/// <summary>
/// Input DTO for creating or updating an AYSO region mapping.
/// </summary>
public class AysoRegionUpsertDto
{
	public int? Id { get; set; }

	public int RegionNumber { get; set; }

	public string Name { get; set; } = string.Empty;
}
