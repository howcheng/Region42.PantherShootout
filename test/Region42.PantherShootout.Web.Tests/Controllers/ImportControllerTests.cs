using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Web.Controllers;
using Region42.PantherShootout.Web.Models;
using Region42.PantherShootout.Web.Tests.TestHelpers;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Controllers;

public class ImportControllerTests
{
	[Fact]
	public async Task UploadGet_PopulatesExistingYearsPlusCreateNewOption()
	{
		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				new TournamentYearDto { Id = 5, Year = 2025, Name = "2025 Panther Shootout", DivisionCount = 6, TeamCount = 48 },
			]);

		var controller = CreateController(
			Mock.Of<IScheduleImportService>(),
			yearService.Object,
			Mock.Of<IChampionshipBracketService>());

		var result = await controller.Upload();

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<ScheduleUploadViewModel>(view.Model);
		Assert.Equal(5, model.SelectedTournamentYearId);
		Assert.Equal(2, model.YearOptions.Count);
		Assert.Contains(model.YearOptions, o => o.Value == "0");
	}

	[Fact]
	public async Task UploadPost_WithStructuralErrors_ReturnsUploadViewWithErrorsAndMissingRegions()
	{
		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetByIdAsync(5, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TournamentYearDto { Id = 5, Year = 2025, Name = "2025 Panther Shootout" });
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync([new TournamentYearDto { Id = 5, Year = 2025, Name = "2025 Panther Shootout" }]);

		var importService = new Mock<IScheduleImportService>(MockBehavior.Strict);
		importService.Setup(s => s.ValidateWorkbookAsync(
				It.IsAny<Stream>(),
				5,
				null,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ScheduleImportPreviewResult
			{
				TournamentYearId = 5,
				Year = 2025,
				MissingRegionNumbers = [777],
				Errors =
				[
					new ScheduleImportValidationErrorDto
					{
						Code = "MissingRegion",
						SheetName = "Pools",
						CellReference = "C4",
						Message = "AYSO Region 777 is not defined.",
						MissingRegionNumber = 777,
					},
				],
			});

		var controller = CreateController(importService.Object, yearService.Object, Mock.Of<IChampionshipBracketService>());
		var file = CreateFakeXlsxFile("2025-schedule.xlsx", [1, 2, 3, 4]);

		var result = await controller.Upload(file, selectedTournamentYearId: 5);

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<ScheduleUploadViewModel>(view.Model);
		Assert.Single(model.ValidationErrors);
		Assert.Contains(777, model.MissingRegionNumbers);
	}

	[Fact]
	public async Task UploadPost_WithUnresolvedBracketCellsOnly_CachesSessionAndRedirectsToPreview()
	{
		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetByIdAsync(5, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TournamentYearDto { Id = 5, Year = 2025, Name = "2025 Panther Shootout" });

		var previewResult = new ScheduleImportPreviewResult
		{
			TournamentYearId = 5,
			Year = 2025,
			Divisions =
			[
				new ImportedDivisionPreviewDto
				{
					DivisionKey = "10u-girls",
					DivisionName = "10U Girls",
					AgeGroup = AgeGroup.U10,
					Gender = Gender.Girls,
					Size = DivisionSize.Eight,
					Pools = [new ImportedPoolPreviewDto { PoolName = "A" }, new ImportedPoolPreviewDto { PoolName = "B" }],
				},
			],
			Errors =
			[
				new ScheduleImportValidationErrorDto
				{
					Code = "UnresolvedBracketCell",
					SheetName = "Schedule",
					CellReference = "E22",
					Message = "Could not automatically determine bracket slot.",
				},
			],
			BracketCellMappings =
			[
				new BracketCellMappingPreviewDto
				{
					CellReference = "E22",
					RawText = "Mystery Final",
					Location = "Field 1",
				},
			],
		};

		var importService = new Mock<IScheduleImportService>(MockBehavior.Strict);
		importService.Setup(s => s.ValidateWorkbookAsync(
				It.IsAny<Stream>(),
				5,
				It.IsAny<IReadOnlyList<ScheduleCellOverrideDto>?>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(previewResult);

		var bracketService = new Mock<IChampionshipBracketService>(MockBehavior.Strict);
		bracketService.Setup(b => b.GetSlotDefinitions(DivisionSize.Eight, It.IsAny<IReadOnlyList<string>?>()))
			.Returns(
			[
				new ChampionshipBracketSlotDefinition
				{
					Slot = BracketSlot.Championship,
					HomeSourceDescription = "Pool A 1st",
					AwaySourceDescription = "Pool B 1st",
					IsOpeningRound = true,
				},
			]);

		var controller = CreateController(importService.Object, yearService.Object, bracketService.Object);
		var file = CreateFakeXlsxFile("2025-schedule.xlsx", [10, 20, 30]);

		var uploadResult = await controller.Upload(file, selectedTournamentYearId: 5);
		var redirect = Assert.IsType<RedirectToActionResult>(uploadResult);
		Assert.Equal(nameof(ImportController.Preview), redirect.ActionName);

		var previewActionResult = await controller.Preview();
		var previewView = Assert.IsType<ViewResult>(previewActionResult);
		var previewVm = Assert.IsType<SchedulePreviewViewModel>(previewView.Model);
		Assert.Equal("2025-schedule.xlsx", previewVm.FileName);
		Assert.NotEmpty(previewVm.PreviewToken);
		Assert.Single(previewVm.Overrides);
	}

	[Fact]
	public async Task RevalidateAndConfirm_PassesParsedOverridesAndClearsSessionOnCommit()
	{
		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetByIdAsync(5, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TournamentYearDto { Id = 5, Year = 2025, Name = "2025 Panther Shootout" });

		var validPreview = new ScheduleImportPreviewResult
		{
			TournamentYearId = 5,
			Year = 2025,
			Divisions =
			[
				new ImportedDivisionPreviewDto
				{
					DivisionKey = "10u-girls",
					DivisionName = "10U Girls",
					AgeGroup = AgeGroup.U10,
					Gender = Gender.Girls,
					Size = DivisionSize.Four,
					Pools = [new ImportedPoolPreviewDto { PoolName = "A" }],
				},
			],
			BracketCellMappings =
			[
				new BracketCellMappingPreviewDto
				{
					CellReference = "E22",
					RawText = "Custom Champ",
					ResolvedDivisionKey = "10u-girls",
					ResolvedDivisionName = "10U Girls",
					ResolvedSlot = BracketSlot.Championship,
				},
			],
		};

		IReadOnlyList<ScheduleCellOverrideDto>? capturedOverrides = null;

		var importService = new Mock<IScheduleImportService>(MockBehavior.Strict);
		importService.Setup(s => s.ValidateWorkbookAsync(
				It.IsAny<Stream>(),
				5,
				It.IsAny<IReadOnlyList<ScheduleCellOverrideDto>?>(),
				It.IsAny<CancellationToken>()))
			.Callback<Stream, int, IReadOnlyList<ScheduleCellOverrideDto>?, CancellationToken>((_, _, ov, _) => capturedOverrides = ov)
			.ReturnsAsync(validPreview);

		importService.Setup(s => s.ImportWorkbookAsync(
				It.IsAny<Stream>(),
				5,
				true,
				It.IsAny<IReadOnlyList<ScheduleCellOverrideDto>?>(),
				It.IsAny<CancellationToken>()))
			.Callback<Stream, int, bool, IReadOnlyList<ScheduleCellOverrideDto>?, CancellationToken>((_, _, _, ov, _) => capturedOverrides = ov)
			.ReturnsAsync(new ScheduleImportCommitResult
			{
				TournamentYearId = 5,
				DivisionsImported = 6,
				PoolsImported = 12,
				TeamsImported = 48,
				PoolPlayGamesImported = 72,
				ChampionshipGamesImported = 12,
				ShootoutRoundsImported = 72,
			});

		var bracketService = new Mock<IChampionshipBracketService>(MockBehavior.Strict);
		bracketService.Setup(b => b.GetSlotDefinitions(DivisionSize.Four, It.IsAny<IReadOnlyList<string>?>()))
			.Returns([]);

		var controller = CreateController(importService.Object, yearService.Object, bracketService.Object);
		await controller.Upload(CreateFakeXlsxFile("2025.xlsx", [1, 2, 3]), selectedTournamentYearId: 5);

		var previewView = Assert.IsType<ViewResult>(await controller.Preview());
		var previewVm = Assert.IsType<SchedulePreviewViewModel>(previewView.Model);

		var overrideInputs = new List<ScheduleCellOverrideInputModel>
		{
			new() { CellReference = "E22", Selection = "10u-girls:Championship" },
			new() { CellReference = "F22", Selection = "ignore" },
		};

		var revalidateResult = await controller.Revalidate(previewVm.PreviewToken, overrideInputs);
		Assert.IsType<ViewResult>(revalidateResult);
		Assert.NotNull(capturedOverrides);
		Assert.Equal(2, capturedOverrides!.Count);
		Assert.Equal("10u-girls", capturedOverrides[0].DivisionKey);
		Assert.Equal(BracketSlot.Championship, capturedOverrides[0].Slot);
		Assert.True(capturedOverrides[1].Ignore);

		var confirmResult = await controller.Confirm(previewVm.PreviewToken, allowOverwriteScores: true);
		var redirect = Assert.IsType<RedirectToActionResult>(confirmResult);
		Assert.Equal("Index", redirect.ActionName);
		Assert.Equal("Home", redirect.ControllerName);
		Assert.False(controller.HttpContext.Session.TryGetValue(ImportController.SessionWorkbookBytesKey, out _));
	}

	private static ImportController CreateController(
		IScheduleImportService importService,
		ITournamentYearService yearService,
		IChampionshipBracketService bracketService)
	{
		var httpContext = new DefaultHttpContext
		{
			Session = new TestHttpSession(),
		};

		return new ImportController(
			importService,
			yearService,
			bracketService,
			NullLogger<ImportController>.Instance)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
			TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>()),
		};
	}

	private static FormFile CreateFakeXlsxFile(string fileName, byte[] bytes)
	{
		var stream = new MemoryStream(bytes);
		return new FormFile(stream, 0, bytes.Length, "workbookFile", fileName);
	}
}
