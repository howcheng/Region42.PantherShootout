using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Web.Services;

/// <summary>
/// Temporary <see cref="ICurrentUserService"/> until Google OAuth sign-in is wired up.
/// Always reports <c>"System"</c>.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
	public string UserIdentifier => "System";
}
