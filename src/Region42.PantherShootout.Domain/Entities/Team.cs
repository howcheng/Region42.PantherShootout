using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A team competing in a pool within a division for a given tournament year.
/// </summary>
public class Team : AuditableEntity
{
	public const int HostRegionNumber = 42;

	private string _name = string.Empty;

	public int PoolId { get; set; }

	public Pool? Pool { get; set; }

	/// <summary>
	/// The team's 1-based position/number within its pool. Combined with the pool's letter
	/// designation (e.g. "A"), this forms the team's short designation/code (e.g. "A1").
	/// </summary>
	public int TeamNumber { get; set; }

	/// <summary>
	/// Permanent AYSO region number (e.g. 42 for Newbury Park, 121 for Simi Valley).
	/// </summary>
	public int RegionNumber { get; set; }

	/// <summary>
	/// Resolved region/city display name hydrated from the global <see cref="AysoRegion"/> catalog.
	/// </summary>
	public string RegionName { get; set; } = string.Empty;

	/// <summary>
	/// Head coach's last name (e.g. "Wright").
	/// </summary>
	public string CoachName { get; set; } = string.Empty;

	/// <summary>
	/// Whether 1 pool-play standings point is deducted for a host Region 42 team failing to meet volunteer requirements.
	/// </summary>
	public bool HasVolunteerPointDeduction { get; set; }

	/// <summary>
	/// True when this team belongs to host AYSO Region 42 (<see cref="HostRegionNumber"/>).
	/// </summary>
	public bool IsHostRegion => RegionNumber == HostRegionNumber;

	/// <summary>
	/// Formatted display name (<c>{RegionName}: {CoachName}</c> when discrete properties are populated,
	/// falling back to any explicitly stored name).
	/// </summary>
	public string Name
	{
		get => GetDisplayName();
		set => _name = value ?? string.Empty;
	}

	/// <summary>
	/// Discrete team code (<c>{Pool.Name}{TeamNumber}</c>, e.g. <c>A1</c>, <c>C4</c>).
	/// </summary>
	public string Code => GetDesignation();

	/// <summary>
	/// The short designation for this team, e.g. "A1" (pool name "A" + team number 1).
	/// </summary>
	public string GetDesignation() => $"{Pool?.Name}{TeamNumber}";

	private string GetDisplayName()
	{
		if (!string.IsNullOrWhiteSpace(CoachName))
		{
			if (!string.IsNullOrWhiteSpace(RegionName))
			{
				return $"{RegionName}: {CoachName}";
			}

			if (!string.IsNullOrWhiteSpace(_name))
			{
				return _name;
			}

			if (RegionNumber > 0)
			{
				return $"Region {RegionNumber}: {CoachName}";
			}

			return CoachName;
		}

		return _name;
	}
}
