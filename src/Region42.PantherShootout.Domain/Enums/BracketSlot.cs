namespace Region42.PantherShootout.Domain.Enums;

/// <summary>
/// Identifies the role/slot a championship-bracket game plays within a division's bracket,
/// per the division-size-specific bracket rules.
/// </summary>
public enum BracketSlot
{
	Consolation,
	Semifinal1,
	Semifinal2,
	ThirdPlace,
	Championship,
}
