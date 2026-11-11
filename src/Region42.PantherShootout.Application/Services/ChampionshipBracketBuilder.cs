using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Pure championship-bracket shell definitions, standings-based seeding, semifinal progression,
/// and 6-team PSO Round 4 pairing sync per <c>docs/BusinessRules.md</c>.
/// </summary>
public static class ChampionshipBracketBuilder
{
	public const int PsoRound4 = 4;

	/// <summary>
	/// Returns the bracket slot shells for a division size (empty for 5- and 10-team divisions).
	/// </summary>
	public static IReadOnlyList<ChampionshipBracketSlotDefinition> GetSlotDefinitions(DivisionSize size) =>
		size switch
		{
			DivisionSize.Four =>
			[
				Opening(BracketSlot.Consolation, "3rd place", "4th place"),
				Opening(BracketSlot.Championship, "1st place", "2nd place"),
			],
			DivisionSize.Six =>
			[
				Opening(BracketSlot.Consolation, "Pool A 3rd place", "Pool B 3rd place"),
				Opening(BracketSlot.Semifinal1, "Pool A 1st place", "Pool B 2nd place"),
				Opening(BracketSlot.Semifinal2, "Pool B 1st place", "Pool A 2nd place"),
				Progression(BracketSlot.ThirdPlace, "Loser Semifinal 1", "Loser Semifinal 2"),
				Progression(BracketSlot.Championship, "Winner Semifinal 1", "Winner Semifinal 2"),
			],
			DivisionSize.Eight =>
			[
				Opening(BracketSlot.ThirdPlace, "Pool A 2nd place", "Pool B 2nd place"),
				Opening(BracketSlot.Championship, "Pool A 1st place", "Pool B 1st place"),
			],
			DivisionSize.Twelve =>
			[
				Opening(BracketSlot.ThirdPlace, "3rd-ranked pool winner", "1st-ranked runner-up"),
				Opening(BracketSlot.Championship, "1st-ranked pool winner", "2nd-ranked pool winner"),
			],
			DivisionSize.Five or DivisionSize.Ten => Array.Empty<ChampionshipBracketSlotDefinition>(),
			_ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported division size."),
		};

