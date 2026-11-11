using Region42.PantherShootout.Domain.Common;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// An age/gender division (e.g. "10U Girls") for a given tournament year.
/// </summary>
public class Division : AuditableEntity
{
	public int TournamentYearId { get; set; }

	public TournamentYear? TournamentYear { get; set; }

	public AgeGroup AgeGroup { get; set; }

	public Gender Gender { get; set; }

	/// <summary>E.g. "10U Girls".</summary>
	public required string Name { get; set; }

	public DivisionSize Size { get; set; }

	public string? CustomMessage { get; set; }

	public ICollection<Pool> Pools { get; set; } = new List<Pool>();
}
