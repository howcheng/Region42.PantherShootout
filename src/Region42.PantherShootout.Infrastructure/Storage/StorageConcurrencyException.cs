namespace Region42.PantherShootout.Infrastructure.Storage;

/// <summary>
/// Thrown when a storage write fails its expected generation precondition because another process
/// modified the underlying JSON object concurrently.
/// </summary>
public class StorageConcurrencyException : Exception
{
	public StorageConcurrencyException(string objectPath)
		: base($"Storage object '{objectPath}' was modified by another process. Please retry.")
	{
		ObjectPath = objectPath;
	}

	public StorageConcurrencyException(string objectPath, Exception innerException)
		: base($"Storage object '{objectPath}' was modified by another process. Please retry.", innerException)
	{
		ObjectPath = objectPath;
	}

	public string ObjectPath { get; }
}
