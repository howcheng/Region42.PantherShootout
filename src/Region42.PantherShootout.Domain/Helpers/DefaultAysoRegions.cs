namespace Region42.PantherShootout.Domain.Helpers;

/// <summary>
/// Default AYSO region number-to-name mappings used to initialize <c>regions.json</c> when no
/// region catalog exists in storage yet.
/// </summary>
public static class DefaultAysoRegions
{
	public static IReadOnlyList<(int RegionNumber, string Name)> Entries { get; } =
	[
		(2, "Arcadia"),
		(4, "Agoura"),
		(9, "Thousand Oaks"),
		(20, "Santa Monica"),
		(33, "Encino"),
		(42, "Newbury Park"),
		(88, "Glendale"),
		(121, "Simi Valley"),
		(638, "Quartz Hill"),
		(795, "Chatsworth"),
	];
}
