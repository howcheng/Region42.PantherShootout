namespace Region42.PantherShootout.Domain;

/// <summary>
/// Well-known storage write lock names used to coordinate exclusive administrative operations.
/// </summary>
public static class StorageWriteLockNames
{
	public const string CsvScheduleImport = "csv-schedule-import";

	public const string ScheduleImport = "schedule-import";
}
