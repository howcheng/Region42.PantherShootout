using Microsoft.Extensions.DependencyInjection;
using Region42.PantherShootout.Application.Interfaces;
using Region42.PantherShootout.Application.Services;

namespace Region42.PantherShootout.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddScoped<IPoolPlayStandingsService, PoolPlayStandingsService>();

		return services;
	}
}
