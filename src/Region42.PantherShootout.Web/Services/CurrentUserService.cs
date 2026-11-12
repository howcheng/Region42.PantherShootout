using System.Security.Claims;
using Region42.PantherShootout.Domain.Interfaces;

namespace Region42.PantherShootout.Web.Services;

/// <summary>
/// Resolves the signed-in user's email or display name from <see cref="IHttpContextAccessor"/>
/// for entity and JSON document audit metadata, falling back to <c>"System"</c> when unauthenticated.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
	public const string SystemUserIdentifier = "System";

	private readonly IHttpContextAccessor _httpContextAccessor;

	public CurrentUserService(IHttpContextAccessor httpContextAccessor)
	{
		ArgumentNullException.ThrowIfNull(httpContextAccessor);

		_httpContextAccessor = httpContextAccessor;
	}

	public string UserIdentifier
	{
		get
		{
			var user = _httpContextAccessor.HttpContext?.User;
			if (user?.Identity?.IsAuthenticated != true)
			{
				return SystemUserIdentifier;
			}

			var email = user.FindFirst(ClaimTypes.Email)?.Value
				?? user.FindFirst("email")?.Value;

			if (!string.IsNullOrWhiteSpace(email))
			{
				return email;
			}

			return !string.IsNullOrWhiteSpace(user.Identity.Name)
				? user.Identity.Name
				: SystemUserIdentifier;
		}
	}
}
