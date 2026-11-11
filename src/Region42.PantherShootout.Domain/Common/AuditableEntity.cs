namespace Region42.PantherShootout.Domain.Common;

/// <summary>
/// Base class for all entities that need audit tracking (created/modified at and by)
/// plus a simple manually-incremented integer concurrency/row-version value.
/// </summary>
public abstract class AuditableEntity
{
	public int Id { get; set; }

	public DateTime CreatedAt { get; set; }

	public string CreatedBy { get; set; } = string.Empty;

	public DateTime ModifiedAt { get; set; }

	public string ModifiedBy { get; set; } = string.Empty;

	/// <summary>
	/// Simple optimistic concurrency token, manually incremented by 1 on every save.
	/// </summary>
	public int RowVersion { get; set; } = 1;
}
