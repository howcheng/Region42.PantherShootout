using Region42.PantherShootout.Application.DTOs;

namespace Region42.PantherShootout.Application.Interfaces;

/// <summary>
/// Manages tournament years (seasons), resolves the default/most-recent tournament year for
/// public views, and updates division bulletin messages.
/// </summary>
public interface ITournamentYearService
{
	Task<IReadOnlyList<TournamentYearDto>> GetAllYearsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves the default tournament year for public visitors: prefers the most recent year
	/// (<c>Year</c> descending) that has imported divisions, falling back to the highest <c>Year</c> overall.
	/// </summary>
	Task<TournamentYearDto?> GetDefaultYearAsync(CancellationToken cancellationToken = default);

	Task<TournamentYearDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

	Task<TournamentYearDto?> GetByYearNumberAsync(int year, CancellationToken cancellationToken = default);

	Task<TournamentYearDto> CreateYearAsync(
		TournamentYearCreateDto input,
		CancellationToken cancellationToken = default);

	Task<TournamentYearDto> UpdateYearAsync(
		TournamentYearUpdateDto input,
		CancellationToken cancellationToken = default);

	Task<IReadOnlyList<DivisionSummaryDto>> GetDivisionsForYearAsync(
		int tournamentYearId,
		CancellationToken cancellationToken = default);

	Task<DivisionSummaryDto?> GetDivisionByIdAsync(
		int divisionId,
		CancellationToken cancellationToken = default);

	Task<DivisionSummaryDto> UpdateDivisionCustomMessageAsync(
		DivisionBulletinUpdateDto input,
		CancellationToken cancellationToken = default);
}
