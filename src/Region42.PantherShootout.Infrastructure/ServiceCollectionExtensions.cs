using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Region42.PantherShootout.Infrastructure;

public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers JSON tournament storage services (local filesystem or Google Cloud Storage),
	/// the scoped <c>ITournamentDataContext</c>, and open-generic <c>IRepository&lt;T&gt;</c>.
	/// Full implementation will be added in Step 2 of the build plan.
	/// </summary>
	public static IServiceCollection AddPantherShootoutInfrastructure(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		return services;
	}
}
