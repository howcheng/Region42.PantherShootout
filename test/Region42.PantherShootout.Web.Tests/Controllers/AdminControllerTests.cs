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

	[Fact]
	public async Task TeamsGet_LoadsSelectedYearDivisionTeamsAndRegions()
	{
		var yearDto = new TournamentYearDto { Id = 2, Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
		var divDto = new DivisionSummaryDto
		{
			Id = 10,
			TournamentYearId = 2,
			Year = 2026,
			DivisionKey = "10u-boys",
			ShortCode = "10UB",
			Name = "10U Boys",
			PoolCount = 1,
			TeamCount = 4,
		};

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([yearDto]);
		yearService.Setup(s => s.GetDivisionsForYearAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync([divDto]);

		var regionService = new Mock<IAysoRegionService>(MockBehavior.Strict);
		regionService.Setup(s => s.GetAllRegionsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				new AysoRegionDto { Id = 1, RegionNumber = 42, Name = "Newbury Park" },
				new AysoRegionDto { Id = 2, RegionNumber = 121, Name = "Simi Valley" },
			]);

		var teamAdminService = new Mock<ITeamAdminService>(MockBehavior.Strict);
		teamAdminService.Setup(s => s.GetDivisionTeamsAsync(10, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionTeamsDto
			{
				DivisionId = 10,
				TournamentYearId = 2,
				Year = 2026,
				DivisionKey = "10u-boys",
				ShortCode = "10UB",
				DivisionName = "10U Boys",
				Pools =
				[
					new PoolTeamsDto
					{
						PoolId = 100,
						PoolName = "A",
						Teams =
						[
							new TeamAdminItemDto
							{
								Id = 501,
								PoolId = 100,
								PoolName = "A",
								TeamNumber = 1,
								Code = "A1",
								RegionNumber = 42,
								RegionName = "Newbury Park",
								CoachName = "Oakman",
								DisplayName = "Newbury Park: Oakman",
								IsHostRegion = true,
								HasVolunteerPointDeduction = true,
							},
						],
					},
				],
			});

		var controller = CreateController(yearService.Object, regionService.Object, teamAdminService.Object);
		var result = await controller.Teams(yearId: 2, division: "10u-boys");

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<AdminTeamsViewModel>(view.Model);
		Assert.Equal(10, model.SelectedDivisionId);
		Assert.Single(model.Teams);
		Assert.Equal("A1", model.Teams[0].Code);
		Assert.True(model.Teams[0].HasVolunteerPointDeduction);
	}

	[Fact]
	public async Task SaveTeamsPost_DelegatesToTeamAdminServiceAndRedirects()
	{
		var teamAdminService = new Mock<ITeamAdminService>(MockBehavior.Strict);
		teamAdminService.Setup(s => s.UpdateDivisionTeamsAsync(
				It.Is<DivisionTeamsUpdateDto>(d =>
					d.DivisionId == 10 &&
					d.Teams.Count == 2 &&
					d.Teams[0].TeamId == 501 &&
					d.Teams[0].CoachName == "Oakman" &&
					d.Teams[0].HasVolunteerPointDeduction &&
					d.Teams[1].TeamId == 502 &&
					!d.Teams[1].HasVolunteerPointDeduction),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionTeamsDto
			{
				DivisionId = 10,
				TournamentYearId = 2,
				Year = 2026,
				DivisionKey = "10u-boys",
				DivisionName = "10U Boys",
			});

		var controller = CreateController(
			Mock.Of<ITournamentYearService>(),
			Mock.Of<IAysoRegionService>(),
			teamAdminService.Object);

		var result = await controller.SaveTeams(new AdminTeamsViewModel
		{
			SelectedTournamentYearId = 2,
			SelectedDivisionId = 10,
			Teams =
			[
				new AdminTeamEditRowViewModel
				{
					TeamId = 501,
					Code = "A1",
					RegionNumber = 42,
					CoachName = "Oakman",
					HasVolunteerPointDeduction = true,
				},
				new AdminTeamEditRowViewModel
				{
					TeamId = 502,
					Code = "A2",
					RegionNumber = 121,
					CoachName = "Wright",
					HasVolunteerPointDeduction = true, // non-Region-42 stripped by controller + service
				},
			],
		});

		var redirect = Assert.IsType<RedirectToActionResult>(result);
		Assert.Equal(nameof(AdminController.Teams), redirect.ActionName);
		Assert.Equal(2, redirect.RouteValues?["yearId"]);
		Assert.Equal("10u-boys", redirect.RouteValues?["division"]);
		Assert.NotNull(controller.TempData["SuccessMessage"]);
	}

	private static AdminController CreateController(
		ITournamentYearService yearService,
		IAysoRegionService regionService,
		ITeamAdminService? teamAdminService = null)
	{
		var httpContext = new DefaultHttpContext();
		return new AdminController(yearService, regionService, teamAdminService ?? Mock.Of<ITeamAdminService>())
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
			TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>()),
		};
	}
}
