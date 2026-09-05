using Microsoft.EntityFrameworkCore;

namespace Region42.PantherShootout.Infrastructure;

/// <summary>
/// EF Core DbContext for the tournament app, mapped exclusively to the "pso" Postgres schema so
/// it coexists safely alongside another application's tables on the same shared database instance.
/// DbSets and entity configurations will be added in a later step.
/// </summary>
public class PantherShootoutDbContext : DbContext
{
	public PantherShootoutDbContext(DbContextOptions<PantherShootoutDbContext> options)
		: base(options)
	{
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.HasDefaultSchema("pso");
		base.OnModelCreating(modelBuilder);
	}
}
