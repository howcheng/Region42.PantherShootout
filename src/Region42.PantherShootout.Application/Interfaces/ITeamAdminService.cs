using Region42.PantherShootout.Application.DTOs;

namespace Region42.PantherShootout.Application.Interfaces;

/// <summary>
/// Application service for viewing and editing division team rosters (region numbers, coach names,
/// and host AYSO Region 42 volunteer point deductions) while keeping team codes read-only.
/// </summary>
public interface ITeamAdminService
{
	Task<DivisionTeamsDto?> GetDivisionTeamsAsync(
		int divisionId,
		CancellationToken cancellationToken = default);

	Task<DivisionTeamsDto> UpdateDivisionTeamsAsync(
		DivisionTeamsUpdateDto input,
		CancellationToken cancellationToken = default);
}
