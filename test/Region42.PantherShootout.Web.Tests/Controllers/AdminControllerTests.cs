using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Web.Controllers;
using Region42.PantherShootout.Web.Models;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Controllers;

public class AdminControllerTests
{
	[Fact]
	public async Task YearsGet_ReturnsOrderedYearsAndSuggestedNextYear()
	{
		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				new TournamentYearDto { Id = 2, Year = 2026, Name = "2026 Panther Shootout", IsActive = true },
				new TournamentYearDto { Id = 1, Year = 2025, Name = "2025 Panther Shootout", IsActive = false },
			]);

		var controller = CreateController(yearService.Object, Mock.Of<IAysoRegionService>());

		var result = await controller.Years();

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<TournamentYearsIndexViewModel>(view.Model);
		Assert.Equal(2, model.Years.Count);
		Assert.Equal(2027, model.NewYear);
	}

	[Fact]
	public async Task CreateYearPost_CreatesYearAndRedirects()
	{
		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.CreateYearAsync(
				It.Is<TournamentYearCreateDto>(d => d.Year == 2025 && d.IsActive),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(new TournamentYearDto { Id = 1, Year = 2025, Name = "2025 Panther Shootout", IsActive = true });

		var controller = CreateController(yearService.Object, Mock.Of<IAysoRegionService>());

		var result = await controller.CreateYear(new TournamentYearsIndexViewModel
		{
			NewYear = 2025,
			NewIsActive = true,
		});

		var redirect = Assert.IsType<RedirectToActionResult>(result);
		Assert.Equal(nameof(AdminController.Years), redirect.ActionName);
		Assert.NotNull(controller.TempData["SuccessMessage"]);
	}

	[Fact]
	public async Task RegionsGet_PrePopulatesMissingRegionAndReturnLink()
	{
		var regionService = new Mock<IAysoRegionService>(MockBehavior.Strict);
		regionService.Setup(s => s.GetAllRegionsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync([new AysoRegionDto { Id = 1, RegionNumber = 42, Name = "Newbury Park", TeamUsageCount = 4 }]);

		var controller = CreateController(Mock.Of<ITournamentYearService>(), regionService.Object);

		var result = await controller.Regions(missingRegion: 638, returnToImport: "true");

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<AysoRegionsIndexViewModel>(view.Model);
		Assert.Equal(638, model.RegionNumber);
		Assert.Equal("true", model.ReturnToImport);
		Assert.Single(model.Regions);
	}

	[Fact]
	public async Task SaveAndDeleteRegion_DelegatesToAysoRegionService()
	{
		var regionService = new Mock<IAysoRegionService>(MockBehavior.Strict);
		regionService.Setup(s => s.UpsertRegionAsync(
				It.Is<AysoRegionUpsertDto>(d => d.RegionNumber == 121 && d.Name == "Simi Valley"),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(new AysoRegionDto { Id = 2, RegionNumber = 121, Name = "Simi Valley" });
		regionService.Setup(s => s.DeleteRegionAsync(2, It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		var controller = CreateController(Mock.Of<ITournamentYearService>(), regionService.Object);

		var saveResult = await controller.SaveRegion(new AysoRegionsIndexViewModel
		{
			RegionNumber = 121,
			Name = "Simi Valley",
			ReturnToImport = "true",
		});
		var saveRedirect = Assert.IsType<RedirectToActionResult>(saveResult);
		Assert.Equal(nameof(AdminController.Regions), saveRedirect.ActionName);

		var deleteResult = await controller.DeleteRegion(2, "true");
		var deleteRedirect = Assert.IsType<RedirectToActionResult>(deleteResult);
		Assert.Equal(nameof(AdminController.Regions), deleteRedirect.ActionName);
	}

	private static AdminController CreateController(
		ITournamentYearService yearService,
		IAysoRegionService regionService)
	{
		var httpContext = new DefaultHttpContext();
		return new AdminController(yearService, regionService)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
			TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>()),
		};
	}
}
