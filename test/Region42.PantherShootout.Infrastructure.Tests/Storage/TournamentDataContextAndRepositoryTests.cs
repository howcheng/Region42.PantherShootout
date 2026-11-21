using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Region42.PantherShootout.Domain.Documents;
using Region42.PantherShootout.Domain.Entities;
using Region42.PantherShootout.Domain.Enums;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Infrastructure.DependencyInjection;
using Region42.PantherShootout.Infrastructure.Repositories;
using Region42.PantherShootout.Infrastructure.Storage;
using Xunit;
using StorageClient = Google.Cloud.Storage.V1.StorageClient;

namespace Region42.PantherShootout.Infrastructure.Tests.Storage;

public sealed class TournamentDataContextAndRepositoryTests : IDisposable
{
	private readonly string _tempRoot;
	private readonly LocalFileTournamentDataStore _store;
	private readonly IOptions<StorageOptions> _storageOptions;

	public TournamentDataContextAndRepositoryTests()
	{
		_tempRoot = Path.Combine(Path.GetTempPath(), "region42-pso-context-tests", Guid.NewGuid().ToString("N"));
		_storageOptions = Options.Create(new StorageOptions
		{
			Provider = "LocalFile",
			LocalRoot = _tempRoot,
			CompetitionSlug = "panther-shootout",
		});
		_store = new LocalFileTournamentDataStore(_storageOptions);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempRoot))
		{
			Directory.Delete(_tempRoot, recursive: true);
		}
	}

	private TournamentDataContext CreateContext(string userIdentifier = "admin@aysoregion42.org")
	{
		var currentUser = new FakeCurrentUserService(userIdentifier);
		return new TournamentDataContext(_store, _storageOptions, currentUser);
	}

	[Fact]
	public async Task EndToEndPersistenceAndRehydration_WiresNavigationsStampsAuditAndAvoidsDirtyReads()
	{
		// Scope 1: Populate full hierarchy + division data via repositories and save
		var context1 = CreateContext("scorekeeper@aysoregion42.org");
		var yearRepo1 = new Repository<TournamentYear>(context1);
		var divisionRepo1 = new Repository<Division>(context1);
		var poolRepo1 = new Repository<Pool>(context1);
		var teamRepo1 = new Repository<Team>(context1);
		var poolGameRepo1 = new Repository<PoolPlayGame>(context1);
		var champGameRepo1 = new Repository<ChampionshipGame>(context1);
		var shootoutRepo1 = new Repository<ShootoutRound>(context1);
		var overrideRepo1 = new Repository<TiebreakerOverride>(context1);

		var year = new TournamentYear { Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
		await yearRepo1.AddAsync(year);

		var division = new Division
		{
			TournamentYearId = year.Id,
			AgeGroup = AgeGroup.U12,
			Gender = Gender.Girls,
			Name = "12U Girls",
			Size = DivisionSize.Six,
		};
		await divisionRepo1.AddAsync(division);

		var poolA = new Pool { DivisionId = division.Id, Name = "A" };
		await poolRepo1.AddAsync(poolA);

		var teamA1 = new Team { PoolId = poolA.Id, TeamNumber = 1, Name = "Newbury Park: Smith" };
		var teamA2 = new Team { PoolId = poolA.Id, TeamNumber = 2, Name = "Simi Valley: Jones" };
		await teamRepo1.AddAsync(teamA1);
		await teamRepo1.AddAsync(teamA2);

		var poolGame = new PoolPlayGame
		{
			DivisionId = division.Id,
			PoolId = poolA.Id,
			Round = 1,
			Location = "Field 1",
			HomeTeamId = teamA1.Id,
			AwayTeamId = teamA2.Id,
			HomeGoals = 2,
			AwayGoals = 1,
			HomeYellowCards = 1,
		};
		await poolGameRepo1.AddAsync(poolGame);

		var shootoutRound1 = new ShootoutRound
		{
			DivisionId = division.Id,
			Round = 1,
			PoolPlayGameId = poolGame.Id,
			HomeTeamId = teamA1.Id,
			AwayTeamId = teamA2.Id,
			HomePkGoals = 4,
			AwayPkGoals = 3,
		};
		await shootoutRepo1.AddAsync(shootoutRound1);

		var semifinal1 = new ChampionshipGame
		{
			DivisionId = division.Id,
			Slot = BracketSlot.Semifinal1,
			HomeSourceDescription = "Pool A 1st",
			AwaySourceDescription = "Pool B 2nd",
			HomeTeamId = teamA1.Id,
			AwayTeamId = teamA2.Id,
		};
		await champGameRepo1.AddAsync(semifinal1);

		var shootoutRound4 = new ShootoutRound
		{
			DivisionId = division.Id,
			Round = 4,
			ChampionshipGameId = semifinal1.Id,
			HomeTeamId = teamA1.Id,
			AwayTeamId = teamA2.Id,
			HomePkGoals = 5,
			AwayPkGoals = 4,
		};
		await shootoutRepo1.AddAsync(shootoutRound4);

		var kftm = new TiebreakerOverride
		{
			DivisionId = division.Id,
			Scope = TiebreakerScope.PoolPlayStandings,
			TeamAId = teamA1.Id,
			TeamBId = teamA2.Id,
			WinningTeamId = teamA1.Id,
		};
		await overrideRepo1.AddAsync(kftm);

		context1.SetStandingsForDivision(division.Id, new DivisionStandingsSnapshotDocument
		{
			CalculatedAt = DateTime.UtcNow,
			ChampionTeamId = teamA1.Id,
		});

		Assert.Equal("scorekeeper@aysoregion42.org", poolGame.CreatedBy);
		Assert.Equal(1, poolGame.RowVersion);

		var savedDocumentCount = await yearRepo1.SaveChangesAsync();
		Assert.Equal(3, savedDocumentCount); // index.json + tournament.json + 12u-girls.json

		// Scope 2: Rehydrate in a brand-new context and verify navigation properties + zero dirty writes on read
		var context2 = CreateContext();
		var teamRepo2 = new Repository<Team>(context2);
		var poolGameRepo2 = new Repository<PoolPlayGame>(context2);
		var champGameRepo2 = new Repository<ChampionshipGame>(context2);
		var overrideRepo2 = new Repository<TiebreakerOverride>(context2);

		var loadedTeamA1 = await teamRepo2.GetByIdAsync(teamA1.Id);
		Assert.NotNull(loadedTeamA1);
		Assert.Equal("A1", loadedTeamA1!.GetDesignation());
		Assert.Equal("12U Girls", loadedTeamA1.Pool?.Division?.Name);
		Assert.Equal(2026, loadedTeamA1.Pool?.Division?.TournamentYear?.Year);

		var loadedPoolGames = await poolGameRepo2.FindAsync(g => g.Round == 1);
		var loadedPoolGame = Assert.Single(loadedPoolGames);
		Assert.NotNull(loadedPoolGame.ShootoutRound);
		Assert.Equal(4, loadedPoolGame.ShootoutRound!.HomePkGoals);
		Assert.Equal("scorekeeper@aysoregion42.org", loadedPoolGame.ModifiedBy);

		var loadedSemifinal = await champGameRepo2.GetByIdAsync(semifinal1.Id);
		Assert.NotNull(loadedSemifinal?.ShootoutRound);
		Assert.Equal(5, loadedSemifinal!.ShootoutRound!.HomePkGoals);

		var loadedOverrides = await overrideRepo2.GetAllAsync();
		Assert.Single(loadedOverrides);
		Assert.Equal(teamA1.Id, context2.GetStandingsForDivision(division.Id)?.ChampionTeamId);

		// Calling SaveChangesAsync without mutations must flush 0 documents
		Assert.Equal(0, await teamRepo2.SaveChangesAsync());

		// Newly allocated ID in Scope 2 must exceed all existing IDs
		var nextAllocatedId = context2.AllocateNextId();
		Assert.True(nextAllocatedId > kftm.Id);
	}

	[Fact]
	public async Task GranularDirtyTracking_FlushesOnlyModifiedDivisionDocument()
	{
		var context1 = CreateContext();
		var year = new TournamentYear { Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
		context1.Add(year);

		var boysDiv = new Division
		{
			TournamentYearId = year.Id,
			AgeGroup = AgeGroup.U10,
			Gender = Gender.Boys,
			Name = "10U Boys",
			Size = DivisionSize.Four,
		};
		var girlsDiv = new Division
		{
			TournamentYearId = year.Id,
			AgeGroup = AgeGroup.U10,
			Gender = Gender.Girls,
			Name = "10U Girls",
			Size = DivisionSize.Four,
		};
		context1.Add(boysDiv);
		context1.Add(girlsDiv);

		var boysPool = new Pool { DivisionId = boysDiv.Id, Name = "A" };
		var girlsPool = new Pool { DivisionId = girlsDiv.Id, Name = "A" };
		context1.Add(boysPool);
		context1.Add(girlsPool);

		var boysTeam1 = new Team { PoolId = boysPool.Id, TeamNumber = 1, Name = "Boys 1" };
		var boysTeam2 = new Team { PoolId = boysPool.Id, TeamNumber = 2, Name = "Boys 2" };
		var girlsTeam1 = new Team { PoolId = girlsPool.Id, TeamNumber = 1, Name = "Girls 1" };
		var girlsTeam2 = new Team { PoolId = girlsPool.Id, TeamNumber = 2, Name = "Girls 2" };
		context1.Add(boysTeam1);
		context1.Add(boysTeam2);
		context1.Add(girlsTeam1);
		context1.Add(girlsTeam2);

		var boysGame = new PoolPlayGame
		{
			DivisionId = boysDiv.Id,
			PoolId = boysPool.Id,
			Round = 1,
			HomeTeamId = boysTeam1.Id,
			AwayTeamId = boysTeam2.Id,
		};
		var girlsGame = new PoolPlayGame
		{
			DivisionId = girlsDiv.Id,
			PoolId = girlsPool.Id,
			Round = 1,
			HomeTeamId = girlsTeam1.Id,
			AwayTeamId = girlsTeam2.Id,
		};
		context1.Add(boysGame);
		context1.Add(girlsGame);

		Assert.Equal(4, await context1.SaveChangesAsync()); // index + tournament + 10u-boys + 10u-girls

		// Scope 2: Only enter a score in 10u-boys
		var context2 = CreateContext("referee@aysoregion42.org");
		var gameRepo2 = new Repository<PoolPlayGame>(context2);
		var gameToUpdate = await gameRepo2.GetByIdAsync(boysGame.Id);
		Assert.NotNull(gameToUpdate);

		gameToUpdate!.HomeGoals = 3;
		gameToUpdate.AwayGoals = 0;
		gameRepo2.Update(gameToUpdate);

		var flushedFiles = await gameRepo2.SaveChangesAsync();
		Assert.Equal(1, flushedFiles);
		Assert.Equal(2, gameToUpdate.RowVersion);
		Assert.Equal("referee@aysoregion42.org", gameToUpdate.ModifiedBy);

		Assert.Equal(1L, (await _store.GetIndexAsync()).Generation);
		Assert.Equal(1L, (await _store.GetTournamentDocumentAsync(2026, "panther-shootout")).Generation);
		Assert.Equal(2L, (await _store.GetDivisionDataAsync(2026, "panther-shootout", "10u-boys")).Generation);
		Assert.Equal(1L, (await _store.GetDivisionDataAsync(2026, "panther-shootout", "10u-girls")).Generation);
	}

	[Fact]
	public async Task Transactions_RollbackAndUncommittedDispose_DiscardAllInMemoryChanges()
	{
		var context = CreateContext();
		var year = new TournamentYear { Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
		context.Add(year);

		var division = new Division
		{
			TournamentYearId = year.Id,
			AgeGroup = AgeGroup.U14,
			Gender = Gender.Boys,
			Name = "14U Boys",
			Size = DivisionSize.Four,
		};
		context.Add(division);

		var pool = new Pool { DivisionId = division.Id, Name = "A" };
		context.Add(pool);

		var team1 = new Team { PoolId = pool.Id, TeamNumber = 1, Name = "Original Team 1" };
		context.Add(team1);
		await context.SaveChangesAsync();

		var teamRepo = new Repository<Team>(context);

		// Case 1: Explicit RollbackAsync()
		await using (var tx = await teamRepo.BeginTransactionAsync())
		{
			team1.Name = "Mutated During Failed Import";
			teamRepo.Update(team1);

			await teamRepo.AddAsync(new Team { PoolId = pool.Id, TeamNumber = 2, Name = "Uncommitted Team 2" });
			context.SetStandingsForDivision(division.Id, new DivisionStandingsSnapshotDocument { ChampionTeamId = team1.Id });

			await tx.RollbackAsync();
		}

		var teamsAfterRollback = await teamRepo.GetAllAsync();
		var restoredTeam = Assert.Single(teamsAfterRollback);
		Assert.Equal("Original Team 1", restoredTeam.Name);
		Assert.Equal("A1", restoredTeam.GetDesignation());
		Assert.Null(context.GetStandingsForDivision(division.Id));
		Assert.Equal(0, await teamRepo.SaveChangesAsync());

		// Case 2: Disposing transaction without calling CommitAsync() automatically rolls back
		await using (await teamRepo.BeginTransactionAsync())
		{
			await teamRepo.AddAsync(new Team { PoolId = pool.Id, TeamNumber = 3, Name = "Abandoned Team" });
		}

		Assert.Single(await teamRepo.GetAllAsync());
		Assert.Equal(0, await teamRepo.SaveChangesAsync());

		// Case 3: CommitAsync() retains changes and SaveChangesAsync() flushes them
		await using (var commitTx = await teamRepo.BeginTransactionAsync())
		{
			await teamRepo.AddAsync(new Team { PoolId = pool.Id, TeamNumber = 2, Name = "Committed Team 2" });
			await commitTx.CommitAsync();
		}

		Assert.Equal(2, (await teamRepo.GetAllAsync()).Count);
		Assert.Equal(1, await teamRepo.SaveChangesAsync());
	}

	[Fact]
	public async Task LoadDivisionDataAsync_OnFreshContextWithoutPriorLoadAll_HydratesAndAllowsSavingScores()
	{
		// Scope 1: Seed tournament year, division, pool, teams, and pool-play game
		var seedContext = CreateContext("admin@aysoregion42.org");
		var year = new TournamentYear { Year = 2026, Name = "2026 Panther Shootout", IsActive = true };
		seedContext.Add(year);

		var division = new Division
		{
			Id = 22,
			TournamentYearId = year.Id,
			AgeGroup = AgeGroup.U10,
			Gender = Gender.Boys,
			Name = "10U Boys",
			Size = DivisionSize.Four,
		};
		seedContext.Add(division);

		var pool = new Pool { DivisionId = division.Id, Name = "C" };
		seedContext.Add(pool);

		var team1 = new Team { PoolId = pool.Id, TeamNumber = 1, RegionNumber = 9, CoachName = "Mallord" };
		var team2 = new Team { PoolId = pool.Id, TeamNumber = 2, RegionNumber = 42, CoachName = "Oakman" };
		seedContext.Add(team1);
		seedContext.Add(team2);

		var game = new PoolPlayGame
		{
			DivisionId = division.Id,
			PoolId = pool.Id,
			Round = 1,
			HomeTeamId = team1.Id,
			AwayTeamId = team2.Id,
		};
		seedContext.Add(game);
		await seedContext.SaveChangesAsync();

		// Scope 2: Fresh scoped context (simulating POST /Scores/SaveBatch) calling LoadDivisionDataAsync directly
		var postScopeContext = CreateContext("scorekeeper@aysoregion42.org");
		await postScopeContext.LoadDivisionDataAsync(division.Id);

		Assert.Contains(postScopeContext.Divisions, d => d.Id == division.Id);
		var loadedGame = Assert.Single(postScopeContext.PoolPlayGames, g => g.Id == game.Id);
		Assert.Equal("Thousand Oaks", loadedGame.HomeTeam?.RegionName);

		loadedGame.HomeGoals = 2;
		loadedGame.AwayGoals = 1;
		postScopeContext.Update(loadedGame);
		Assert.Equal(1, await postScopeContext.SaveChangesAsync());

		// Scope 3: Verify score persisted and non-existent division still throws
		var verifyContext = CreateContext();
		await verifyContext.LoadDivisionDataAsync(division.Id);
		var persistedGame = Assert.Single(verifyContext.PoolPlayGames, g => g.Id == game.Id);
		Assert.Equal(2, persistedGame.HomeGoals);
		Assert.Equal(1, persistedGame.AwayGoals);

		var missingContext = CreateContext();
		await Assert.ThrowsAsync<InvalidOperationException>(() => missingContext.LoadDivisionDataAsync(9999));
	}

	[Fact]
	public void AddInfrastructure_SwitchesCleanlyBetweenLocalFileAndGcsProviders()
	{
		var localConfig = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Storage:Provider"] = "LocalFile",
				["Storage:LocalRoot"] = _tempRoot,
			})
			.Build();

		var localServices = new ServiceCollection();
		localServices.AddScoped<ICurrentUserService>(_ => new FakeCurrentUserService("System"));
		localServices.AddInfrastructure(localConfig);

		using var localProvider = localServices.BuildServiceProvider();
		using var localScope = localProvider.CreateScope();
		Assert.IsType<LocalFileTournamentDataStore>(localScope.ServiceProvider.GetRequiredService<ITournamentDataStore>());
		Assert.IsType<LocalFileStorageWriteLock>(localScope.ServiceProvider.GetRequiredService<IStorageWriteLock>());
		Assert.IsType<TournamentDataContext>(localScope.ServiceProvider.GetRequiredService<ITournamentDataContext>());
		Assert.IsType<Repository<Team>>(localScope.ServiceProvider.GetRequiredService<IRepository<Team>>());

		var gcsConfig = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Storage:Provider"] = "Gcs",
				["Storage:BucketName"] = "region42-storage",
			})
			.Build();

		var gcsServices = new ServiceCollection();
		gcsServices.AddSingleton(new Mock<StorageClient>().Object);
		gcsServices.AddScoped<ICurrentUserService>(_ => new FakeCurrentUserService("System"));
		gcsServices.AddInfrastructure(gcsConfig);

		using var gcsProvider = gcsServices.BuildServiceProvider();
		using var gcsScope = gcsProvider.CreateScope();
		Assert.IsType<GcsTournamentDataStore>(gcsScope.ServiceProvider.GetRequiredService<ITournamentDataStore>());
		Assert.IsType<GcsStorageWriteLock>(gcsScope.ServiceProvider.GetRequiredService<IStorageWriteLock>());
	}

	private sealed class FakeCurrentUserService(string userIdentifier) : ICurrentUserService
	{
		public string UserIdentifier { get; } = userIdentifier;
	}
}
