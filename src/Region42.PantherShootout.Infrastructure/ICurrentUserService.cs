namespace Region42.PantherShootout.Infrastructure;

/// <summary>
/// Abstraction over "who is making this change" so <see cref="AuditSaveChangesInterceptor"/> can
/// stamp CreatedBy/ModifiedBy without depending on ASP.NET Core's HttpContext directly.
/// Implemented in the Web layer using the signed-in user's corresponding pso.users row.
/// </summary>
public interface ICurrentUserService
{
	/// <summary>The Id of the current <c>User</c> row, or 0 if unknown (e.g. background/system jobs).</summary>
	int UserId { get; }
}
