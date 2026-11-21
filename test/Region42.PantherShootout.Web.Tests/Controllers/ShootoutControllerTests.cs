using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Web.Controllers;
using Region42.PantherShootout.Web.Helpers;
using Region42.PantherShootout.Web.Models;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Controllers;

public class ShootoutControllerTests
{
	[Fact]
	public async Task Index_WithoutExplicitDivision_HonorsPreferredCookieAndMostRecentYear()
	{
		var yearDto = new TournamentYearDto { Id = 3, Year = 2026, Name = "2026 Panther Shootout" };
		var divisions = new List<DivisionSummaryDto>
		{
			new() { Id = 21, DivisionKey = "10u-girls", ShortCode = "10UG", Name = "10U Girls", Size = DivisionSize.Eight },
			new() { Id = 22, DivisionKey = "12u-girls", ShortCode = "12UG", Name = "12U Girls", Size = DivisionSize.Five },
		};

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([yearDto]);
		yearService.Setup(s => s.GetDefaultYearAsync(It.IsAny<CancellationToken>())).ReturnsAsync(yearDto);
		yearService.Setup(s => s.GetDivisionsForYearAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(divisions);

		var psoService = new Mock<IPsoStandingsService>(MockBehavior.Strict);
		psoService.Setup(p => p.CalculateForDivisionAsync(22, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PsoStandingsResult { DivisionId = 22, DivisionName = "12U Girls" });

		var dataContext = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		dataContext.Setup(c => c.LoadDivisionDataAsync(22, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		dataContext.SetupGet(c => c.Teams).Returns([]);
		dataContext.SetupGet(c => c.Pools).Returns([]);
		dataContext.SetupGet(c => c.PoolPlayGames).Returns([]);
		dataContext.SetupGet(c => c.ChampionshipGames).Returns([]);
		dataContext.SetupGet(c => c.ShootoutRounds).Returns([]);

		var httpContext = new DefaultHttpContext();
		httpContext.Request.Headers.Cookie = $"{DivisionPreferenceCookieHelper.CookieName}=12u-girls";

		var controller = new ShootoutController(yearService.Object, psoService.Object, dataContext.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
		};

		var result = await controller.Index();

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<ShootoutPageViewModel>(view.Model);
		Assert.Equal("12u-girls", model.SelectedDivision?.DivisionKey);
		Assert.Equal(4, model.PsoTable?.ScheduledRoundCount);
		Assert.True(model.PsoTable?.UsesFourthRoundCap);
	}

	[Fact]
	public async Task Index_WithExplicitDivision_UpdatesPreferredCookie()
	{
		var yearDto = new TournamentYearDto { Id = 3, Year = 2025, Name = "2025 Panther Shootout" };
		var divisions = new List<DivisionSummaryDto>
		{
			new() { Id = 21, DivisionKey = "10u-girls", ShortCode = "10UG", Name = "10U Girls", Size = DivisionSize.Six },
			new() { Id = 22, DivisionKey = "14u-boys", ShortCode = "14UB", Name = "14U Boys", Size = DivisionSize.Six },
		};

		var yearService = new Mock<ITournamentYearService>(MockBehavior.Strict);
		yearService.Setup(s => s.GetAllYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([yearDto]);
		yearService.Setup(s => s.GetDivisionsForYearAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(divisions);

		var psoService = new Mock<IPsoStandingsService>(MockBehavior.Strict);
		psoService.Setup(p => p.CalculateForDivisionAsync(22, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PsoStandingsResult { DivisionId = 22, DivisionName = "14U Boys" });

		var champGame = new ChampionshipGame
		{
			Id = 90,
			DivisionId = 22,
			Slot = BracketSlot.Semifinal1,
			HomeSourceDescription = "Pool A 1st",
			AwaySourceDescription = "Pool B 2nd",
			Location = "Field 2",
		};
		var r4Round = new ShootoutRound
		{
			Id = 500,
			DivisionId = 22,
			Round = 4,
			ChampionshipGameId = 90,
		};

		var dataContext = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		dataContext.Setup(c => c.LoadDivisionDataAsync(22, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		dataContext.SetupGet(c => c.Teams).Returns([]);
		dataContext.SetupGet(c => c.Pools).Returns([]);
		dataContext.SetupGet(c => c.PoolPlayGames).Returns([]);
		dataContext.SetupGet(c => c.ChampionshipGames).Returns([champGame]);
		dataContext.SetupGet(c => c.ShootoutRounds).Returns([r4Round]);

		var httpContext = new DefaultHttpContext();
		var controller = new ShootoutController(yearService.Object, psoService.Object, dataContext.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext },
		};

		var result = await controller.Index(year: 2025, division: "14u-boys");

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<ShootoutPageViewModel>(view.Model);
		Assert.True(model.HasFourthShootoutRound);
		Assert.True(model.PsoTable?.HasFourthShootoutRound);
		Assert.Single(model.Matchups);
		Assert.Equal("Semifinal 1", model.Matchups[0].SourceLabel);
		Assert.Contains($"{DivisionPreferenceCookieHelper.CookieName}=14u-boys", httpContext.Response.Headers.SetCookie.ToString());
	}
}
