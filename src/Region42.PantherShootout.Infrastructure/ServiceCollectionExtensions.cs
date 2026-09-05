using Microsoft.Extensions.DependencyInjection;
using Region42.PantherShootout.Infrastructure.Repositories;

namespace Region42.PantherShootout.Infrastructure;

public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers <see cref="IRepository{T}"/> as an open generic so any entity type can be
	/// injected as <c>IRepository&lt;SomeEntity&gt;</c> without per-entity registration.
	/// </summary>
	public static IServiceCollection AddPantherShootoutRepositories(this IServiceCollection services)
	{
		services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
		return services;
	}
}
