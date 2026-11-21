using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class TournamentYearServiceTests
{
	[Fact]
	public async Task GetDefaultYearAsync_PrefersMostRecentYearWithImportedDivisions()
	{
		var years = new List<TournamentYear>
		{
			new() { Id = 1, Year = 2024, Name = "2024 Panther Shootout", IsActive = false },
			new() { Id = 2, Year = 2025, Name = "2025 Panther Shootout", IsActive = true },
			new() { Id = 3, Year = 2026, Name = "2026 Panther Shootout (Draft)", IsActive = false },
		};

		var divisions = new List<Division>
		{
			new() { Id = 10, TournamentYearId = 1, AgeGroup = AgeGroup.U10, Gender = Gender.Girls, Name = "10U Girls", Size = DivisionSize.Eight },
			new() { Id = 20, TournamentYearId = 2, AgeGroup = AgeGroup.U10, Gender = Gender.Girls, Name = "10U Girls", Size = DivisionSize.Eight },
		};

		var pools = new List<Pool>
		{
			new() { Id = 100, DivisionId = 20, Name = "A" },
		};

		var teams = new List<Team>
		{
			new() { Id = 1000, PoolId = 100, TeamNumber = 1, RegionNumber = 42, CoachName = "Carter" },
		};

		var context = SetupReadContext(years, divisions, pools, teams);
		var service = new TournamentYearService(context.Object);

		var defaultYear = await service.GetDefaultYearAsync();

		Assert.NotNull(defaultYear);
		Assert.Equal(2025, defaultYear!.Year);
		Assert.Equal(1, defaultYear.DivisionCount);
		Assert.Equal(1, defaultYear.TeamCount);
	}

	[Fact]
	public async Task GetDefaultYearAsync_WhenNoYearHasDivisions_FallsBackToHighestYear()
	{
		var years = new List<TournamentYear>
		{
			new() { Id = 1, Year = 2025, Name = "2025 Panther Shootout", IsActive = true },
			new() { Id = 2, Year = 2026, Name = "2026 Panther Shootout", IsActive = false },
		};

		var context = SetupReadContext(years, [], [], []);
		var service = new TournamentYearService(context.Object);

		var defaultYear = await service.GetDefaultYearAsync();

		Assert.NotNull(defaultYear);
		Assert.Equal(2026, defaultYear!.Year);
	}

	[Fact]
	public async Task CreateYearAsync_CreatesYearDeactivatesPriorActiveAndAppliesDefaultName()
	{
		var existingActive = new TournamentYear
		{
			Id = 1,
			Year = 2025,
			Name = "2025 Panther Shootout",
			IsActive = true,
		};
		var years = new List<TournamentYear> { existingActive };

		var tx = new Mock<ITransaction>(MockBehavior.Strict);
		tx.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		tx.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

		var context = SetupReadContext(years, [], [], []);
		context.Setup(c => c.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
		context.Setup(c => c.AllocateNextId()).Returns(2);
		context.Setup(c => c.Update(existingActive));
		context.Setup(c => c.Add(It.IsAny<TournamentYear>())).Callback<TournamentYear>(years.Add);
		context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

		var service = new TournamentYearService(context.Object);

		var created = await service.CreateYearAsync(new TournamentYearCreateDto
		{
			Year = 2026,
			Name = "   ",
			IsActive = true,
			CustomMessage = "  Welcome to Newbury Park!  ",
		});

		Assert.Equal(2, created.Id);
		Assert.Equal(2026, created.Year);
		Assert.Equal("2026 Panther Shootout", created.Name);
		Assert.True(created.IsActive);
		Assert.Equal("Welcome to Newbury Park!", created.CustomMessage);
		Assert.False(existingActive.IsActive);
		tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task CreateYearAsync_ThrowsWhenDuplicateYearExists()
	{
		var years = new List<TournamentYear>
		{
			new() { Id = 1, Year = 2025, Name = "2025 Panther Shootout" },
		};

		var context = SetupReadContext(years, [], [], []);
		var service = new TournamentYearService(context.Object);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			service.CreateYearAsync(new TournamentYearCreateDto { Year = 2025 }));
	}

	[Fact]
	public async Task UpdateYearAsync_UpdatesMetadataAndExclusiveActiveFlag()
	{
		var year2024 = new TournamentYear { Id = 1, Year = 2024, Name = "2024", IsActive = true };
		var year2025 = new TournamentYear { Id = 2, Year = 2025, Name = "2025", IsActive = false };
		var years = new List<TournamentYear> { year2024, year2025 };

		var tx = new Mock<ITransaction>(MockBehavior.Strict);
		tx.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		tx.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

		var context = SetupReadContext(years, [], [], []);
		context.Setup(c => c.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
		context.Setup(c => c.Update(year2024));
		context.Setup(c => c.Update(year2025));
		context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

		var service = new TournamentYearService(context.Object);

		var updated = await service.UpdateYearAsync(new TournamentYearUpdateDto
		{
			Id = 2,
			Name = "  2025 Panther Shootout  ",
			IsActive = true,
			CustomMessage = "   ",
		});

		Assert.Equal("2025 Panther Shootout", updated.Name);
		Assert.True(updated.IsActive);
		Assert.Null(updated.CustomMessage);
		Assert.False(year2024.IsActive);
	}

	[Fact]
	public async Task UpdateDivisionCustomMessageAsync_UpdatesAndNormalizesBulletin()
	{
		var years = new List<TournamentYear>
		{
			new() { Id = 1, Year = 2025, Name = "2025 Panther Shootout" },
		};
		var division = new Division
		{
			Id = 12,
			TournamentYearId = 1,
			AgeGroup = AgeGroup.U12,
			Gender = Gender.Boys,
			Name = "12U Boys",
			Size = DivisionSize.Eight,
		};

		var context = SetupReadContext(years, [division], [], []);
		context.Setup(c => c.Update(division));
		context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

		var service = new TournamentYearService(context.Object);

		var result = await service.UpdateDivisionCustomMessageAsync(new DivisionBulletinUpdateDto
		{
			DivisionId = 12,
			CustomMessage = "  Field 3 moved to Field 4  ",
		});

		Assert.Equal("Field 3 moved to Field 4", result.CustomMessage);
		Assert.Equal("12u-boys", result.DivisionKey);
		Assert.Equal("12UB", result.ShortCode);
	}

	[Fact]
	public void SynchronousValidation_ThrowsImmediately()
	{
		var service = new TournamentYearService(Mock.Of<ITournamentDataContext>());

		Assert.Throws<ArgumentNullException>(() => { _ = service.CreateYearAsync(null!); });
		Assert.Throws<ArgumentOutOfRangeException>(() =>
		{
			_ = service.CreateYearAsync(new TournamentYearCreateDto { Year = 1999 });
		});
		Assert.Throws<ArgumentNullException>(() => { _ = service.UpdateYearAsync(null!); });
		Assert.Throws<ArgumentException>(() =>
		{
			_ = service.UpdateYearAsync(new TournamentYearUpdateDto { Id = 1, Name = " " });
		});
		Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.GetByIdAsync(0); });
		Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.GetByYearNumberAsync(1800); });
	}

	private static Mock<ITournamentDataContext> SetupReadContext(
		List<TournamentYear> years,
		List<Division> divisions,
		List<Pool> pools,
		List<Team> teams)
	{
		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.LoadAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.SetupGet(c => c.TournamentYears).Returns(years);
		context.SetupGet(c => c.Divisions).Returns(divisions);
		context.SetupGet(c => c.Pools).Returns(pools);
		context.SetupGet(c => c.Teams).Returns(teams);
		return context;
	}
}
