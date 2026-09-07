using Region42.PantherShootout.Infrastructure;

namespace Region42.PantherShootout.Web.Services;

/// <summary>
/// Temporary no-op <see cref="ICurrentUserService"/> until Google OAuth sign-in is wired up
/// (see plan step for authentication). Always reports UserId 0 (system).
/// </summary>
public class CurrentUserService : ICurrentUserService
{
	public int UserId => 0;
}
