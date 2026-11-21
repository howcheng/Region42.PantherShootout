using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Web.Controllers;
using Region42.PantherShootout.Web.Helpers;
using Region42.PantherShootout.Web.Models;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Controllers;

public class HomeControllerTests
{
	[Fact]
	public async Task Index_DefaultsToMostRecentYearAndPinsPreferredDivisionFromCookie()
	{
		var year2026 = new TournamentYearDto { Id = 2, Year = 2026, Name = "2026 Panther Shootout", DivisionCount = 2 };
		var year2025 = new TournamentYearDto { Id = 1, Year = 2025, Name = "2025 Panther Shootout", DivisionCount = 6 };

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync([year2026, year2025]);
		yearService.Setup(s => s.GetDefaultYearAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(year2026);
		yearService.Setup(s => s.GetDivisionsForYearAsync(2, It.IsAny<CancellationToken>()))
			.ReturnsAsync(
			[
				new DivisionSummaryDto { Id = 10, TournamentYearId = 2, Year = 2026, DivisionKey = "10u-girls", ShortCode = "10UG", Name = "10U Girls", Size = DivisionSize.Eight, PoolCount = 2, TeamCount = 8 },
				new DivisionSummaryDto { Id = 11, TournamentYearId = 2, Year = 2026, DivisionKey = "12u-boys", ShortCode = "12UB", Name = "12U Boys", Size = DivisionSize.Six, PoolCount = 2, TeamCount = 6 },
			]);

		var pool = new Pool { Id = 1, DivisionId = 11, Name = "A" };
		var champTeam = new Team { Id = 50, PoolId = 1, Pool = pool, TeamNumber = 1, RegionName = "Newbury Park", CoachName = "Oakman" };

		var dataContext = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		dataContext.Setup(c => c.LoadDivisionDataAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		dataContext.SetupGet(c => c.Teams).Returns([champTeam]);
		dataContext.SetupGet(c => c.PoolPlayGames).Returns([]);
		dataContext.SetupGet(c => c.ChampionshipGames).Returns([]);
		dataContext.Setup(c => c.GetStandingsForDivision(10)).Returns((DivisionStandingsSnapshotDocument?)null);
		dataContext.Setup(c => c.GetStandingsForDivision(11)).Returns(new DivisionStandingsSnapshotDocument
		{
			ChampionTeamId = 50,
		});

		var httpContext = new DefaultHttpContext();
		httpContext.Request.Headers.Cookie = $"{DivisionPreferenceCookieHelper.CookieName}=12u-boys";

		var controller = new HomeController(yearService.Object, dataContext.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
		};

		var result = await controller.Index();

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<HomeLandingViewModel>(view.Model);
		Assert.Equal(2026, model.SelectedYear?.Year);
		Assert.NotNull(model.PreferredDivision);
		Assert.Equal("12u-boys", model.PreferredDivision!.DivisionKey);
		Assert.Equal("A1 Newbury Park: Oakman", model.PreferredDivision.ChampionTeamDisplay);
	}
}
