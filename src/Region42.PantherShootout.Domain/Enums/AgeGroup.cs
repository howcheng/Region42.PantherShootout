using System.ComponentModel.DataAnnotations;

namespace Region42.PantherShootout.Domain.Enums;

/// <summary>
/// Supported age divisions in the tournament.
/// </summary>
public enum AgeGroup
{
	[Display(Name = "10U")]
	U10 = 0,

	[Display(Name = "12U")]
	U12 = 1,

	[Display(Name = "14U")]
	U14 = 2,
}
