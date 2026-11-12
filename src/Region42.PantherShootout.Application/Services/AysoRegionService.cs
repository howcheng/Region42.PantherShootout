using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Async application service for viewing, adding, renaming, and deleting global AYSO region
/// mappings stored in <c>panther-shootout/regions.json</c>.
/// </summary>
public class AysoRegionService : IAysoRegionService
{
	private readonly ITournamentDataContext _dataContext;

	public AysoRegionService(ITournamentDataContext dataContext)
	{
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
	}

	public async Task<IReadOnlyList<AysoRegionDto>> GetAllRegionsAsync(CancellationToken cancellationToken = default)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var usageByRegionNumber = _dataContext.Teams
			.Where(t => t.RegionNumber > 0)
			.GroupBy(t => t.RegionNumber)
			.ToDictionary(g => g.Key, g => g.Count());

		return _dataContext.Regions
			.OrderBy(r => r.RegionNumber)
			.Select(r => ToDto(r, usageByRegionNumber.GetValueOrDefault(r.RegionNumber)))
			.ToList();
	}

	public Task<AysoRegionDto> UpsertRegionAsync(
		AysoRegionUpsertDto input,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.RegionNumber);
		ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);

		return UpsertRegionInternalAsync(input, cancellationToken);
	}

	private async Task<AysoRegionDto> UpsertRegionInternalAsync(
		AysoRegionUpsertDto input,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var normalizedName = input.Name.Trim();
		AysoRegion entity;

		if (input.Id is > 0)
		{
			entity = _dataContext.Regions.FirstOrDefault(r => r.Id == input.Id.Value)
				?? throw new InvalidOperationException($"AYSO Region {input.Id.Value} was not found.");

			var conflicting = _dataContext.Regions.FirstOrDefault(r =>
				r.RegionNumber == input.RegionNumber && r.Id != entity.Id);
			if (conflicting is not null)
			{
				throw new InvalidOperationException(
					$"AYSO Region {input.RegionNumber} already exists ('{conflicting.Name}').");
			}

			entity.RegionNumber = input.RegionNumber;
			entity.Name = normalizedName;
			_dataContext.Update(entity);
		}
		else
		{
			var existingByNumber = _dataContext.Regions.FirstOrDefault(r => r.RegionNumber == input.RegionNumber);
			if (existingByNumber is not null)
			{
				existingByNumber.Name = normalizedName;
				_dataContext.Update(existingByNumber);
				entity = existingByNumber;
			}
			else
			{
				entity = new AysoRegion
				{
					Id = _dataContext.AllocateNextId(),
					RegionNumber = input.RegionNumber,
					Name = normalizedName,
				};
				_dataContext.Add(entity);
			}
		}

		await _dataContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		var usageCount = _dataContext.Teams.Count(t => t.RegionNumber == entity.RegionNumber);
		return ToDto(entity, usageCount);
	}

	public Task DeleteRegionAsync(int regionId, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(regionId);
		return DeleteRegionInternalAsync(regionId, cancellationToken);
	}

	private async Task DeleteRegionInternalAsync(int regionId, CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var existing = _dataContext.Regions.FirstOrDefault(r => r.Id == regionId)
			?? throw new InvalidOperationException($"AYSO Region {regionId} was not found.");

		var usageCount = _dataContext.Teams.Count(t => t.RegionNumber == existing.RegionNumber);
		if (usageCount > 0)
		{
			throw new InvalidOperationException(
				$"Cannot delete AYSO Region {existing.RegionNumber} ('{existing.Name}') because {usageCount} team(s) reference it.");
		}

		_dataContext.Remove(existing);
		await _dataContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	private static AysoRegionDto ToDto(AysoRegion entity, int usageCount) => new()
	{
		Id = entity.Id,
		RegionNumber = entity.RegionNumber,
		Name = entity.Name,
		TeamUsageCount = usageCount,
	};
}
