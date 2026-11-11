using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Helpers;
using Xunit;

namespace Region42.PantherShootout.Domain.Tests.Helpers;

public class DivisionKeyHelperTests
{
	[Theory]
	[InlineData(AgeGroup.U10, Gender.Boys, "10u-boys")]
	[InlineData(AgeGroup.U10, Gender.Girls, "10u-girls")]
	[InlineData(AgeGroup.U12, Gender.Boys, "12u-boys")]
	[InlineData(AgeGroup.U12, Gender.Girls, "12u-girls")]
	[InlineData(AgeGroup.U14, Gender.Boys, "14u-boys")]
	[InlineData(AgeGroup.U14, Gender.Girls, "14u-girls")]
	public void ToKey_ReturnsExpectedStorageKey(AgeGroup ageGroup, Gender gender, string expectedKey)
	{
		Assert.Equal(expectedKey, DivisionKeyHelper.ToKey(ageGroup, gender));
	}

	[Theory]
	[InlineData("10U", AgeGroup.U10)]
	[InlineData("U10", AgeGroup.U10)]
	[InlineData("12u", AgeGroup.U12)]
	[InlineData("u14", AgeGroup.U14)]
	public void ParseAgeGroup_SupportsStandardFormats(string input, AgeGroup expected)
	{
		Assert.Equal(expected, DivisionKeyHelper.ParseAgeGroup(input));
	}

	[Theory]
	[InlineData("10U Girls", AgeGroup.U10, Gender.Girls)]
	[InlineData("U12 Boys", AgeGroup.U12, Gender.Boys)]
	[InlineData("14UG", AgeGroup.U14, Gender.Girls)]
	[InlineData("U10B", AgeGroup.U10, Gender.Boys)]
	[InlineData("12u-boys", AgeGroup.U12, Gender.Boys)]
	public void ParseDivisionName_ExtractsAgeGroupAndGender(string divisionName, AgeGroup expectedAge, Gender expectedGender)
	{
		var (ageGroup, gender) = DivisionKeyHelper.ParseDivisionName(divisionName);

		Assert.Equal(expectedAge, ageGroup);
		Assert.Equal(expectedGender, gender);
	}

	[Fact]
	public void ObjectPathHelpers_UseIsolatedPantherShootoutPaths()
	{
		Assert.Equal("panther-shootout/index.json", DivisionKeyHelper.GetIndexObjectPath());
		Assert.Equal("panther-shootout/locks/csv-schedule-import.json", DivisionKeyHelper.GetLockObjectPath("csv-schedule-import"));
		Assert.Equal("2026/panther-shootout/tournament.json", DivisionKeyHelper.GetTournamentObjectPath(2026));
		Assert.Equal("2026/panther-shootout/12u-girls.json", DivisionKeyHelper.GetDivisionObjectPath(2026, "12u-girls"));
	}
}
