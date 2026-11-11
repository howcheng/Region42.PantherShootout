using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class ChampionshipBracketBuilderTests
{
	[Theory]
	[InlineData(DivisionSize.Four, 2)]
	[InlineData(DivisionSize.Five, 0)]
	[InlineData(DivisionSize.Six, 5)]
	[InlineData(DivisionSize.Eight, 2)]
	[InlineData(DivisionSize.Ten, 0)]
	[InlineData(DivisionSize.Twelve, 2)]
	public void GetSlotDefinitions_ReturnsExpectedSlotCount(DivisionSize size, int expectedCount)
	{
		var definitions = ChampionshipBracketBuilder.GetSlotDefinitions(size);
		Assert.Equal(expectedCount, definitions.Count);
	}

	[Fact]
	public void CreateBracketShells_FourTeam_CreatesConsolationAndChampionship()
	{
		var shells = ChampionshipBracketBuilder.CreateBracketShells(divisionId: 9, DivisionSize.Four);

		Assert.Equal(2, shells.Count);
		Assert.All(shells, g => Assert.Equal(9, g.DivisionId));
		Assert.Contains(shells, g => g.Slot == BracketSlot.Consolation);
		Assert.Contains(shells, g => g.Slot == BracketSlot.Championship);
		Assert.All(shells, g => Assert.Null(g.HomeTeamId));
	}

	[Fact]
	public void SeedAndProgress_FourTeam_SeedsFromSinglePoolRanks()
	{
		var division = CreateDivision(DivisionSize.Four);
		var (standings, teamIds) = CreateCompletedFourTeamStandings();
		var games = ChampionshipBracketBuilder.CreateBracketShells(division.Id, DivisionSize.Four);

		var result = ChampionshipBracketBuilder.SeedAndProgress(division, standings, games);

		Assert.True(result.HasBracket);
		Assert.True(result.PoolPlayFinal);
		Assert.Equal(teamIds[0], Find(games, BracketSlot.Championship).HomeTeamId); // 1st
		Assert.Equal(teamIds[1], Find(games, BracketSlot.Championship).AwayTeamId); // 2nd
		Assert.Equal(teamIds[2], Find(games, BracketSlot.Consolation).HomeTeamId); // 3rd
		Assert.Equal(teamIds[3], Find(games, BracketSlot.Consolation).AwayTeamId); // 4th
		Assert.Null(result.ChampionTeamId);
	}

	[Fact]
	public void SeedAndProgress_FourTeam_SetsChampionWhenChampionshipComplete()
	{
		var division = CreateDivision(DivisionSize.Four);
		var (standings, teamIds) = CreateCompletedFourTeamStandings();
		var games = ChampionshipBracketBuilder.CreateBracketShells(division.Id, DivisionSize.Four);
		ChampionshipBracketBuilder.SeedAndProgress(division, standings, games);

		var final = Find(games, BracketSlot.Championship);
		final.HomeGoals = 2;
		final.AwayGoals = 1;

		var result = ChampionshipBracketBuilder.SeedAndProgress(division, standings, games);

		Assert.Equal(teamIds[0], result.ChampionTeamId);
		Assert.Equal(teamIds[0], result.Games.Single(g => g.Slot == BracketSlot.Championship).WinnerTeamId);
	}

	[Fact]
	public void SeedAndProgress_FiveTeam_HasNoBracketAndUsesPoolLeaderAsChampion()
	{
		var division = CreateDivision(DivisionSize.Five);
		var standings = new DivisionPoolPlayStandingsResult
		{
			DivisionId = division.Id,
			DivisionName = division.Name,
			AllPoolsFinal = true,
			Pools =
			[
				new PoolPlayStandingsResult
				{
					PoolId = 1,
					PoolName = "A",
					IsFinal = true,
					Standings =
					[
						new PoolPlayStandingDto { Rank = 1, TeamId = 50, TeamName = "Leader" },
						new PoolPlayStandingDto { Rank = 2, TeamId = 51, TeamName = "Second" },
					],
				},
			],
		};

		var result = ChampionshipBracketBuilder.SeedAndProgress(division, standings, []);

		Assert.False(result.HasBracket);
		Assert.Empty(result.Games);
		Assert.Equal(50, result.ChampionTeamId);
	}

	[Fact]
	public void SeedAndProgress_SixTeam_SeedsOpeningRoundsAndProgressesSemifinals()
	{
		var division = CreateDivision(DivisionSize.Six);
		var (standings, ids) = CreateCompletedSixTeamStandings();
		var games = ChampionshipBracketBuilder.CreateBracketShells(division.Id, DivisionSize.Six);
		for (var i = 0; i < games.Count; i++)
		{
			games[i].Id = i + 1;
		}

		var seeded = ChampionshipBracketBuilder.SeedAndProgress(division, standings, games);

		Assert.Equal(ids["A1"], Find(games, BracketSlot.Semifinal1).HomeTeamId);
		Assert.Equal(ids["B2"], Find(games, BracketSlot.Semifinal1).AwayTeamId);
		Assert.Equal(ids["B1"], Find(games, BracketSlot.Semifinal2).HomeTeamId);
		Assert.Equal(ids["A2"], Find(games, BracketSlot.Semifinal2).AwayTeamId);
		Assert.Equal(ids["A3"], Find(games, BracketSlot.Consolation).HomeTeamId);
		Assert.Equal(ids["B3"], Find(games, BracketSlot.Consolation).AwayTeamId);
		Assert.Null(Find(games, BracketSlot.Championship).HomeTeamId);
		Assert.Equal(3, seeded.PsoRound4Assignments.Count);
		Assert.All(seeded.PsoRound4Assignments, a => Assert.NotNull(a.HomeTeamId));

		// SF1: A1 beats B2; SF2: A2 beats B1
		Find(games, BracketSlot.Semifinal1).HomeGoals = 1;
		Find(games, BracketSlot.Semifinal1).AwayGoals = 0;
		Find(games, BracketSlot.Semifinal2).HomeGoals = 0;
		Find(games, BracketSlot.Semifinal2).AwayGoals = 2;

		var progressed = ChampionshipBracketBuilder.SeedAndProgress(division, standings, games);

		Assert.Equal(ids["B2"], Find(games, BracketSlot.ThirdPlace).HomeTeamId); // SF1 loser
		Assert.Equal(ids["B1"], Find(games, BracketSlot.ThirdPlace).AwayTeamId); // SF2 loser
		Assert.Equal(ids["A1"], Find(games, BracketSlot.Championship).HomeTeamId); // SF1 winner
		Assert.Equal(ids["A2"], Find(games, BracketSlot.Championship).AwayTeamId); // SF2 winner
		Assert.Null(progressed.ChampionTeamId);

		Find(games, BracketSlot.Championship).HomeGoals = 3;
		Find(games, BracketSlot.Championship).AwayGoals = 1;
		var complete = ChampionshipBracketBuilder.SeedAndProgress(division, standings, games);
		Assert.Equal(ids["A1"], complete.ChampionTeamId);
	}

	[Fact]
	public void ApplyPsoRound4Assignments_CopiesOpeningBracketTeamsOntoRound4Shootouts()
	{
		var championshipGames = new List<ChampionshipGame>
		{
			new()
			{
				Id = 10,
				DivisionId = 1,
				Slot = BracketSlot.Consolation,
				HomeSourceDescription = "A3",
				AwaySourceDescription = "B3",
				HomeTeamId = 3,
				AwayTeamId = 6,
			},
			new()
			{
				Id = 11,
				DivisionId = 1,
				Slot = BracketSlot.Semifinal1,
				HomeSourceDescription = "A1",
				AwaySourceDescription = "B2",
				HomeTeamId = 1,
				AwayTeamId = 5,
			},
			new()
			{
				Id = 12,
				DivisionId = 1,
				Slot = BracketSlot.Semifinal2,
				HomeSourceDescription = "B1",
				AwaySourceDescription = "A2",
				HomeTeamId = 4,
				AwayTeamId = 2,
			},
		};

		var shootouts = new List<ShootoutRound>
		{
			new() { Id = 100, DivisionId = 1, Round = 4, ChampionshipGameId = 10 },
			new() { Id = 101, DivisionId = 1, Round = 4, ChampionshipGameId = 11 },
			new() { Id = 102, DivisionId = 1, Round = 4, ChampionshipGameId = 12 },
			new() { Id = 103, DivisionId = 1, Round = 3, PoolPlayGameId = 1 }, // ignored
		};

		ChampionshipBracketBuilder.ApplyPsoRound4Assignments(shootouts, championshipGames);

		Assert.Equal(3, shootouts[0].HomeTeamId);
		Assert.Equal(6, shootouts[0].AwayTeamId);
		Assert.Equal(1, shootouts[1].HomeTeamId);
		Assert.Equal(5, shootouts[1].AwayTeamId);
		Assert.Equal(4, shootouts[2].HomeTeamId);
		Assert.Equal(2, shootouts[2].AwayTeamId);
		Assert.Null(shootouts[3].HomeTeamId);
	}

	[Fact]
	public void SeedAndProgress_EightTeam_SeedsThirdPlaceAndChampionshipFromPoolLeaders()
	{
		var division = CreateDivision(DivisionSize.Eight);
		var (standings, ids) = CreateCompletedEightTeamStandings();
		var games = ChampionshipBracketBuilder.CreateBracketShells(division.Id, DivisionSize.Eight);

		ChampionshipBracketBuilder.SeedAndProgress(division, standings, games);

		Assert.Equal(ids["A2"], Find(games, BracketSlot.ThirdPlace).HomeTeamId);
		Assert.Equal(ids["B2"], Find(games, BracketSlot.ThirdPlace).AwayTeamId);
		Assert.Equal(ids["A1"], Find(games, BracketSlot.Championship).HomeTeamId);
		Assert.Equal(ids["B1"], Find(games, BracketSlot.Championship).AwayTeamId);
	}

	[Fact]
	public void SeedAndProgress_TenTeam_UsesCombinedLeaderAsChampion()
	{
		var division = CreateDivision(DivisionSize.Ten);
		var standings = new DivisionPoolPlayStandingsResult
		{
			DivisionId = division.Id,
			DivisionName = division.Name,
			AllPoolsFinal = true,
			Pools =
			[
				new PoolPlayStandingsResult { PoolName = "A", IsFinal = true, Standings = [new() { Rank = 1, TeamId = 1 }] },
				new PoolPlayStandingsResult { PoolName = "B", IsFinal = true, Standings = [new() { Rank = 1, TeamId = 6 }] },
			],
			CombinedDivisionStandings = new PoolPlayStandingsResult
			{
				PoolName = "Division",
				IsFinal = true,
				Standings =
				[
					new PoolPlayStandingDto { Rank = 1, TeamId = 6 },
					new PoolPlayStandingDto { Rank = 2, TeamId = 1 },
				],
			},
		};

		var result = ChampionshipBracketBuilder.SeedAndProgress(division, standings, []);

		Assert.False(result.HasBracket);
		Assert.Equal(6, result.ChampionTeamId);
	}

	[Fact]
	public void SeedAndProgress_TwelveTeam_SeedsFromPoolWinnerAndRunnerUpRankings()
	{
		var division = CreateDivision(DivisionSize.Twelve);
		var standings = new DivisionPoolPlayStandingsResult
		{
			DivisionId = division.Id,
			DivisionName = division.Name,
			AllPoolsFinal = true,
			Pools =
			[
				new PoolPlayStandingsResult { PoolName = "A", IsFinal = true },
				new PoolPlayStandingsResult { PoolName = "B", IsFinal = true },
				new PoolPlayStandingsResult { PoolName = "C", IsFinal = true },
			],
			PoolWinnersStandings = new PoolPlayStandingsResult
			{
				PoolName = "Pool Winners",
				IsFinal = true,
				Standings =
				[
					new() { Rank = 1, TeamId = 101 },
					new() { Rank = 2, TeamId = 201 },
					new() { Rank = 3, TeamId = 301 },
				],
			},
			RunnersUpStandings = new PoolPlayStandingsResult
			{
				PoolName = "Runners-Up",
				IsFinal = true,
				Standings =
				[
					new() { Rank = 1, TeamId = 102 },
					new() { Rank = 2, TeamId = 202 },
					new() { Rank = 3, TeamId = 302 },
				],
			},
		};
		var games = ChampionshipBracketBuilder.CreateBracketShells(division.Id, DivisionSize.Twelve);

		ChampionshipBracketBuilder.SeedAndProgress(division, standings, games);

		Assert.Equal(301, Find(games, BracketSlot.ThirdPlace).HomeTeamId); // 3rd pool winner
		Assert.Equal(102, Find(games, BracketSlot.ThirdPlace).AwayTeamId); // best runner-up
		Assert.Equal(101, Find(games, BracketSlot.Championship).HomeTeamId);
		Assert.Equal(201, Find(games, BracketSlot.Championship).AwayTeamId);
	}

	[Fact]
	public void SeedAndProgress_WhenPoolPlayIncomplete_DoesNotSeedOpeningRounds()
	{
		var division = CreateDivision(DivisionSize.Four);
		var standings = new DivisionPoolPlayStandingsResult
		{
			DivisionId = division.Id,
			DivisionName = division.Name,
			AllPoolsFinal = false,
			Pools =
			[
				new PoolPlayStandingsResult
				{
					PoolName = "A",
					IsFinal = false,
					Standings =
					[
						new() { Rank = 1, TeamId = 1 },
						new() { Rank = 2, TeamId = 2 },
						new() { Rank = 3, TeamId = 3 },
						new() { Rank = 4, TeamId = 4 },
					],
				},
			],
		};
		var games = ChampionshipBracketBuilder.CreateBracketShells(division.Id, DivisionSize.Four);

		var result = ChampionshipBracketBuilder.SeedAndProgress(division, standings, games);

		Assert.False(result.PoolPlayFinal);
		Assert.All(games, g => Assert.Null(g.HomeTeamId));
		Assert.Null(result.ChampionTeamId);
	}

	private static Division CreateDivision(DivisionSize size) => new()
	{
		Id = 1,
		TournamentYearId = 1,
		AgeGroup = AgeGroup.U12,
		Gender = Gender.Boys,
		Name = "12U Boys",
		Size = size,
	};

	private static ChampionshipGame Find(IEnumerable<ChampionshipGame> games, BracketSlot slot) =>
		games.Single(g => g.Slot == slot);

	private static (DivisionPoolPlayStandingsResult Standings, int[] TeamIds) CreateCompletedFourTeamStandings()
	{
		var teamIds = new[] { 1, 2, 3, 4 };
		var standings = new DivisionPoolPlayStandingsResult
		{
			DivisionId = 1,
			DivisionName = "12U Boys",
			AllPoolsFinal = true,
			Pools =
			[
				new PoolPlayStandingsResult
				{
					PoolId = 1,
					PoolName = "A",
					IsFinal = true,
					Standings = teamIds
						.Select((id, index) => new PoolPlayStandingDto
						{
							Rank = index + 1,
							TeamId = id,
							TeamName = $"Team {id}",
							PoolName = "A",
						})
						.ToList(),
				},
			],
		};
		return (standings, teamIds);
	}

	private static (DivisionPoolPlayStandingsResult Standings, Dictionary<string, int> Ids) CreateCompletedSixTeamStandings()
	{
		var ids = new Dictionary<string, int>
		{
			["A1"] = 1,
			["A2"] = 2,
			["A3"] = 3,
			["B1"] = 4,
			["B2"] = 5,
			["B3"] = 6,
		};

		var standings = new DivisionPoolPlayStandingsResult
		{
			DivisionId = 1,
			DivisionName = "12U Boys",
			AllPoolsFinal = true,
			Pools =
			[
				Pool("A", [ids["A1"], ids["A2"], ids["A3"]]),
				Pool("B", [ids["B1"], ids["B2"], ids["B3"]]),
			],
		};
		return (standings, ids);
	}

	private static (DivisionPoolPlayStandingsResult Standings, Dictionary<string, int> Ids) CreateCompletedEightTeamStandings()
	{
		var ids = new Dictionary<string, int>
		{
			["A1"] = 1,
			["A2"] = 2,
			["B1"] = 5,
			["B2"] = 6,
		};

		var standings = new DivisionPoolPlayStandingsResult
		{
			DivisionId = 1,
			DivisionName = "12U Boys",
			AllPoolsFinal = true,
			Pools =
			[
				Pool("A", [ids["A1"], ids["A2"], 3, 4]),
				Pool("B", [ids["B1"], ids["B2"], 7, 8]),
			],
		};
		return (standings, ids);
	}

	private static PoolPlayStandingsResult Pool(string name, IReadOnlyList<int> rankedTeamIds) => new()
	{
		PoolName = name,
		IsFinal = true,
		Standings = rankedTeamIds
			.Select((id, index) => new PoolPlayStandingDto
			{
				Rank = index + 1,
				TeamId = id,
				TeamName = $"{name}{index + 1}",
				PoolName = name,
			})
			.ToList(),
	};
}
