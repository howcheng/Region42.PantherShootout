using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Helpers;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Application.Services;

/// <summary>
/// Async application service for editing division teams (AYSO region number, coach name, and
/// Region 42 volunteer standings point deduction) while preserving immutable team codes and
/// refreshing division standings and brackets on update.
/// </summary>
public class TeamAdminService : ITeamAdminService
{
	public const int MaxCoachNameLength = 100;

	private readonly ITournamentDataContext _dataContext;
	private readonly IStandingsRefreshService _standingsRefreshService;

	public TeamAdminService(
		ITournamentDataContext dataContext,
		IStandingsRefreshService standingsRefreshService)
	{
		_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
		_standingsRefreshService = standingsRefreshService ?? throw new ArgumentNullException(nameof(standingsRefreshService));
	}

	public Task<DivisionTeamsDto?> GetDivisionTeamsAsync(
		int divisionId,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisionId);
		return GetDivisionTeamsInternalAsync(divisionId, cancellationToken);
	}

	private async Task<DivisionTeamsDto?> GetDivisionTeamsInternalAsync(
		int divisionId,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var division = _dataContext.Divisions.FirstOrDefault(d => d.Id == divisionId);
		if (division is null)
		{
			return null;
		}

		return BuildDivisionTeamsDto(division);
	}

	private DivisionTeamsDto BuildDivisionTeamsDto(Division division)
	{
		var year = _dataContext.TournamentYears.FirstOrDefault(y => y.Id == division.TournamentYearId)?.Year ?? 0;
		var regionsByNumber = _dataContext.Regions
			.GroupBy(r => r.RegionNumber)
			.ToDictionary(g => g.Key, g => g.First().Name);

		var pools = _dataContext.Pools
			.Where(p => p.DivisionId == division.Id)
			.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var poolDtos = new List<PoolTeamsDto>(pools.Count);
		foreach (var pool in pools)
		{
			var poolTeams = _dataContext.Teams
				.Where(t => t.PoolId == pool.Id)
				.OrderBy(t => t.TeamNumber)
				.ThenBy(t => t.Id)
				.Select(team =>
				{
					var regionName = !string.IsNullOrWhiteSpace(team.RegionName)
						? team.RegionName
						: (regionsByNumber.GetValueOrDefault(team.RegionNumber) ?? string.Empty);

					var code = !string.IsNullOrWhiteSpace(team.Pool?.Name)
						? team.Code
						: $"{pool.Name}{team.TeamNumber}";

					return new TeamAdminItemDto
					{
						Id = team.Id,
						PoolId = pool.Id,
						PoolName = pool.Name,
						TeamNumber = team.TeamNumber,
						Code = code,
						RegionNumber = team.RegionNumber,
						RegionName = regionName,
						CoachName = team.CoachName,
						DisplayName = team.Name,
						IsHostRegion = team.IsHostRegion,
						HasVolunteerPointDeduction = team.IsHostRegion && team.HasVolunteerPointDeduction,
					};
				})
				.ToList();

			poolDtos.Add(new PoolTeamsDto
			{
				PoolId = pool.Id,
				PoolName = pool.Name,
				Teams = poolTeams,
			});
		}

		return new DivisionTeamsDto
		{
			DivisionId = division.Id,
			TournamentYearId = division.TournamentYearId,
			Year = year,
			DivisionKey = DivisionKeyHelper.ToKey(division.AgeGroup, division.Gender),
			ShortCode = DivisionKeyHelper.ToShortCode(division.AgeGroup, division.Gender),
			DivisionName = division.Name,
			Size = division.Size,
			Pools = poolDtos,
		};
	}

	public Task<DivisionTeamsDto> UpdateDivisionTeamsAsync(
		DivisionTeamsUpdateDto input,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.DivisionId);
		ArgumentNullException.ThrowIfNull(input.Teams);

		if (input.Teams.Count == 0)
		{
			throw new ArgumentException("At least one team update entry is required.", nameof(input));
		}

		var seenTeamIds = new HashSet<int>();
		foreach (var item in input.Teams)
		{
			if (item is null)
			{
				throw new ArgumentException("Team update entries cannot be null.", nameof(input));
			}

			if (item.TeamId <= 0)
			{
				throw new ArgumentException("Each team update entry must specify a valid positive TeamId.", nameof(input));
			}

			if (!seenTeamIds.Add(item.TeamId))
			{
				throw new ArgumentException($"Duplicate TeamId {item.TeamId} in update request.", nameof(input));
			}

			if (item.RegionNumber <= 0)
			{
				throw new ArgumentException($"Team {item.TeamId} must specify a valid positive AYSO region number.", nameof(input));
			}

			if (string.IsNullOrWhiteSpace(item.CoachName))
			{
				throw new ArgumentException($"Coach name is required for team {item.TeamId}.", nameof(input));
			}

			if (item.CoachName.Trim().Length > MaxCoachNameLength)
			{
				throw new ArgumentException(
					$"Coach name for team {item.TeamId} cannot exceed {MaxCoachNameLength} characters.",
					nameof(input));
			}
		}

		return UpdateDivisionTeamsInternalAsync(input, cancellationToken);
	}

	private async Task<DivisionTeamsDto> UpdateDivisionTeamsInternalAsync(
		DivisionTeamsUpdateDto input,
		CancellationToken cancellationToken)
	{
		await _dataContext.LoadAllAsync(cancellationToken).ConfigureAwait(false);

		var division = _dataContext.Divisions.FirstOrDefault(d => d.Id == input.DivisionId)
			?? throw new InvalidOperationException($"Division {input.DivisionId} was not found.");

		await _dataContext.LoadDivisionDataAsync(division.Id, cancellationToken).ConfigureAwait(false);

		var divisionPoolIds = _dataContext.Pools
			.Where(p => p.DivisionId == division.Id)
			.Select(p => p.Id)
			.ToHashSet();

		var divisionTeamsById = _dataContext.Teams
			.Where(t => divisionPoolIds.Contains(t.PoolId))
			.ToDictionary(t => t.Id);

		var regionsByNumber = _dataContext.Regions
			.GroupBy(r => r.RegionNumber)
			.ToDictionary(g => g.Key, g => g.First());

		foreach (var item in input.Teams)
		{
			if (!divisionTeamsById.ContainsKey(item.TeamId))
			{
				throw new InvalidOperationException(
					$"Team {item.TeamId} does not belong to division '{division.Name}'.");
			}

			if (!regionsByNumber.ContainsKey(item.RegionNumber))
			{
				throw new InvalidOperationException(
					$"AYSO Region {item.RegionNumber} is not configured. Add Region {item.RegionNumber} under AYSO Regions first.");
			}
		}

		await using var tx = await _dataContext.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

		var anyUpdated = false;
		foreach (var item in input.Teams)
		{
			var team = divisionTeamsById[item.TeamId];
			var region = regionsByNumber[item.RegionNumber];
			var normalizedCoach = item.CoachName.Trim();
			var effectiveDeduction = item.RegionNumber == Team.HostRegionNumber && item.HasVolunteerPointDeduction;
			var formattedName = $"{region.Name}: {normalizedCoach}";

			var hasChanges =
				team.RegionNumber != item.RegionNumber ||
				!string.Equals(team.CoachName, normalizedCoach, StringComparison.Ordinal) ||
				team.HasVolunteerPointDeduction != effectiveDeduction ||
				!string.Equals(team.RegionName, region.Name, StringComparison.Ordinal);

			if (!hasChanges)
			{
				continue;
			}

			// Team.PoolId and Team.TeamNumber (and therefore Team.Code) are intentionally never modified.
			team.RegionNumber = item.RegionNumber;
			team.RegionName = region.Name;
			team.CoachName = normalizedCoach;
			team.Name = formattedName;
			team.HasVolunteerPointDeduction = effectiveDeduction;

			_dataContext.Update(team);
			anyUpdated = true;
		}

		if (anyUpdated || _dataContext.GetStandingsForDivision(division.Id) is null)
		{
			await _standingsRefreshService
				.RefreshDivisionAsync(division.Id, saveChanges: false, cancellationToken)
				.ConfigureAwait(false);

			await _dataContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

		return BuildDivisionTeamsDto(division);
	}
}
