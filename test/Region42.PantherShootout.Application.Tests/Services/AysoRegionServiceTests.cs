using Moq;
using Region42.PantherShootout.Application.DTOs;
using Region42.PantherShootout.Application.Services;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Interfaces;
using Xunit;

namespace Region42.PantherShootout.Application.Tests.Services;

public class AysoRegionServiceTests
{
	[Fact]
	public async Task GetAllRegionsAsync_ReturnsOrderedRegionsWithTeamUsageCounts()
	{
		var regions = new List<AysoRegion>
		{
			new() { Id = 2, RegionNumber = 121, Name = "Simi Valley" },
			new() { Id = 1, RegionNumber = 42, Name = "Newbury Park" },
		};

		var teams = new List<Team>
		{
			new() { Id = 10, RegionNumber = 42, CoachName = "Carter" },
			new() { Id = 11, RegionNumber = 42, CoachName = "Oakman" },
		};

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.LoadAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.SetupGet(c => c.Regions).Returns(regions);
		context.SetupGet(c => c.Teams).Returns(teams);

		var service = new AysoRegionService(context.Object);
		var result = await service.GetAllRegionsAsync();

		Assert.Equal(2, result.Count);
		Assert.Equal(42, result[0].RegionNumber);
		Assert.Equal("Newbury Park", result[0].Name);
		Assert.Equal(2, result[0].TeamUsageCount);
		Assert.Equal(121, result[1].RegionNumber);
		Assert.Equal(0, result[1].TeamUsageCount);
	}

	[Fact]
	public async Task UpsertRegionAsync_CreatesNewRegionAndSaves()
	{
		var regions = new List<AysoRegion>();
		var teams = new List<Team>();

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.LoadAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.SetupGet(c => c.Regions).Returns(regions);
		context.SetupGet(c => c.Teams).Returns(teams);
		context.Setup(c => c.AllocateNextId()).Returns(99);
		context.Setup(c => c.Add(It.IsAny<AysoRegion>())).Callback<AysoRegion>(regions.Add);
		context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

		var service = new AysoRegionService(context.Object);
		var dto = await service.UpsertRegionAsync(new AysoRegionUpsertDto
		{
			RegionNumber = 76,
			Name = "  Beverly Hills  ",
		});

		Assert.Equal(99, dto.Id);
		Assert.Equal(76, dto.RegionNumber);
		Assert.Equal("Beverly Hills", dto.Name);
		Assert.Single(regions);
	}

	[Fact]
	public async Task UpsertRegionAsync_ThrowsWhenChangingToDuplicateRegionNumber()
	{
		var regions = new List<AysoRegion>
		{
			new() { Id = 1, RegionNumber = 42, Name = "Newbury Park" },
			new() { Id = 2, RegionNumber = 121, Name = "Simi Valley" },
		};

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.LoadAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.SetupGet(c => c.Regions).Returns(regions);

		var service = new AysoRegionService(context.Object);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			service.UpsertRegionAsync(new AysoRegionUpsertDto
			{
				Id = 2,
				RegionNumber = 42,
				Name = "Duplicate",
			}));
	}

	[Fact]
	public async Task DeleteRegionAsync_RejectsWhenReferencedByTeams_AndDeletesWhenUnused()
	{
		var inUse = new AysoRegion { Id = 1, RegionNumber = 42, Name = "Newbury Park" };
		var unused = new AysoRegion { Id = 2, RegionNumber = 999, Name = "Unused Region" };
		var regions = new List<AysoRegion> { inUse, unused };
		var teams = new List<Team> { new() { Id = 10, RegionNumber = 42, CoachName = "Carter" } };

		var context = new Mock<ITournamentDataContext>(MockBehavior.Strict);
		context.Setup(c => c.LoadAllAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
		context.SetupGet(c => c.Regions).Returns(regions);
		context.SetupGet(c => c.Teams).Returns(teams);
		context.Setup(c => c.Remove(unused)).Callback<AysoRegion>(r => regions.Remove(r));
		context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

		var service = new AysoRegionService(context.Object);

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteRegionAsync(inUse.Id));

		await service.DeleteRegionAsync(unused.Id);
		Assert.DoesNotContain(regions, r => r.Id == unused.Id);
	}

	[Fact]
	public void SynchronousParameterValidation_ThrowsImmediately()
	{
		var service = new AysoRegionService(Mock.Of<ITournamentDataContext>());

		Assert.Throws<ArgumentNullException>(() => { _ = service.UpsertRegionAsync(null!); });
		Assert.Throws<ArgumentOutOfRangeException>(() =>
		{
			_ = service.UpsertRegionAsync(new AysoRegionUpsertDto { RegionNumber = 0, Name = "Invalid" });
		});
		Assert.Throws<ArgumentException>(() =>
		{
			_ = service.UpsertRegionAsync(new AysoRegionUpsertDto { RegionNumber = 42, Name = "   " });
		});
		Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.DeleteRegionAsync(0); });
	}
}
