using Microsoft.EntityFrameworkCore;
using Region42.PantherShootout.Domain.Entities;

namespace Region42.PantherShootout.Infrastructure;

/// <summary>
/// EF Core DbContext for the tournament app, mapped exclusively to the "pso" Postgres schema so
/// it coexists safely alongside another application's tables on the same shared database instance.
/// </summary>
public class PantherShootoutDbContext : DbContext
{
	public PantherShootoutDbContext(DbContextOptions<PantherShootoutDbContext> options)
		: base(options)
	{
	}

	public DbSet<TournamentYear> TournamentYears => Set<TournamentYear>();

	public DbSet<Division> Divisions => Set<Division>();

	public DbSet<Pool> Pools => Set<Pool>();

	public DbSet<Team> Teams => Set<Team>();

	public DbSet<PoolPlayGame> PoolPlayGames => Set<PoolPlayGame>();

	public DbSet<ShootoutRound> ShootoutRounds => Set<ShootoutRound>();

	public DbSet<ChampionshipGame> ChampionshipGames => Set<ChampionshipGame>();

	public DbSet<TiebreakerOverride> TiebreakerOverrides => Set<TiebreakerOverride>();

	public DbSet<User> Users => Set<User>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.HasDefaultSchema("pso");
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(PantherShootoutDbContext).Assembly);
		base.OnModelCreating(modelBuilder);
	}
}
