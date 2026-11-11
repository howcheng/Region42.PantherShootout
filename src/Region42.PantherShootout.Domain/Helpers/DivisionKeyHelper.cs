using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Domain.Helpers;

/// <summary>
/// Converts age/gender division identifiers to canonical storage keys, display names, and
/// Google Cloud Storage / local file object paths under the <c>panther-shootout</c> slug.
/// </summary>
public static class DivisionKeyHelper
{
	public const string DefaultCompetitionSlug = "panther-shootout";

	public static string ToKey(AgeGroup ageGroup, Gender gender)
	{
		var age = ageGroup switch
		{
			AgeGroup.U10 => "10u",
			AgeGroup.U12 => "12u",
			AgeGroup.U14 => "14u",
			_ => ageGroup.ToString().ToLowerInvariant(),
		};

		var genderKey = gender == Gender.Boys ? "boys" : "girls";
		return $"{age}-{genderKey}";
	}

	public static AgeGroup ParseAgeGroup(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		return value.Trim().ToUpperInvariant() switch
		{
			"U10" or "10U" => AgeGroup.U10,
			"U12" or "12U" => AgeGroup.U12,
			"U14" or "14U" => AgeGroup.U14,
			_ => Enum.Parse<AgeGroup>(value.Trim(), ignoreCase: true),
		};
	}

	public static Gender ParseGender(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		return value.Trim().ToUpperInvariant() switch
		{
			"BOYS" or "B" => Gender.Boys,
			"GIRLS" or "G" => Gender.Girls,
			_ => throw new ArgumentException($"Unrecognized gender value '{value}'.", nameof(value)),
		};
	}

	public static (AgeGroup AgeGroup, Gender Gender) ParseDivisionName(string divisionName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(divisionName);

		var normalized = divisionName.Trim();

		// Support compact codes like "10UB", "10UG", "U12B", "14U-Boys", or "10U Girls".
		var upper = normalized.ToUpperInvariant().Replace("-", " ", StringComparison.Ordinal);
		var parts = upper.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		if (parts.Length == 1)
		{
			var compact = parts[0];
			if (compact.EndsWith('B') || compact.EndsWith('G'))
			{
				var agePart = compact[..^1];
				var genderPart = compact[^1..];
				return (ParseAgeGroup(agePart), ParseGender(genderPart));
			}
		}

		if (parts.Length >= 2)
		{
			return (ParseAgeGroup(parts[0]), ParseGender(parts[1]));
		}

		throw new ArgumentException($"Unrecognized division name '{divisionName}'.", nameof(divisionName));
	}

	public static string ToAgeGroupString(AgeGroup ageGroup)
	{
		return ageGroup switch
		{
			AgeGroup.U10 => "10U",
			AgeGroup.U12 => "12U",
			AgeGroup.U14 => "14U",
			_ => ageGroup.ToString(),
		};
	}

	public static string ToGenderString(Gender gender)
	{
		return gender == Gender.Boys ? "Boys" : "Girls";
	}

	public static string ToDisplayName(AgeGroup ageGroup, Gender gender)
	{
		return $"{ToAgeGroupString(ageGroup)} {ToGenderString(gender)}";
	}

	public static string ToShortCode(AgeGroup ageGroup, Gender gender)
	{
		var genderInitial = gender == Gender.Boys ? "B" : "G";
		return $"{ToAgeGroupString(ageGroup)}{genderInitial}";
	}

	public static string GetIndexObjectPath(string competitionSlug = DefaultCompetitionSlug)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
		return $"{competitionSlug}/index.json";
	}

	public static string GetLockObjectPath(string lockName, string competitionSlug = DefaultCompetitionSlug)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(lockName);
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
		return $"{competitionSlug}/locks/{lockName}.json";
	}

	public static string GetTournamentDirectoryPath(int year, string competitionSlug = DefaultCompetitionSlug)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
		return $"{year}/{competitionSlug}";
	}

	public static string GetTournamentObjectPath(int year, string competitionSlug = DefaultCompetitionSlug)
	{
		return $"{GetTournamentDirectoryPath(year, competitionSlug)}/tournament.json";
	}

	public static string GetDivisionObjectPath(int year, string divisionKey, string competitionSlug = DefaultCompetitionSlug)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(divisionKey);
		return $"{GetTournamentDirectoryPath(year, competitionSlug)}/{divisionKey}.json";
	}
}
