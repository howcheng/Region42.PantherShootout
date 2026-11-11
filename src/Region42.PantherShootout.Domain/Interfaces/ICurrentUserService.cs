namespace Region42.PantherShootout.Domain.Interfaces;

/// <summary>
/// Abstraction over "who is making this change" so the data context can stamp
/// <c>CreatedBy</c> / <c>ModifiedBy</c> without depending on ASP.NET Core's <c>HttpContext</c> directly.
/// </summary>
public interface ICurrentUserService
{
	/// <summary>
	/// The email or display identifier of the signed-in user, or <c>"System"</c> if unauthenticated.
	/// </summary>
	string UserIdentifier { get; }
}
