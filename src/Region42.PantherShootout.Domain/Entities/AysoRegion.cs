using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// Year-independent mapping between a permanent AYSO region number (e.g. 42) and its region/city
/// display name (e.g. "Newbury Park").
/// </summary>
public class AysoRegion : AuditableEntity
{
	public int RegionNumber { get; set; }

	public required string Name { get; set; }
}
