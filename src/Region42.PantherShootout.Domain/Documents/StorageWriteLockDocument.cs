namespace Region42.PantherShootout.Domain.Documents;

/// <summary>
/// JSON document stored for coarse-grained write locking (e.g. during CSV import).
/// </summary>
public class StorageWriteLockDocument
{
	public string Holder { get; set; } = string.Empty;

	public DateTime AcquiredAtUtc { get; set; }

	public DateTime ExpiresAtUtc { get; set; }
}