	/// <summary>
	/// Creates unsaved <see cref="ChampionshipGame"/> shells for a division (no team IDs).
	/// </summary>
	public static List<ChampionshipGame> CreateBracketShells(int divisionId, DivisionSize size)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);

		return GetSlotDefinitions(size)
			.Select(definition => new ChampionshipGame
			{
				DivisionId = divisionId,
				Slot = definition.Slot,
				HomeSourceDescription = definition.HomeSourceDescription,
				AwaySourceDescription = definition.AwaySourceDescription,
			})
			.ToList();
	}

	/// <summary>
	/// Seeds opening-round team IDs from pool-play standings, progresses dependent slots
	/// (6-team SF → Final / 3rd place), resolves the division champion when known, and
	/// builds 6-team PSO Round 4 team assignments for the opening bracket games.
	/// </summary>
	/// <remarks>
	/// Mutates <paramref name="championshipGames"/> team IDs in place when seeding/progression
	/// can resolve them. Does not create or delete games.
	/// </remarks>
	public static ChampionshipBracketResult SeedAndProgress(
		Division division,
		DivisionPoolPlayStandingsResult poolStandings,
		IList<ChampionshipGame> championshipGames)
	{
		ArgumentNullException.ThrowIfNull(division);
		ArgumentNullException.ThrowIfNull(poolStandings);
		ArgumentNullException.ThrowIfNull(championshipGames);

		var hasBracket = DivisionSizeRules.HasChampionshipBracket(division.Size);
		var poolPlayFinal = poolStandings.AllPoolsFinal
			&& (poolStandings.CombinedDivisionStandings is null || poolStandings.CombinedDivisionStandings.IsFinal);

		if (hasBracket && poolPlayFinal)
		{
			SeedOpeningRounds(division.Size, poolStandings, championshipGames);
		}

		if (division.Size == DivisionSize.Six)
		{
			ProgressSixTeamBracket(championshipGames);
		}

		var gameDtos = championshipGames
			.OrderBy(g => SlotSortOrder(g.Slot))
			.Select(ToGameDto)
			.ToList();

		var result = new ChampionshipBracketResult
		{
			DivisionId = division.Id,
			DivisionName = division.Name,
			Size = division.Size,
			HasBracket = hasBracket,
			PoolPlayFinal = poolPlayFinal,
			Games = gameDtos,
			ChampionTeamId = ResolveChampionTeamId(division.Size, poolPlayFinal, poolStandings, gameDtos),
			PsoRound4Assignments = division.Size == DivisionSize.Six
				? BuildPsoRound4Assignments(championshipGames)
				: [],
		};

		return result;
	}

	/// <summary>
	/// Copies opening-bracket team IDs onto existing Round 4 <see cref="ShootoutRound"/> rows
	/// linked to Consolation / Semifinal1 / Semifinal2 championship games.
	/// </summary>
	public static void ApplyPsoRound4Assignments(
		IEnumerable<ShootoutRound> shootoutRounds,
		IEnumerable<ChampionshipGame> championshipGames)
	{
		ArgumentNullException.ThrowIfNull(shootoutRounds);
		ArgumentNullException.ThrowIfNull(championshipGames);

		var openingById = championshipGames
			.Where(g => g.Slot is BracketSlot.Consolation or BracketSlot.Semifinal1 or BracketSlot.Semifinal2)
			.ToDictionary(g => g.Id);

		foreach (var round in shootoutRounds.Where(r => r.Round == PsoRound4 && r.ChampionshipGameId is not null))
		{
			if (!openingById.TryGetValue(round.ChampionshipGameId!.Value, out var game))
			{
				continue;
			}

			round.HomeTeamId = game.HomeTeamId;
			round.AwayTeamId = game.AwayTeamId;
		}
	}

	private static void SeedOpeningRounds(
		DivisionSize size,
		DivisionPoolPlayStandingsResult poolStandings,
		IList<ChampionshipGame> championshipGames)
	{
		switch (size)
		{
			case DivisionSize.Four:
				SeedPair(championshipGames, BracketSlot.Consolation,
					RequirePoolRank(poolStandings, poolIndex: 0, rank: 3),
					RequirePoolRank(poolStandings, poolIndex: 0, rank: 4));
				SeedPair(championshipGames, BracketSlot.Championship,
					RequirePoolRank(poolStandings, poolIndex: 0, rank: 1),
					RequirePoolRank(poolStandings, poolIndex: 0, rank: 2));
				break;

			case DivisionSize.Six:
				SeedPair(championshipGames, BracketSlot.Consolation,
					RequirePoolRank(poolStandings, poolName: "A", rank: 3),
					RequirePoolRank(poolStandings, poolName: "B", rank: 3));
				SeedPair(championshipGames, BracketSlot.Semifinal1,
					RequirePoolRank(poolStandings, poolName: "A", rank: 1),
					RequirePoolRank(poolStandings, poolName: "B", rank: 2));
				SeedPair(championshipGames, BracketSlot.Semifinal2,
					RequirePoolRank(poolStandings, poolName: "B", rank: 1),
					RequirePoolRank(poolStandings, poolName: "A", rank: 2));
				break;

			case DivisionSize.Eight:
				SeedPair(championshipGames, BracketSlot.ThirdPlace,
					RequirePoolRank(poolStandings, poolName: "A", rank: 2),
					RequirePoolRank(poolStandings, poolName: "B", rank: 2));
				SeedPair(championshipGames, BracketSlot.Championship,
					RequirePoolRank(poolStandings, poolName: "A", rank: 1),
					RequirePoolRank(poolStandings, poolName: "B", rank: 1));
				break;

			case DivisionSize.Twelve:
				SeedPair(championshipGames, BracketSlot.ThirdPlace,
					RequireGroupRank(poolStandings.PoolWinnersStandings, rank: 3, "pool winners"),
					RequireGroupRank(poolStandings.RunnersUpStandings, rank: 1, "runners-up"));
				SeedPair(championshipGames, BracketSlot.Championship,
					RequireGroupRank(poolStandings.PoolWinnersStandings, rank: 1, "pool winners"),
					RequireGroupRank(poolStandings.PoolWinnersStandings, rank: 2, "pool winners"));
				break;
		}
	}

	private static void ProgressSixTeamBracket(IList<ChampionshipGame> championshipGames)
	{
		var semifinal1 = FindGame(championshipGames, BracketSlot.Semifinal1);
		var semifinal2 = FindGame(championshipGames, BracketSlot.Semifinal2);
		if (semifinal1 is null || semifinal2 is null)
		{
			return;
		}

		var sf1 = ToGameDto(semifinal1);
		var sf2 = ToGameDto(semifinal2);
		if (!sf1.HasDeterminedWinner || !sf2.HasDeterminedWinner)
		{
			return;
		}

		SeedPair(championshipGames, BracketSlot.ThirdPlace, sf1.LoserTeamId, sf2.LoserTeamId);
		SeedPair(championshipGames, BracketSlot.Championship, sf1.WinnerTeamId, sf2.WinnerTeamId);
	}

	private static List<PsoRound4AssignmentDto> BuildPsoRound4Assignments(
		IEnumerable<ChampionshipGame> championshipGames) =>
		championshipGames
			.Where(g => g.Slot is BracketSlot.Consolation or BracketSlot.Semifinal1 or BracketSlot.Semifinal2)
			.OrderBy(g => SlotSortOrder(g.Slot))
			.Select(g => new PsoRound4AssignmentDto
			{
				ChampionshipSlot = g.Slot,
				ChampionshipGameId = g.Id == 0 ? null : g.Id,
				HomeTeamId = g.HomeTeamId,
				AwayTeamId = g.AwayTeamId,
			})
			.ToList();

	private static int? ResolveChampionTeamId(
		DivisionSize size,
		bool poolPlayFinal,
		DivisionPoolPlayStandingsResult poolStandings,
		IReadOnlyList<ChampionshipGameDto> games)
	{
		if (!DivisionSizeRules.HasChampionshipBracket(size))
		{
			if (!poolPlayFinal)
			{
				return null;
			}

			if (size == DivisionSize.Ten)
			{
				return poolStandings.CombinedDivisionStandings?.Standings.FirstOrDefault()?.TeamId;
			}

			return poolStandings.Pools.FirstOrDefault()?.Standings.FirstOrDefault()?.TeamId;
		}

		var championship = games.FirstOrDefault(g => g.Slot == BracketSlot.Championship);
		return championship is { HasDeterminedWinner: true } ? championship.WinnerTeamId : null;
	}

	private static void SeedPair(
		IList<ChampionshipGame> games,
		BracketSlot slot,
		int? homeTeamId,
		int? awayTeamId)
	{
		var game = FindGame(games, slot);
		if (game is null)
		{
			return;
		}

		game.HomeTeamId = homeTeamId;
		game.AwayTeamId = awayTeamId;
	}

	private static ChampionshipGame? FindGame(IEnumerable<ChampionshipGame> games, BracketSlot slot) =>
		games.FirstOrDefault(g => g.Slot == slot);

	private static int RequirePoolRank(DivisionPoolPlayStandingsResult standings, int poolIndex, int rank)
	{
		if (poolIndex < 0 || poolIndex >= standings.Pools.Count)
		{
			throw new InvalidOperationException($"Pool index {poolIndex} is out of range for division standings.");
		}

		return RequireRank(standings.Pools[poolIndex], rank, $"pool index {poolIndex}");
	}

	private static int RequirePoolRank(DivisionPoolPlayStandingsResult standings, string poolName, int rank)
	{
		var pool = standings.Pools.FirstOrDefault(p =>
			string.Equals(p.PoolName, poolName, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException($"Pool '{poolName}' was not found in standings.");

		return RequireRank(pool, rank, $"pool {poolName}");
	}

	private static int RequireGroupRank(PoolPlayStandingsResult? group, int rank, string groupName)
	{
		if (group is null)
		{
			throw new InvalidOperationException($"Missing {groupName} standings for 12-team bracket seeding.");
		}

		return RequireRank(group, rank, groupName);
	}

	private static int RequireRank(PoolPlayStandingsResult pool, int rank, string scope)
	{
		var row = pool.Standings.FirstOrDefault(s => s.Rank == rank)
			?? throw new InvalidOperationException($"Could not find rank {rank} in {scope} standings.");
		return row.TeamId;
	}

	private static ChampionshipGameDto ToGameDto(ChampionshipGame game)
	{
		var winner = ResolveWinnerTeamId(game);
		var loser = winner is null
			? null
			: game.HomeTeamId == winner ? game.AwayTeamId : game.HomeTeamId;

		return new ChampionshipGameDto
		{
			GameId = game.Id == 0 ? null : game.Id,
			Slot = game.Slot,
			HomeSourceDescription = game.HomeSourceDescription,
			AwaySourceDescription = game.AwaySourceDescription,
			HomeTeamId = game.HomeTeamId,
			AwayTeamId = game.AwayTeamId,
			HomeGoals = game.HomeGoals,
			AwayGoals = game.AwayGoals,
			IsForfeit = game.IsForfeit,
			WinnerTeamId = winner,
			LoserTeamId = loser,
		};
	}

	private static int? ResolveWinnerTeamId(ChampionshipGame game)
	{
		if (game.HomeTeamId is null || game.AwayTeamId is null)
		{
			return null;
		}

		return game.GetWinner() switch
		{
			GameWinner.Home => game.HomeTeamId,
			GameWinner.Away => game.AwayTeamId,
			_ => null,
		};
	}

	private static ChampionshipBracketSlotDefinition Opening(
		BracketSlot slot,
		string homeSource,
		string awaySource) => new()
	{
		Slot = slot,
		HomeSourceDescription = homeSource,
		AwaySourceDescription = awaySource,
		IsOpeningRound = true,
	};

	private static ChampionshipBracketSlotDefinition Progression(
		BracketSlot slot,
		string homeSource,
		string awaySource) => new()
	{
		Slot = slot,
		HomeSourceDescription = homeSource,
		AwaySourceDescription = awaySource,
		IsOpeningRound = false,
	};

	private static int SlotSortOrder(BracketSlot slot) => slot switch
	{
		BracketSlot.Consolation => 1,
		BracketSlot.Semifinal1 => 2,
		BracketSlot.Semifinal2 => 3,
		BracketSlot.ThirdPlace => 4,
		BracketSlot.Championship => 5,
		_ => 99,
	};
}
