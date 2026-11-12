using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
		var storageOptions = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();

		if (string.Equals(storageOptions.Provider, "Gcs", StringComparison.OrdinalIgnoreCase))
		{
			services.TryAddSingleton(_ => StorageClient.Create());
			services.AddSingleton<ITournamentDataStore, GcsTournamentDataStore>();
			services.AddSingleton<IStorageWriteLock, GcsStorageWriteLock>();
		}
		else
		{
			services.AddSingleton<ITournamentDataStore, LocalFileTournamentDataStore>();
			services.AddSingleton<IStorageWriteLock, LocalFileStorageWriteLock>();
		}

		services.AddSingleton<IScheduleWorkbookReader, OpenXmlScheduleWorkbookReader>();
		services.AddScoped<ITournamentDataContext, TournamentDataContext>();
		services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

		return services;
	}
}
