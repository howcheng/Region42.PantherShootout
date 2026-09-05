namespace Region42.PantherShootout.Domain.Common;

/// <summary>
/// Base class for all entities that need audit tracking (created/modified at and by)
/// plus a simple manually-incremented integer concurrency/row-version column.
/// </summary>
public abstract class AuditableEntity
{
	public int Id { get; set; }

	public DateTimeOffset CreatedAt { get; set; }

	public int CreatedByUserId { get; set; }

	public DateTimeOffset ModifiedAt { get; set; }

	public int ModifiedByUserId { get; set; }

	/// <summary>
	/// Simple optimistic concurrency token, manually incremented by 1 on every save.
	/// </summary>
	public int RowVersion { get; set; }
}
