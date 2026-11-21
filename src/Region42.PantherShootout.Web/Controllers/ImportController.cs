using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Exceptions;
using Region42.PantherShootout.Application.Helpers;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Web.Models;

namespace Region42.PantherShootout.Web.Controllers;

[Authorize(Policy = "AdminPolicy")]
public class ImportController : Controller
{
	public const string SessionWorkbookBytesKey = "PantherShootout.Import.WorkbookBytes";
	public const string SessionMetadataKey = "PantherShootout.Import.Metadata";
	public const long MaxWorkbookSizeBytes = 10 * 1024 * 1024; // 10 MB

	private static readonly HashSet<string> BracketAdjustableErrorCodes = new(StringComparer.OrdinalIgnoreCase)
	{
		"UnresolvedBracketCell",
		"UnexpectedBracketGame",
		"UnexpectedBracketSlot",
		"DuplicateBracketSlot",
		"MissingBracketSlot",
		"InvalidOverrideDivision",
	};

	private readonly IScheduleImportService _scheduleImportService;
	private readonly ITournamentYearService _tournamentYearService;
	private readonly IChampionshipBracketService _bracketService;
	private readonly ILogger<ImportController> _logger;

	public ImportController(
		IScheduleImportService scheduleImportService,
		ITournamentYearService tournamentYearService,
		IChampionshipBracketService bracketService,
		ILogger<ImportController> logger)
	{
		_scheduleImportService = scheduleImportService ?? throw new ArgumentNullException(nameof(scheduleImportService));
		_tournamentYearService = tournamentYearService ?? throw new ArgumentNullException(nameof(tournamentYearService));
		_bracketService = bracketService ?? throw new ArgumentNullException(nameof(bracketService));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	[HttpGet]
	public async Task<IActionResult> Upload(int? yearId = null, CancellationToken cancellationToken = default)
	{
		var model = await BuildUploadViewModelAsync(yearId, newYear: null, newYearName: null, cancellationToken);
		return View(model);
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Upload(
		IFormFile? workbookFile,
		int selectedTournamentYearId,
		int? newYear = null,
		string? newYearName = null,
		CancellationToken cancellationToken = default)
	{
		if (workbookFile is null || workbookFile.Length == 0)
		{
			ModelState.AddModelError("workbookFile", "Please select an Excel (.xlsx) schedule workbook to upload.");
			return View(await BuildUploadViewModelAsync(selectedTournamentYearId, newYear, newYearName, cancellationToken));
		}

		if (!workbookFile.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
		{
			ModelState.AddModelError("workbookFile", "Only Excel .xlsx workbooks are supported.");
			return View(await BuildUploadViewModelAsync(selectedTournamentYearId, newYear, newYearName, cancellationToken));
		}

		if (workbookFile.Length > MaxWorkbookSizeBytes)
		{
			ModelState.AddModelError("workbookFile", "Workbook file size cannot exceed 10 MB.");
			return View(await BuildUploadViewModelAsync(selectedTournamentYearId, newYear, newYearName, cancellationToken));
		}

		TournamentYearDto targetYear;
		if (selectedTournamentYearId == 0)
		{
			var targetYearNumber = newYear ?? DateTime.UtcNow.Year;
			var existingByNumber = await _tournamentYearService.GetByYearNumberAsync(targetYearNumber, cancellationToken);
			if (existingByNumber is not null)
			{
				targetYear = existingByNumber;
			}
			else
			{
				try
				{
					targetYear = await _tournamentYearService.CreateYearAsync(new TournamentYearCreateDto
					{
						Year = targetYearNumber,
						Name = newYearName,
						IsActive = true,
					}, cancellationToken);
				}
				catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
				{
					ModelState.AddModelError("", ex.Message);
					return View(await BuildUploadViewModelAsync(0, targetYearNumber, newYearName, cancellationToken));
				}
			}
		}
		else
		{
			var existing = await _tournamentYearService.GetByIdAsync(selectedTournamentYearId, cancellationToken);
			if (existing is null)
			{
				ModelState.AddModelError("", "Selected tournament year was not found.");
				return View(await BuildUploadViewModelAsync(null, newYear, newYearName, cancellationToken));
			}

			targetYear = existing;
		}

		byte[] workbookBytes;
		await using (var memoryStream = new MemoryStream((int)workbookFile.Length))
		{
			await workbookFile.CopyToAsync(memoryStream, cancellationToken);
			workbookBytes = memoryStream.ToArray();
		}

		try
		{
			await using var stream = new MemoryStream(workbookBytes, writable: false);
			var preview = await _scheduleImportService.ValidateWorkbookAsync(
				stream,
				targetYear.Id,
				cellOverrides: null,
				cancellationToken);

			var canProceedToPreview = preview.IsValid ||
				(preview.Divisions.Count > 0 && preview.Errors.All(e => BracketAdjustableErrorCodes.Contains(e.Code)));

			if (!canProceedToPreview)
			{
				var failedVm = await BuildUploadViewModelAsync(targetYear.Id, newYear, newYearName, cancellationToken);
				failedVm.UploadedFileName = workbookFile.FileName;
				failedVm.MissingRegionNumbers = preview.MissingRegionNumbers;
				failedVm.ValidationErrors = preview.Errors;
				return View(failedVm);
			}

			var token = Guid.NewGuid().ToString("N");
			SaveSessionPayload(new CachedImportSessionMetadata
			{
				PreviewToken = token,
				FileName = workbookFile.FileName,
				TournamentYearId = targetYear.Id,
				Year = targetYear.Year,
				TournamentName = targetYear.Name,
				Overrides = [],
			}, workbookBytes);

			return RedirectToAction(nameof(Preview));
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error validating uploaded schedule workbook {FileName}", workbookFile.FileName);
			ModelState.AddModelError("", $"Error reading workbook: {ex.Message}");
			return View(await BuildUploadViewModelAsync(targetYear.Id, newYear, newYearName, cancellationToken));
		}
	}

	[HttpGet]
	public async Task<IActionResult> Preview(CancellationToken cancellationToken = default)
	{
		if (!TryLoadSessionPayload(out var metadata, out var workbookBytes))
		{
			TempData["ErrorMessage"] = "Schedule preview session expired or was not found. Please upload the workbook again.";
			return RedirectToAction(nameof(Upload));
		}

		var cellOverrides = ParseCellOverrides(metadata.Overrides);
		await using var stream = new MemoryStream(workbookBytes, writable: false);
		var preview = await _scheduleImportService.ValidateWorkbookAsync(
			stream,
			metadata.TournamentYearId,
			cellOverrides,
			cancellationToken);

		var vm = BuildPreviewViewModel(metadata, preview);
		return View(vm);
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Revalidate(
		string previewToken,
		List<ScheduleCellOverrideInputModel>? overrides,
		CancellationToken cancellationToken = default)
	{
		if (!TryLoadSessionPayload(out var metadata, out var workbookBytes) ||
			!string.Equals(metadata.PreviewToken, previewToken, StringComparison.Ordinal))
		{
			TempData["ErrorMessage"] = "Schedule preview session expired. Please upload the workbook again.";
			return RedirectToAction(nameof(Upload));
		}

		metadata.Overrides = overrides ?? [];
		SaveSessionPayload(metadata, workbookBytes);

		var cellOverrides = ParseCellOverrides(metadata.Overrides);
		await using var stream = new MemoryStream(workbookBytes, writable: false);
		var preview = await _scheduleImportService.ValidateWorkbookAsync(
			stream,
			metadata.TournamentYearId,
			cellOverrides,
			cancellationToken);

		if (preview.IsValid)
		{
			TempData["SuccessMessage"] = "All schedule and bracket cells validated cleanly. Ready to confirm import.";
		}

		var vm = BuildPreviewViewModel(metadata, preview);
		return View(nameof(Preview), vm);
	}

	[HttpPost]
	[ValidateAntiForgeryToken]
	public async Task<IActionResult> Confirm(
		string previewToken,
		bool allowOverwriteScores = false,
		List<ScheduleCellOverrideInputModel>? overrides = null,
		CancellationToken cancellationToken = default)
	{
		if (!TryLoadSessionPayload(out var metadata, out var workbookBytes) ||
			!string.Equals(metadata.PreviewToken, previewToken, StringComparison.Ordinal))
		{
			TempData["ErrorMessage"] = "Schedule preview session expired. Please upload the workbook again.";
			return RedirectToAction(nameof(Upload));
		}

		if (overrides is not null && overrides.Count > 0)
		{
			metadata.Overrides = overrides;
			SaveSessionPayload(metadata, workbookBytes);
		}

		var cellOverrides = ParseCellOverrides(metadata.Overrides);

		try
		{
			await using var stream = new MemoryStream(workbookBytes, writable: false);
			var result = await _scheduleImportService.ImportWorkbookAsync(
				stream,
				metadata.TournamentYearId,
				allowOverwriteScores,
				cellOverrides,
				cancellationToken);

			ClearSessionPayload();

			TempData["SuccessMessage"] =
				$"Imported {metadata.TournamentName}: {result.DivisionsImported} divisions, " +
				$"{result.PoolsImported} pools, {result.TeamsImported} teams, " +
				$"{result.PoolPlayGamesImported} pool-play games, " +
				$"{result.ChampionshipGamesImported} bracket games, and " +
				$"{result.ShootoutRoundsImported} PSO shootout matchups.";

			return RedirectToAction("Index", "Home", new { year = metadata.Year });
		}
		catch (ScheduleImportValidationException vex)
		{
			ModelState.AddModelError("", vex.Message);
			await using var previewStream = new MemoryStream(workbookBytes, writable: false);
			var preview = await _scheduleImportService.ValidateWorkbookAsync(
				previewStream,
				metadata.TournamentYearId,
				cellOverrides,
				cancellationToken);
			var vm = BuildPreviewViewModel(metadata, preview);
			vm.AllowOverwriteScores = allowOverwriteScores;
			return View(nameof(Preview), vm);
		}
		catch (InvalidOperationException ioex)
		{
			ModelState.AddModelError("", ioex.Message);
			await using var previewStream = new MemoryStream(workbookBytes, writable: false);
			var preview = await _scheduleImportService.ValidateWorkbookAsync(
				previewStream,
				metadata.TournamentYearId,
				cellOverrides,
				cancellationToken);
			var vm = BuildPreviewViewModel(metadata, preview);
			vm.AllowOverwriteScores = allowOverwriteScores;
			return View(nameof(Preview), vm);
		}
	}

	private async Task<ScheduleUploadViewModel> BuildUploadViewModelAsync(
		int? selectedYearId,
		int? newYear,
		string? newYearName,
		CancellationToken cancellationToken)
	{
		var years = await _tournamentYearService.GetAllYearsAsync(cancellationToken);
		var currentYear = DateTime.UtcNow.Year;
		var suggestedNewYear = newYear ?? (years.Any(y => y.Year == currentYear)
			? years.Max(y => y.Year) + 1
			: currentYear);

		var defaultSelectedId = selectedYearId ?? years.FirstOrDefault()?.Id ?? 0;

		var options = new List<SelectListItem>();
		foreach (var year in years)
		{
			var statusSuffix = year.DivisionCount == 0
				? " (Empty)"
				: $" ({year.DivisionCount} divisions, {year.TeamCount} teams)";
			options.Add(new SelectListItem
			{
				Value = year.Id.ToString(),
				Text = $"{year.Name}{statusSuffix}",
				Selected = year.Id == defaultSelectedId,
			});
		}

		options.Add(new SelectListItem
		{
			Value = "0",
			Text = "Create New Tournament Year...",
			Selected = defaultSelectedId == 0,
		});

		return new ScheduleUploadViewModel
		{
			SelectedTournamentYearId = defaultSelectedId,
			NewYear = suggestedNewYear,
			NewYearName = newYearName,
			YearOptions = options,
		};
	}

	private SchedulePreviewViewModel BuildPreviewViewModel(
		CachedImportSessionMetadata metadata,
		ScheduleImportPreviewResult preview)
	{
		var savedByCell = metadata.Overrides
			.Where(o => !string.IsNullOrWhiteSpace(o.CellReference))
			.ToDictionary(o => o.CellReference, o => o.Selection, StringComparer.OrdinalIgnoreCase);

		var overrideRows = preview.BracketCellMappings
			.Select(cell =>
			{
				var defaultSelection = "auto";
				if (savedByCell.TryGetValue(cell.CellReference, out var savedSelection) &&
					!string.IsNullOrWhiteSpace(savedSelection))
				{
					defaultSelection = savedSelection;
				}
				else if (cell.IsIgnored)
				{
					defaultSelection = "ignore";
				}

				return new ScheduleCellOverrideInputModel
				{
					CellReference = cell.CellReference,
					Selection = defaultSelection,
				};
			})
			.ToList();

		var bracketOptions = new List<SelectListItem>
		{
			new() { Value = "auto", Text = "Auto-detect from label & color" },
			new() { Value = "ignore", Text = "Ignore cell (note / non-game)" },
		};

		foreach (var div in preview.Divisions.OrderBy(d => d.AgeGroup).ThenBy(d => d.Gender))
		{
			if (!DivisionSizeRules.HasChampionshipBracket(div.Size))
			{
				continue;
			}

			var group = new SelectListGroup { Name = $"{div.DivisionName} ({(int)div.Size} teams)" };
			var poolNames = div.Pools.Select(p => p.PoolName).ToList();
			var slots = _bracketService.GetSlotDefinitions(div.Size, poolNames);
			foreach (var slotDef in slots)
			{
				bracketOptions.Add(new SelectListItem
				{
					Value = $"{div.DivisionKey}:{slotDef.Slot}",
					Text = $"{div.DivisionName} — {FormatBracketSlotLabel(slotDef.Slot)} ({slotDef.HomeSourceDescription} vs {slotDef.AwaySourceDescription})",
					Group = group,
				});
			}
		}

		return new SchedulePreviewViewModel
		{
			PreviewToken = metadata.PreviewToken,
			FileName = metadata.FileName,
			TournamentYearId = metadata.TournamentYearId,
			Year = metadata.Year,
			TournamentName = metadata.TournamentName,
			Preview = preview,
			Overrides = overrideRows,
			BracketOverrideOptions = bracketOptions,
		};
	}

	public static IReadOnlyList<ScheduleCellOverrideDto> ParseCellOverrides(
		IEnumerable<ScheduleCellOverrideInputModel>? inputs)
	{
		if (inputs is null)
		{
			return Array.Empty<ScheduleCellOverrideDto>();
		}

		var result = new List<ScheduleCellOverrideDto>();
		foreach (var item in inputs)
		{
			if (string.IsNullOrWhiteSpace(item.CellReference) ||
				string.IsNullOrWhiteSpace(item.Selection) ||
				string.Equals(item.Selection, "auto", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (string.Equals(item.Selection, "ignore", StringComparison.OrdinalIgnoreCase))
			{
				result.Add(new ScheduleCellOverrideDto
				{
					CellReference = item.CellReference.Trim(),
					Ignore = true,
				});
				continue;
			}

			var parts = item.Selection.Split(':', 2, StringSplitOptions.TrimEntries);
			if (parts.Length == 2 && Enum.TryParse<BracketSlot>(parts[1], ignoreCase: true, out var slot))
			{
				result.Add(new ScheduleCellOverrideDto
				{
					CellReference = item.CellReference.Trim(),
					DivisionKey = parts[0],
					Slot = slot,
				});
			}
		}

		return result;
	}

	public static string FormatBracketSlotLabel(BracketSlot slot)
	{
		return slot switch
		{
			BracketSlot.Consolation => "Consolation",
			BracketSlot.Semifinal1 => "Semifinal 1",
			BracketSlot.Semifinal2 => "Semifinal 2",
			BracketSlot.ThirdPlace => "3rd Place",
			BracketSlot.Championship => "Championship Final",
			_ => slot.ToString(),
		};
	}

	private void SaveSessionPayload(CachedImportSessionMetadata metadata, byte[] workbookBytes)
	{
		HttpContext.Session.Set(SessionWorkbookBytesKey, workbookBytes);
		HttpContext.Session.SetString(SessionMetadataKey, JsonSerializer.Serialize(metadata));
	}

	private bool TryLoadSessionPayload(out CachedImportSessionMetadata metadata, out byte[] workbookBytes)
	{
		metadata = new CachedImportSessionMetadata();
		workbookBytes = Array.Empty<byte>();

		if (!HttpContext.Session.TryGetValue(SessionWorkbookBytesKey, out var bytes) ||
			bytes is null ||
			bytes.Length == 0)
		{
			return false;
		}

		var json = HttpContext.Session.GetString(SessionMetadataKey);
		if (string.IsNullOrWhiteSpace(json))
		{
			return false;
		}

		var parsed = JsonSerializer.Deserialize<CachedImportSessionMetadata>(json);
		if (parsed is null || parsed.TournamentYearId <= 0)
		{
			return false;
		}

		metadata = parsed;
		workbookBytes = bytes;
		return true;
	}

	private void ClearSessionPayload()
	{
		HttpContext.Session.Remove(SessionWorkbookBytesKey);
		HttpContext.Session.Remove(SessionMetadataKey);
	}

	private sealed class CachedImportSessionMetadata
	{
		public string PreviewToken { get; set; } = string.Empty;

		public string FileName { get; set; } = string.Empty;

		public int TournamentYearId { get; set; }

		public int Year { get; set; }

		public string TournamentName { get; set; } = string.Empty;

		public List<ScheduleCellOverrideInputModel> Overrides { get; set; } = new();
	}
}
