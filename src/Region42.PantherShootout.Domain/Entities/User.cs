using Region42.PantherShootout.Domain.Common;

namespace Region42.PantherShootout.Domain.Entities;

/// <summary>
/// A tournament staff user, tracked for audit (CreatedBy/ModifiedBy) purposes. Authentication is
/// handled via Google OAuth restricted to the @aysoregion42.org email domain; this record is
/// created/updated on first sign-in.
/// </summary>
public class User : AuditableEntity
{
	public required string Email { get; set; }

	public required string DisplayName { get; set; }

	public DateTimeOffset LastLoginAt { get; set; }
}
