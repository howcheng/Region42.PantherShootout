using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Region42.PantherShootout.Infrastructure.Repositories;

namespace Region42.PantherShootout.Infrastructure;

public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers the <see cref="PantherShootoutDbContext"/> (mapped to the "pso" schema), the
	/// audit interceptor, and <see cref="IRepository{T}"/> as an open generic so any entity type
	/// can be injected as <c>IRepository&lt;SomeEntity&gt;</c> without per-entity registration.
	/// </summary>
	public static IServiceCollection AddPantherShootoutInfrastructure(this IServiceCollection services, string connectionString)
	{
		services.AddScoped<AuditSaveChangesInterceptor>();

		services.AddDbContext<PantherShootoutDbContext>((sp, options) =>
			options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "pso"))
				.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

		services.AddScoped<DbContext>(sp => sp.GetRequiredService<PantherShootoutDbContext>());

		services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
		return services;
	}
}
