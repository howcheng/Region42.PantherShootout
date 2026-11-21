using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.DTOs;

/// <summary>
/// Summary DTO for a tournament year (season) and its imported division/team counts.
/// </summary>
public class TournamentYearDto
{
	public int Id { get; set; }

	public int Year { get; set; }

	public string Name { get; set; } = string.Empty;

	public bool IsActive { get; set; }

	public string? CustomMessage { get; set; }

	public int DivisionCount { get; set; }

	public int TeamCount { get; set; }

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;
}

/// <summary>
/// Input DTO for creating a new tournament year.
/// </summary>
public class TournamentYearCreateDto
{
	public int Year { get; set; }

	public string? Name { get; set; }

	public bool IsActive { get; set; } = true;

	public string? CustomMessage { get; set; }
}

/// <summary>
/// Input DTO for updating an existing tournament year's metadata and active status.
/// </summary>
public class TournamentYearUpdateDto
{
	public int Id { get; set; }

	public string Name { get; set; } = string.Empty;

	public bool IsActive { get; set; }

	public string? CustomMessage { get; set; }
}

/// <summary>
/// Summary DTO for a division within a tournament year, including custom bulletin text.
/// </summary>
public class DivisionSummaryDto
{
	public int Id { get; set; }

	public int TournamentYearId { get; set; }

	public int Year { get; set; }

	public string DivisionKey { get; set; } = string.Empty;

	public string ShortCode { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	public AgeGroup AgeGroup { get; set; }

	public Gender Gender { get; set; }

	public DivisionSize Size { get; set; }

	public int PoolCount { get; set; }

	public int TeamCount { get; set; }

	public string? CustomMessage { get; set; }
}

/// <summary>
/// Input DTO for updating a division's custom bulletin message.
/// </summary>
public class DivisionBulletinUpdateDto
{
	public int DivisionId { get; set; }

	public string? CustomMessage { get; set; }
}
