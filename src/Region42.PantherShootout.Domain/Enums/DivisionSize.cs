namespace Region42.PantherShootout.Domain.Enums;

/// <summary>
/// The supported division team-count configurations. The numeric value is the total number
/// of teams in the division (see the division-size configuration matrix in the business rules).
/// </summary>
public enum DivisionSize
{
	Four = 4,
	Five = 5,
	Six = 6,
	Eight = 8,
	Ten = 10,
	Twelve = 12,
}
