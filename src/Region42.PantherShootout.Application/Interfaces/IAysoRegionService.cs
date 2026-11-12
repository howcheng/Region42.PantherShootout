using Region42.PantherShootout.Application.DTOs;

namespace Region42.PantherShootout.Application.Interfaces;

/// <summary>
/// Manages year-independent AYSO region number-to-name mappings persisted in <c>regions.json</c>.
/// </summary>
public interface IAysoRegionService
{
	Task<IReadOnlyList<AysoRegionDto>> GetAllRegionsAsync(CancellationToken cancellationToken = default);

	Task<AysoRegionDto> UpsertRegionAsync(
		AysoRegionUpsertDto input,
		CancellationToken cancellationToken = default);

	Task DeleteRegionAsync(int regionId, CancellationToken cancellationToken = default);
}
