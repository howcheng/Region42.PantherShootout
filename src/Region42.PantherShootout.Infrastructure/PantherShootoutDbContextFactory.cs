using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Region42.PantherShootout.Infrastructure;

/// <summary>
/// Enables running `dotnet ef migrations add` / `dotnet ef database update` directly against
/// this project without needing the Web project's DI container or a real connection string.
/// </summary>
public class PantherShootoutDbContextFactory : IDesignTimeDbContextFactory<PantherShootoutDbContext>
{
	public PantherShootoutDbContext CreateDbContext(string[] args)
	{
		var optionsBuilder = new DbContextOptionsBuilder<PantherShootoutDbContext>();
		optionsBuilder.UseNpgsql(
			"Host=localhost;Database=panthershootout_design;Username=design;Password=design",
			npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "pso"));

		return new PantherShootoutDbContext(optionsBuilder.Options);
	}
}
