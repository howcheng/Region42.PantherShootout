using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Async application service for managing tournament years, resolving the most recent year for
/// public visitors, and maintaining division custom bulletins.
/// </summary>
public class TournamentYearService : ITournamentYearService
{
	public const int MinSupportedYear = 2000;
	public const int MaxSupportedYear = 2100;

	private readonly ITournamentDataContext _dataContext;

	public TournamentYearService(ITournamentDataContext dataContext)
	{
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
	}

	public static string GetDefaultTournamentName(int year)
	{
		return $"{year} Panther Shootout";
	}

	public async Task<IReadOnlyList<TournamentYearDto>> GetAllYearsAsync(CancellationToken cancellationToken = default)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);
		return BuildYearDtos();
	}

	public async Task<TournamentYearDto?> GetDefaultYearAsync(CancellationToken cancellationToken = default)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var years = BuildYearDtos();
		return years.FirstOrDefault(y => y.DivisionCount > 0) ?? years.FirstOrDefault();
	}

	public Task<TournamentYearDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
		return GetByIdInternalAsync(id, cancellationToken);
	}

	private async Task<TournamentYearDto?> GetByIdInternalAsync(int id, CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);
		return BuildYearDtos().FirstOrDefault(y => y.Id == id);
	}

	public Task<TournamentYearDto?> GetByYearNumberAsync(int year, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(year, MinSupportedYear);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(year, MaxSupportedYear);
		return GetByYearNumberInternalAsync(year, cancellationToken);
	}

	private async Task<TournamentYearDto?> GetByYearNumberInternalAsync(int year, CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);
		return BuildYearDtos().FirstOrDefault(y => y.Year == year);
	}

	public Task<TournamentYearDto> CreateYearAsync(
		TournamentYearCreateDto input,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentOutOfRangeException.ThrowIfLessThan(input.Year, MinSupportedYear, nameof(input.Year));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Year, MaxSupportedYear, nameof(input.Year));

		return CreateYearInternalAsync(input, cancellationToken);
	}

	private async Task<TournamentYearDto> CreateYearInternalAsync(
		TournamentYearCreateDto input,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var duplicate = _dataContext.TournamentYears.FirstOrDefault(y => y.Year == input.Year);
		if (duplicate is not null)
		{
			throw new InvalidOperationException(
				$"Tournament year {input.Year} already exists ('{duplicate.Name}').");
		}

		var resolvedName = string.IsNullOrWhiteSpace(input.Name)
			? GetDefaultTournamentName(input.Year)
			: input.Name.Trim();

		var normalizedMessage = NormalizeOptionalMessage(input.CustomMessage);

		await using var tx = await _dataContext.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

		if (input.IsActive)
		{
			DeactivateOtherYears(excludeYearId: null);
		}

		var created = new TournamentYear
		{
			Id = _dataContext.AllocateNextId(),
			Year = input.Year,
			Name = resolvedName,
			IsActive = input.IsActive,
			CustomMessage = normalizedMessage,
		};

		_dataContext.Add(created);
		await _dataContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

		return BuildYearDtos().First(y => y.Id == created.Id);
	}

	public Task<TournamentYearDto> UpdateYearAsync(
		TournamentYearUpdateDto input,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.Id);
		ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);

		return UpdateYearInternalAsync(input, cancellationToken);
	}

	private async Task<TournamentYearDto> UpdateYearInternalAsync(
		TournamentYearUpdateDto input,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var existing = _dataContext.TournamentYears.FirstOrDefault(y => y.Id == input.Id)
			?? throw new InvalidOperationException($"Tournament year {input.Id} was not found.");

		await using var tx = await _dataContext.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

		if (input.IsActive)
		{
			DeactivateOtherYears(excludeYearId: existing.Id);
		}

		existing.Name = input.Name.Trim();
		existing.IsActive = input.IsActive;
		existing.CustomMessage = NormalizeOptionalMessage(input.CustomMessage);

		_dataContext.Update(existing);
		await _dataContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

		return BuildYearDtos().First(y => y.Id == existing.Id);
	}

	public Task<IReadOnlyList<DivisionSummaryDto>> GetDivisionsForYearAsync(
		int tournamentYearId,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tournamentYearId);
		return GetDivisionsForYearInternalAsync(tournamentYearId, cancellationToken);
	}

	private async Task<IReadOnlyList<DivisionSummaryDto>> GetDivisionsForYearInternalAsync(
		int tournamentYearId,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var year = _dataContext.TournamentYears.FirstOrDefault(y => y.Id == tournamentYearId);
		if (year is null)
		{
			return Array.Empty<DivisionSummaryDto>();
		}

		return _dataContext.Divisions
			.Where(d => d.TournamentYearId == tournamentYearId)
			.OrderBy(d => d.AgeGroup)
			.ThenBy(d => d.Gender)
			.Select(d => MapDivisionSummary(d, year.Year))
			.ToList();
	}

	public Task<DivisionSummaryDto?> GetDivisionByIdAsync(
		int divisionId,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);
		return GetDivisionByIdInternalAsync(divisionId, cancellationToken);
	}

	private async Task<DivisionSummaryDto?> GetDivisionByIdInternalAsync(
		int divisionId,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var division = _dataContext.Divisions.FirstOrDefault(d => d.Id == divisionId);
		if (division is null)
		{
			return null;
		}

		var year = _dataContext.TournamentYears.FirstOrDefault(y => y.Id == division.TournamentYearId)?.Year ?? 0;
		return MapDivisionSummary(division, year);
	}

	public Task<DivisionSummaryDto> UpdateDivisionCustomMessageAsync(
		DivisionBulletinUpdateDto input,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.DivisionId);

		return UpdateDivisionCustomMessageInternalAsync(input, cancellationToken);
	}

	private async Task<DivisionSummaryDto> UpdateDivisionCustomMessageInternalAsync(
		DivisionBulletinUpdateDto input,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var division = _dataContext.Divisions.FirstOrDefault(d => d.Id == input.DivisionId)
			?? throw new InvalidOperationException($"Division {input.DivisionId} was not found.");

		division.CustomMessage = NormalizeOptionalMessage(input.CustomMessage);
		_dataContext.Update(division);
		await _dataContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		var year = _dataContext.TournamentYears.FirstOrDefault(y => y.Id == division.TournamentYearId)?.Year ?? 0;
		return MapDivisionSummary(division, year);
	}

	private List<TournamentYearDto> BuildYearDtos()
	{
		var divisionsByYear = _dataContext.Divisions
			.GroupBy(d => d.TournamentYearId)
			.ToDictionary(g => g.Key, g => g.ToList());

		var poolsByDivision = _dataContext.Pools
			.GroupBy(p => p.DivisionId)
			.ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToHashSet());

		var teamCountsByPool = _dataContext.Teams
			.GroupBy(t => t.PoolId)
			.ToDictionary(g => g.Key, g => g.Count());

		return _dataContext.TournamentYears
			.OrderByDescending(y => y.Year)
			.ThenByDescending(y => y.Id)
			.Select(y =>
			{
				var yearDivisions = divisionsByYear.GetValueOrDefault(y.Id) ?? [];
				var teamCount = 0;
				foreach (var div in yearDivisions)
				{
					if (poolsByDivision.TryGetValue(div.Id, out var poolIds))
					{
						foreach (var poolId in poolIds)
						{
							teamCount += teamCountsByPool.GetValueOrDefault(poolId);
						}
					}
				}

				return new TournamentYearDto
				{
					Id = y.Id,
					Year = y.Year,
					Name = y.Name,
					IsActive = y.IsActive,
					CustomMessage = y.CustomMessage,
					DivisionCount = yearDivisions.Count,
					TeamCount = teamCount,
					ModifiedAt = y.ModifiedAt,
					ModifiedBy = y.ModifiedBy,
				};
			})
			.ToList();
	}

	private DivisionSummaryDto MapDivisionSummary(Division division, int year)
	{
		var pools = _dataContext.Pools.Where(p => p.DivisionId == division.Id).ToList();
		var poolIds = pools.Select(p => p.Id).ToHashSet();
		var teamCount = _dataContext.Teams.Count(t => poolIds.Contains(t.PoolId));

		return new DivisionSummaryDto
		{
			Id = division.Id,
			TournamentYearId = division.TournamentYearId,
			Year = year,
			DivisionKey = DivisionKeyHelper.ToKey(division.AgeGroup, division.Gender),
			ShortCode = DivisionKeyHelper.ToShortCode(division.AgeGroup, division.Gender),
			Name = division.Name,
			AgeGroup = division.AgeGroup,
			Gender = division.Gender,
			Size = division.Size,
			PoolCount = pools.Count,
			TeamCount = teamCount,
			CustomMessage = division.CustomMessage,
		};
	}

	private void DeactivateOtherYears(int? excludeYearId)
	{
		foreach (var other in _dataContext.TournamentYears.Where(y => y.IsActive && y.Id != excludeYearId).ToList())
		{
			other.IsActive = false;
			_dataContext.Update(other);
		}
	}

	private static string? NormalizeOptionalMessage(string? message)
	{
		return string.IsNullOrWhiteSpace(message) ? null : message.Trim();
	}
}
