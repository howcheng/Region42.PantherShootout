using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Web.Helpers;
using Region42.PantherShootout.Web.Models;

namespace Region42.PantherShootout.Web.Controllers;

[Authorize(Policy = "AdminPolicy")]
public class AdminController : Controller
{
	private readonly ITournamentYearService _tournamentYearService;
	private readonly IAysoRegionService _aysoRegionService;
	private readonly ITeamAdminService _teamAdminService;

	public AdminController(
		ITournamentYearService tournamentYearService,
		IAysoRegionService aysoRegionService,
		ITeamAdminService teamAdminService)
	{
		_tournamentYearService = tournamentYearService ?? throw new ArgumentNullException(nameof(tournamentYearService));
		_aysoRegionService = aysoRegionService ?? throw new ArgumentNullException(nameof(aysoRegionService));
		_teamAdminService = teamAdminService ?? throw new ArgumentNullException(nameof(teamAdminService));
	}

	[HttpGet]
	public async Task<IActionResult> Years(CancellationToken cancellationToken = default)
	{
		var years = await _tournamentYearService.GetAllYearsAsync(cancellationToken);
		var currentYear = DateTime.UtcNow.Year;
		var suggestedYear = years.Any(y => y.Year == currentYear)
			? years.Max(y => y.Year) + 1
			: currentYear;

		return View(new TournamentYearsIndexViewModel
		{
			Years = years.ToList(),
			NewYear = suggestedYear,
			NewIsActive = years.Count == 0,
		});
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> CreateYear(
		TournamentYearsIndexViewModel input,
		CancellationToken cancellationToken = default)
	{
		if (!ModelState.IsValid)
		{
			input.Years = (await _tournamentYearService.GetAllYearsAsync(cancellationToken)).ToList();
			return View(nameof(Years), input);
		}

		try
		{
			var created = await _tournamentYearService.CreateYearAsync(new TournamentYearCreateDto
			{
				Year = input.NewYear,
				Name = input.NewName,
				IsActive = input.NewIsActive,
				CustomMessage = input.NewCustomMessage,
			}, cancellationToken);

			TempData["SuccessMessage"] = $"Created tournament year '{created.Name}'.";
			return RedirectToAction(nameof(Years));
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			ModelState.AddModelError("", ex.Message);
			input.Years = (await _tournamentYearService.GetAllYearsAsync(cancellationToken)).ToList();
			return View(nameof(Years), input);
		}
	}

	[HttpGet]
	public async Task<IActionResult> EditYear(int id, CancellationToken cancellationToken = default)
	{
		if (id <= 0)
		{
			return BadRequest();
		}

		var existing = await _tournamentYearService.GetByIdAsync(id, cancellationToken);
		if (existing is null)
		{
			return NotFound();
		}

		return View(new EditTournamentYearViewModel
		{
			Id = existing.Id,
			Year = existing.Year,
			Name = existing.Name,
			IsActive = existing.IsActive,
			CustomMessage = existing.CustomMessage,
		});
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EditYear(
		EditTournamentYearViewModel input,
		CancellationToken cancellationToken = default)
	{
		if (!ModelState.IsValid)
		{
			return View(input);
		}

		try
		{
			var updated = await _tournamentYearService.UpdateYearAsync(new TournamentYearUpdateDto
			{
				Id = input.Id,
				Name = input.Name,
				IsActive = input.IsActive,
				CustomMessage = input.CustomMessage,
			}, cancellationToken);

			TempData["SuccessMessage"] = $"Updated '{updated.Name}'.";
			return RedirectToAction(nameof(Years));
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			ModelState.AddModelError("", ex.Message);
			return View(input);
		}
	}

	[HttpGet]
	public async Task<IActionResult> Regions(
		int? missingRegion = null,
		string? returnToImport = null,
		CancellationToken cancellationToken = default)
	{
		var regions = await _aysoRegionService.GetAllRegionsAsync(cancellationToken);
		return View(new AysoRegionsIndexViewModel
		{
			Regions = regions.ToList(),
			RegionNumber = missingRegion ?? 0,
			ReturnToImport = returnToImport,
		});
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SaveRegion(
		AysoRegionsIndexViewModel input,
		CancellationToken cancellationToken = default)
	{
		if (!ModelState.IsValid)
		{
			input.Regions = (await _aysoRegionService.GetAllRegionsAsync(cancellationToken)).ToList();
			return View(nameof(Regions), input);
		}

		try
		{
			var saved = await _aysoRegionService.UpsertRegionAsync(new AysoRegionUpsertDto
			{
				Id = input.EditId,
				RegionNumber = input.RegionNumber,
				Name = input.Name,
			}, cancellationToken);

			TempData["SuccessMessage"] = $"Saved AYSO Region {saved.RegionNumber} ({saved.Name}).";
			return RedirectToAction(nameof(Regions), new { returnToImport = input.ReturnToImport });
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			ModelState.AddModelError("", ex.Message);
			input.Regions = (await _aysoRegionService.GetAllRegionsAsync(cancellationToken)).ToList();
			return View(nameof(Regions), input);
		}
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> DeleteRegion(
		int id,
		string? returnToImport = null,
		CancellationToken cancellationToken = default)
	{
		if (id <= 0)
		{
			return BadRequest();
		}

		try
		{
			await _aysoRegionService.DeleteRegionAsync(id, cancellationToken);
			TempData["SuccessMessage"] = "AYSO Region deleted.";
		}
		catch (InvalidOperationException ex)
		{
			TempData["ErrorMessage"] = ex.Message;
		}

		return RedirectToAction(nameof(Regions), new { returnToImport });
	}

	[HttpGet]
	public async Task<IActionResult> Divisions(int? yearId = null, CancellationToken cancellationToken = default)
	{
		var years = await _tournamentYearService.GetAllYearsAsync(cancellationToken);
		var selectedYear = yearId.HasValue
			? years.FirstOrDefault(y => y.Id == yearId.Value)
			: await _tournamentYearService.GetDefaultYearAsync(cancellationToken);

		if (selectedYear is null)
		{
			return View(new DivisionBulletinsIndexViewModel
			{
				Years = years.ToList(),
			});
		}

		var divisions = await _tournamentYearService.GetDivisionsForYearAsync(selectedYear.Id, cancellationToken);

		return View(new DivisionBulletinsIndexViewModel
		{
			SelectedTournamentYearId = selectedYear.Id,
			SelectedYear = selectedYear.Year,
			SelectedYearName = selectedYear.Name,
			Years = years.ToList(),
			Divisions = divisions.ToList(),
		});
	}

	[HttpGet]
	public async Task<IActionResult> EditDivision(int id, CancellationToken cancellationToken = default)
	{
		if (id <= 0)
		{
			return BadRequest();
		}

		var division = await _tournamentYearService.GetDivisionByIdAsync(id, cancellationToken);
		if (division is null)
		{
			return NotFound();
		}

		return View(new EditDivisionBulletinViewModel
		{
			DivisionId = division.Id,
			TournamentYearId = division.TournamentYearId,
			Year = division.Year,
			DivisionName = division.Name,
			ShortCode = division.ShortCode,
			CustomMessage = division.CustomMessage,
		});
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> EditDivision(
		EditDivisionBulletinViewModel input,
		CancellationToken cancellationToken = default)
	{
		if (!ModelState.IsValid)
		{
			return View(input);
		}

		try
		{
			var updated = await _tournamentYearService.UpdateDivisionCustomMessageAsync(
				new DivisionBulletinUpdateDto
				{
					DivisionId = input.DivisionId,
					CustomMessage = input.CustomMessage,
				},
				cancellationToken);

			TempData["SuccessMessage"] = $"Updated bulletin for {updated.Name}.";
			return RedirectToAction(nameof(Divisions), new { yearId = updated.TournamentYearId });
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			ModelState.AddModelError("", ex.Message);
			return View(input);
		}
	}

	[HttpGet]
	public async Task<IActionResult> Teams(
		int? yearId = null,
		int? year = null,
		string? division = null,
		CancellationToken cancellationToken = default)
	{
		var years = await _tournamentYearService.GetAllYearsAsync(cancellationToken);
		TournamentYearDto? selectedYear = null;

		if (yearId.HasValue)
		{
			selectedYear = years.FirstOrDefault(y => y.Id == yearId.Value);
		}
		else if (year.HasValue)
		{
			selectedYear = years.FirstOrDefault(y => y.Year == year.Value);
		}

		selectedYear ??= await _tournamentYearService.GetDefaultYearAsync(cancellationToken);

		if (selectedYear is null)
		{
			return View(new AdminTeamsViewModel
			{
				Years = years.ToList(),
			});
		}

		var divisions = await _tournamentYearService.GetDivisionsForYearAsync(selectedYear.Id, cancellationToken);
		if (divisions.Count == 0)
		{
			return View(new AdminTeamsViewModel
			{
				SelectedTournamentYearId = selectedYear.Id,
				SelectedYear = selectedYear.Year,
				SelectedYearName = selectedYear.Name,
				Years = years.ToList(),
			});
		}

		DivisionSummaryDto selectedDivision;
		if (!string.IsNullOrWhiteSpace(division))
		{
			selectedDivision = divisions.FirstOrDefault(d =>
				string.Equals(d.DivisionKey, division.Trim(), StringComparison.OrdinalIgnoreCase))
				?? divisions[0];
			DivisionPreferenceCookieHelper.SetPreferredDivisionKey(HttpContext, selectedDivision.DivisionKey);
		}
		else
		{
			var preferredKey = DivisionPreferenceCookieHelper.GetPreferredDivisionKey(HttpContext);
			selectedDivision = divisions.FirstOrDefault(d =>
				string.Equals(d.DivisionKey, preferredKey, StringComparison.OrdinalIgnoreCase))
				?? divisions[0];
		}

		var divisionTeams = await _teamAdminService.GetDivisionTeamsAsync(selectedDivision.Id, cancellationToken);
		var regions = await _aysoRegionService.GetAllRegionsAsync(cancellationToken);

		return View(BuildAdminTeamsViewModel(
			selectedYear,
			years,
			selectedDivision,
			divisions,
			regions,
			divisionTeams,
			submittedRows: null));
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> SaveTeams(
		AdminTeamsViewModel input,
		CancellationToken cancellationToken = default)
	{
		if (input.SelectedDivisionId <= 0)
		{
			return BadRequest();
		}

		if (!ModelState.IsValid)
		{
			var invalidVm = await RebuildAdminTeamsViewModelOnErrorAsync(input, cancellationToken);
			if (invalidVm is null)
			{
				return NotFound();
			}

			return View(nameof(Teams), invalidVm);
		}

		try
		{
			var updated = await _teamAdminService.UpdateDivisionTeamsAsync(
				new DivisionTeamsUpdateDto
				{
					DivisionId = input.SelectedDivisionId,
					Teams = input.Teams
						.Select(t => new TeamUpdateItemDto
						{
							TeamId = t.TeamId,
							RegionNumber = t.RegionNumber,
							CoachName = t.CoachName,
							HasVolunteerPointDeduction = t.RegionNumber == Team.HostRegionNumber && t.HasVolunteerPointDeduction,
						})
						.ToList(),
				},
				cancellationToken);

			TempData["SuccessMessage"] = $"Saved team updates for {updated.DivisionName}.";
			return RedirectToAction(nameof(Teams), new
			{
				yearId = updated.TournamentYearId,
				division = updated.DivisionKey,
			});
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
		{
			ModelState.AddModelError("", ex.Message);
			var errorVm = await RebuildAdminTeamsViewModelOnErrorAsync(input, cancellationToken);
			if (errorVm is null)
			{
				return NotFound();
			}

			return View(nameof(Teams), errorVm);
		}
	}

	private async Task<AdminTeamsViewModel?> RebuildAdminTeamsViewModelOnErrorAsync(
		AdminTeamsViewModel input,
		CancellationToken cancellationToken)
	{
		var divisionSummary = await _tournamentYearService.GetDivisionByIdAsync(input.SelectedDivisionId, cancellationToken);
		if (divisionSummary is null)
		{
			return null;
		}

		var years = await _tournamentYearService.GetAllYearsAsync(cancellationToken);
		var selectedYear = years.FirstOrDefault(y => y.Id == divisionSummary.TournamentYearId);
		if (selectedYear is null)
		{
			return null;
		}

		var divisions = await _tournamentYearService.GetDivisionsForYearAsync(selectedYear.Id, cancellationToken);
		var divisionTeams = await _teamAdminService.GetDivisionTeamsAsync(divisionSummary.Id, cancellationToken);
		var regions = await _aysoRegionService.GetAllRegionsAsync(cancellationToken);

		return BuildAdminTeamsViewModel(
			selectedYear,
			years,
			divisionSummary,
			divisions,
			regions,
			divisionTeams,
			input.Teams);
	}

	private static AdminTeamsViewModel BuildAdminTeamsViewModel(
		TournamentYearDto selectedYear,
		IReadOnlyList<TournamentYearDto> years,
		DivisionSummaryDto selectedDivision,
		IReadOnlyList<DivisionSummaryDto> divisions,
		IReadOnlyList<AysoRegionDto> regions,
		DivisionTeamsDto? divisionTeams,
		IReadOnlyList<AdminTeamEditRowViewModel>? submittedRows)
	{
		var submittedByTeamId = (submittedRows ?? [])
			.Where(r => r.TeamId > 0)
			.GroupBy(r => r.TeamId)
			.ToDictionary(g => g.Key, g => g.First());

		var regionsByNumber = regions
			.GroupBy(r => r.RegionNumber)
			.ToDictionary(g => g.Key, g => g.First().Name);

		var flatTeams = new List<AdminTeamEditRowViewModel>();
		var poolViewModels = new List<AdminPoolTeamsViewModel>();

		if (divisionTeams is not null)
		{
			foreach (var pool in divisionTeams.Pools)
			{
				var poolVm = new AdminPoolTeamsViewModel
				{
					PoolId = pool.PoolId,
					PoolName = pool.PoolName,
				};

				foreach (var team in pool.Teams)
				{
					var idx = flatTeams.Count;
					poolVm.TeamIndices.Add(idx);

					if (submittedByTeamId.TryGetValue(team.Id, out var submitted))
					{
						var resolvedRegionName = regionsByNumber.GetValueOrDefault(submitted.RegionNumber) ?? team.RegionName;
						flatTeams.Add(new AdminTeamEditRowViewModel
						{
							TeamId = team.Id,
							PoolId = team.PoolId,
							PoolName = team.PoolName,
							TeamNumber = team.TeamNumber,
							Code = team.Code,
							RegionNumber = submitted.RegionNumber,
							RegionName = resolvedRegionName,
							CoachName = submitted.CoachName,
							HasVolunteerPointDeduction = submitted.RegionNumber == Team.HostRegionNumber && submitted.HasVolunteerPointDeduction,
						});
					}
					else
					{
						flatTeams.Add(new AdminTeamEditRowViewModel
						{
							TeamId = team.Id,
							PoolId = team.PoolId,
							PoolName = team.PoolName,
							TeamNumber = team.TeamNumber,
							Code = team.Code,
							RegionNumber = team.RegionNumber,
							RegionName = team.RegionName,
							CoachName = team.CoachName,
							HasVolunteerPointDeduction = team.IsHostRegion && team.HasVolunteerPointDeduction,
						});
					}
				}

				poolViewModels.Add(poolVm);
			}
		}

		return new AdminTeamsViewModel
		{
			SelectedTournamentYearId = selectedYear.Id,
			SelectedYear = selectedYear.Year,
			SelectedYearName = selectedYear.Name,
			SelectedDivisionId = selectedDivision.Id,
			SelectedDivisionKey = selectedDivision.DivisionKey,
			SelectedDivisionShortCode = selectedDivision.ShortCode,
			SelectedDivisionName = selectedDivision.Name,
			Years = years.ToList(),
			Divisions = divisions.ToList(),
			Regions = regions.ToList(),
			Pools = poolViewModels,
			Teams = flatTeams,
		};
	}
}
