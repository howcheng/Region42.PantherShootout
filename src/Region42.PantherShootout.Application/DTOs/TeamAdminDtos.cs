using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.DTOs;

public class DivisionTeamsDto
{
	public int DivisionId { get; set; }

	public int TournamentYearId { get; set; }

	public int Year { get; set; }

	public string DivisionKey { get; set; } = string.Empty;

	public string ShortCode { get; set; } = string.Empty;

	public string DivisionName { get; set; } = string.Empty;

	public DivisionSize Size { get; set; }

	public List<PoolTeamsDto> Pools { get; set; } = new();
}

public class PoolTeamsDto
{
	public int PoolId { get; set; }

	public string PoolName { get; set; } = string.Empty;

	public List<TeamAdminItemDto> Teams { get; set; } = new();
}

public class TeamAdminItemDto
{
	public int Id { get; set; }

	public int PoolId { get; set; }

	public string PoolName { get; set; } = string.Empty;

	public int TeamNumber { get; set; }

	public string Code { get; set; } = string.Empty;

	public int RegionNumber { get; set; }

	public string RegionName { get; set; } = string.Empty;

	public string CoachName { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public bool IsHostRegion { get; set; }

	public bool HasVolunteerPointDeduction { get; set; }
}

public class DivisionTeamsUpdateDto
{
	public int DivisionId { get; set; }

	public IReadOnlyList<TeamUpdateItemDto> Teams { get; set; } = Array.Empty<TeamUpdateItemDto>();
}

public class TeamUpdateItemDto
{
	public int TeamId { get; set; }

	public int RegionNumber { get; set; }

	public string CoachName { get; set; } = string.Empty;

	public bool HasVolunteerPointDeduction { get; set; }
}
