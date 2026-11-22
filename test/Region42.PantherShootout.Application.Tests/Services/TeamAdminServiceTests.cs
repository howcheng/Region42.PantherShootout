using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class TeamAdminServiceTests
{
	[Fact]
	public async Task GetDivisionTeamsAsync_ReturnsOrderedPoolsAndTeamsWithHostFlags()
	{
		var year = new TournamentYear { Id = 1, Year = 2026, Name = "2026 Panther Shootout" };
		var division = new Division
		{
			Id = 10,
			TournamentYearId = year.Id,
			AgeGroup = AgeGroup.U10,
			Gender = Gender.Girls,
			Name = "10U Girls",
			Size = DivisionSize.Six,
		};
		var poolB = new Pool { Id = 101, DivisionId = division.Id, Name = "B" };
		var poolA = new Pool { Id = 100, DivisionId = division.Id, Name = "A" };

		var teams = new List<Team>
		{
			new()
			{
				Id = 1002,
				PoolId = poolA.Id,
				Pool = poolA,
				TeamNumber = 2,
				RegionNumber = 121,
				RegionName = "Simi Valley",
				CoachName = "Wright",
				HasVolunteerPointDeduction = true, // non-host flag should report false
			},
			new()
			{
				Id = 1001,
				PoolId = poolA.Id,
				Pool = poolA,
				TeamNumber = 1,
				RegionNumber = Team.HostRegionNumber,
				RegionName = "Newbury Park",
				CoachName = "Oakman",
				HasVolunteerPointDeduction = true,
			},
			new()
			{
				Id = 1003,
				PoolId = poolB.Id,
				Pool = poolB,
				TeamNumber = 1,
				RegionNumber = 9,
				RegionName = "Thousand Oaks",
				CoachName = "Lee",
			},
		};

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.LoadAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.SetupGet(c => c.Divisions).Returns([division]);
		context.SetupGet(c => c.TournamentYears).Returns([year]);
		context.SetupGet(c => c.Regions).Returns(
		[
			new AysoRegion { Id = 1, RegionNumber = 42, Name = "Newbury Park" },
			new AysoRegion { Id = 2, RegionNumber = 121, Name = "Simi Valley" },
			new AysoRegion { Id = 3, RegionNumber = 9, Name = "Thousand Oaks" },
		]);
		context.SetupGet(c => c.Pools).Returns([poolB, poolA]);
		context.SetupGet(c => c.Teams).Returns(teams);

		var service = new TeamAdminService(context.Object, Mock.Of<IStandingsRefreshService>());
		var dto = await service.GetDivisionTeamsAsync(division.Id);

		Assert.NotNull(dto);
		Assert.Equal("10u-girls", dto!.DivisionKey);
		Assert.Equal(["A", "B"], dto.Pools.Select(p => p.PoolName).ToArray());

		var a1 = dto.Pools[0].Teams[0];
		Assert.Equal("A1", a1.Code);
		Assert.True(a1.IsHostRegion);
		Assert.True(a1.HasVolunteerPointDeduction);

		var a2 = dto.Pools[0].Teams[1];
		Assert.Equal("A2", a2.Code);
		Assert.False(a2.IsHostRegion);
		Assert.False(a2.HasVolunteerPointDeduction);
	}

	[Fact]
	public async Task UpdateDivisionTeamsAsync_UpdatesRegionCoachAndHostDeduction_PreservesCodeAndRefreshesStandings()
	{
		var year = new TournamentYear { Id = 1, Year = 2026, Name = "2026 Panther Shootout" };
		var division = new Division
		{
			Id = 10,
			TournamentYearId = year.Id,
			AgeGroup = AgeGroup.U12,
			Gender = Gender.Boys,
			Name = "12U Boys",
			Size = DivisionSize.Four,
		};
		var poolA = new Pool { Id = 100, DivisionId = division.Id, Name = "A" };

		var team1 = new Team
		{
			Id = 501,
			PoolId = poolA.Id,
			Pool = poolA,
			TeamNumber = 1,
			RegionNumber = Team.HostRegionNumber,
			RegionName = "Newbury Park",
			CoachName = "Oakmn", // misspelled
			HasVolunteerPointDeduction = false,
		};
		var team2 = new Team
		{
			Id = 502,
			PoolId = poolA.Id,
			Pool = poolA,
			TeamNumber = 2,
			RegionNumber = Team.HostRegionNumber,
			RegionName = "Newbury Park",
			CoachName = "Carter",
			HasVolunteerPointDeduction = true, // had deduction, now switching away from Region 42 -> must clear!
		};

		var updatedTeams = new List<Team>();
		var tx = new Mock<ITransaction>(MockBehavior.Strict);
		tx.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		tx.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.LoadAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.Setup(c => c.LoadDivisionDataAsync(division.Id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.SetupGet(c => c.Divisions).Returns([division]);
		context.SetupGet(c => c.TournamentYears).Returns([year]);
		context.SetupGet(c => c.Pools).Returns([poolA]);
		context.SetupGet(c => c.Teams).Returns([team1, team2]);
		context.SetupGet(c => c.Regions).Returns(
		[
			new AysoRegion { Id = 1, RegionNumber = 42, Name = "Newbury Park" },
			new AysoRegion { Id = 2, RegionNumber = 121, Name = "Simi Valley" },
		]);
		context.Setup(c => c.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
		context.Setup(c => c.Update(It.IsAny<Team>())).Callback<Team>(updatedTeams.Add);
		context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

		var refreshService = new Mock<IStandingsRefreshService>(MockBehavior.Strict);
		refreshService.Setup(r => r.RefreshDivisionAsync(division.Id, false, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new DivisionStandingsSnapshotDocument());

		var service = new TeamAdminService(context.Object, refreshService.Object);
		var result = await service.UpdateDivisionTeamsAsync(new DivisionTeamsUpdateDto
		{
			DivisionId = division.Id,
			Teams =
			[
				new TeamUpdateItemDto
				{
					TeamId = 501,
					RegionNumber = 42,
					CoachName = "  Oakman  ",
					HasVolunteerPointDeduction = true,
				},
				new TeamUpdateItemDto
				{
					TeamId = 502,
					RegionNumber = 121,
					CoachName = "Carter",
					HasVolunteerPointDeduction = true, // must be forced false because RegionNumber != 42
				},
			],
		});

		Assert.Equal(2, updatedTeams.Count);
		Assert.Equal("A1", team1.Code);
		Assert.Equal(100, team1.PoolId);
		Assert.Equal(1, team1.TeamNumber);
		Assert.Equal("Oakman", team1.CoachName);
		Assert.Equal("Newbury Park: Oakman", team1.Name);
		Assert.True(team1.HasVolunteerPointDeduction);

		Assert.Equal("A2", team2.Code);
		Assert.Equal(121, team2.RegionNumber);
		Assert.Equal("Simi Valley", team2.RegionName);
		Assert.Equal("Simi Valley: Carter", team2.Name);
		Assert.False(team2.HasVolunteerPointDeduction);

		Assert.Equal("Newbury Park: Oakman", result.Pools[0].Teams[0].DisplayName);
		Assert.False(result.Pools[0].Teams[1].HasVolunteerPointDeduction);

		refreshService.Verify(r => r.RefreshDivisionAsync(division.Id, false, It.IsAny<CancellationToken>()), Times.Once);
		context.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
		tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task UpdateDivisionTeamsAsync_RejectsUnregisteredRegionOrForeignTeam()
	{
		var division = new Division { Id = 10, TournamentYearId = 1, Name = "10U Boys", Size = DivisionSize.Four };
		var poolA = new Pool { Id = 100, DivisionId = division.Id, Name = "A" };
		var team1 = new Team { Id = 501, PoolId = poolA.Id, TeamNumber = 1, RegionNumber = 42, CoachName = "Oakman" };

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.LoadAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.Setup(c => c.LoadDivisionDataAsync(division.Id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.SetupGet(c => c.Divisions).Returns([division]);
		context.SetupGet(c => c.Pools).Returns([poolA]);
		context.SetupGet(c => c.Teams).Returns([team1]);
		context.SetupGet(c => c.Regions).Returns([new AysoRegion { Id = 1, RegionNumber = 42, Name = "Newbury Park" }]);

		var service = new TeamAdminService(context.Object, Mock.Of<IStandingsRefreshService>());

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			service.UpdateDivisionTeamsAsync(new DivisionTeamsUpdateDto
			{
				DivisionId = division.Id,
				Teams = [new TeamUpdateItemDto { TeamId = 501, RegionNumber = 999, CoachName = "Oakman" }],
			}));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			service.UpdateDivisionTeamsAsync(new DivisionTeamsUpdateDto
			{
				DivisionId = division.Id,
				Teams = [new TeamUpdateItemDto { TeamId = 9999, RegionNumber = 42, CoachName = "Foreign" }],
			}));
	}

	[Fact]
	public void SynchronousParameterValidation_ThrowsImmediately()
	{
		var service = new TeamAdminService(
			Mock.Of<ITournamentDataContext>(),
			Mock.Of<IStandingsRefreshService>());

		Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.GetDivisionTeamsAsync(0); });
		Assert.Throws<ArgumentNullException>(() => { _ = service.UpdateDivisionTeamsAsync(null!); });
		Assert.Throws<ArgumentOutOfRangeException>(() =>
		{
			_ = service.UpdateDivisionTeamsAsync(new DivisionTeamsUpdateDto { DivisionId = 0 });
		});
		Assert.Throws<ArgumentException>(() =>
		{
			_ = service.UpdateDivisionTeamsAsync(new DivisionTeamsUpdateDto { DivisionId = 1, Teams = [] });
		});
		Assert.Throws<ArgumentException>(() =>
		{
			_ = service.UpdateDivisionTeamsAsync(new DivisionTeamsUpdateDto
			{
				DivisionId = 1,
				Teams =
				[
					new TeamUpdateItemDto { TeamId = 1, RegionNumber = 42, CoachName = "A" },
					new TeamUpdateItemDto { TeamId = 1, RegionNumber = 42, CoachName = "B" },
				],
			});
		});
		Assert.Throws<ArgumentException>(() =>
		{
			_ = service.UpdateDivisionTeamsAsync(new DivisionTeamsUpdateDto
			{
				DivisionId = 1,
				Teams = [new TeamUpdateItemDto { TeamId = 1, RegionNumber = 42, CoachName = "   " }],
			});
		});
	}
}
