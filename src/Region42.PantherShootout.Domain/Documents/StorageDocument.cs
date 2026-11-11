namespace Region42.PantherShootout.Domain.Documents;

/// <summary>
/// Wrapper for a JSON document loaded from storage, carrying optimistic concurrency metadata.
/// </summary>
public class StorageDocument<T>
{
	public T Content { get; set; } = default!;

	public long? Generation { get; set; }

	public string ObjectPath { get; set; } = string.Empty;
}
