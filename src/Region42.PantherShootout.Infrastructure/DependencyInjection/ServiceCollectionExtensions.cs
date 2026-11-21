using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Infrastructure.Import;
using Region42.PantherShootout.Infrastructure.Repositories;
using Region42.PantherShootout.Infrastructure.Storage;

namespace Region42.PantherShootout.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers JSON tournament storage services (local filesystem or Google Cloud Storage),
	/// write locks, the scoped <see cref="ITournamentDataContext"/>, open-generic <see cref="IRepository{T}"/>,
	/// and the OpenXML schedule workbook reader.
	/// </summary>
	public static IServiceCollection AddInfrastructure(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
		services.TryAddSingleton(_ => StorageClient.Create());

		services.AddSingleton<LocalFileTournamentDataStore>();
		services.AddSingleton<GcsTournamentDataStore>();
		services.AddSingleton<LocalFileStorageWriteLock>();
		services.AddSingleton<GcsStorageWriteLock>();

		services.AddSingleton<ITournamentDataStore>(sp =>
		{
			var opts = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
			return string.Equals(opts.Provider, "Gcs", StringComparison.OrdinalIgnoreCase)
				? sp.GetRequiredService<GcsTournamentDataStore>()
				: sp.GetRequiredService<LocalFileTournamentDataStore>();
		});

		services.AddSingleton<IStorageWriteLock>(sp =>
		{
			var opts = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
			return string.Equals(opts.Provider, "Gcs", StringComparison.OrdinalIgnoreCase)
				? sp.GetRequiredService<GcsStorageWriteLock>()
				: sp.GetRequiredService<LocalFileStorageWriteLock>();
		});

		services.AddSingleton<IScheduleWorkbookReader, OpenXmlScheduleWorkbookReader>();
		services.AddScoped<ITournamentDataContext, TournamentDataContext>();
		services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

		return services;
	}
}
