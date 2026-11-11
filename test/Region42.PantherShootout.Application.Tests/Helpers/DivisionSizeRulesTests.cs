using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Domain.Enums;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Helpers;

public class DivisionSizeRulesTests
{
	[Theory]
	[InlineData(DivisionSize.Four, 1, 4, 3, 6)]
	[InlineData(DivisionSize.Five, 1, 5, 4, 10)]
	[InlineData(DivisionSize.Six, 2, 3, 2, 3)]
	[InlineData(DivisionSize.Eight, 2, 4, 3, 6)]
	[InlineData(DivisionSize.Ten, 2, 5, 4, 10)]
	[InlineData(DivisionSize.Twelve, 3, 4, 3, 6)]
	public void Matrix_MatchesBusinessRules(
		DivisionSize size,
		int expectedPools,
		int expectedTeamsPerPool,
		int expectedGamesPerTeam,
		int expectedGamesPerPool)
	{
		Assert.Equal(expectedPools, DivisionSizeRules.GetPoolCount(size));
		Assert.Equal(expectedTeamsPerPool, DivisionSizeRules.GetTeamsPerPool(size));
		Assert.Equal(expectedGamesPerTeam, DivisionSizeRules.GetExpectedGamesPerTeam(size));
		Assert.Equal(expectedGamesPerPool, DivisionSizeRules.GetExpectedGamesPerPool(size));
	}

	[Theory]
	[InlineData(DivisionSize.Four, true)]
	[InlineData(DivisionSize.Five, false)]
	[InlineData(DivisionSize.Six, true)]
	[InlineData(DivisionSize.Eight, true)]
	[InlineData(DivisionSize.Ten, false)]
	[InlineData(DivisionSize.Twelve, true)]
	public void HasChampionshipBracket_MatchesBusinessRules(DivisionSize size, bool expected) =>
		Assert.Equal(expected, DivisionSizeRules.HasChampionshipBracket(size));

	[Fact]
	public void TenTeam_UsesCombinedStandingsAndDivisionWideForfeitScope()
	{
		Assert.True(DivisionSizeRules.UsesCombinedDivisionStandings(DivisionSize.Ten));
		Assert.True(DivisionSizeRules.UsesDivisionWideForfeitScope(DivisionSize.Ten));
		Assert.False(DivisionSizeRules.UsesPoolWinnerRunnerUpRanking(DivisionSize.Ten));
	}

	[Fact]
	public void TwelveTeam_UsesPoolWinnerRunnerUpRanking()
	{
		Assert.True(DivisionSizeRules.UsesPoolWinnerRunnerUpRanking(DivisionSize.Twelve));
		Assert.False(DivisionSizeRules.UsesCombinedDivisionStandings(DivisionSize.Twelve));
	}
}
