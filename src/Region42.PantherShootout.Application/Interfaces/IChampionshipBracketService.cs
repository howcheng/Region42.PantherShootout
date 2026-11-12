using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;

namespace Region42.PantherShootout.Application.Interfaces;

/// <summary>
/// Creates championship bracket shells and seeds/progresses bracket games from pool-play standings.
/// </summary>
public interface IChampionshipBracketService
{
	/// <summary>
	/// Returns static slot definitions for a division size (empty for 5- and 10-team divisions).
	/// </summary>
	IReadOnlyList<ChampionshipBracketSlotDefinition> GetSlotDefinitions(
		DivisionSize size,
		IReadOnlyList<string>? poolNames = null);

	/// <summary>
	/// Creates unsaved championship game shells for the division (no team IDs assigned yet).
	/// </summary>
	IReadOnlyList<ChampionshipGame> CreateBracketShells(
		int divisionId,
		DivisionSize size,
		IReadOnlyList<string>? poolNames = null);

	/// <summary>
	/// Loads division standings/games, seeds opening bracket slots when pool play is final,
	/// progresses 6-team semifinal results into Final/3rd place, syncs PSO Round 4 team pairings,
	/// and returns the resulting bracket state. Mutates tracked entities; does not call SaveChanges.
	/// </summary>
	Task<ChampionshipBracketResult> SeedAndProgressAsync(
		int divisionId,
		CancellationToken cancellationToken = default);
}
