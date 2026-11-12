using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Region42.PantherShootout.Web.Authorization;

/// <summary>
/// Authorization requirement that validates the authenticated user's email domain.
/// </summary>
public class DomainRequirement : IAuthorizationRequirement
{
	public DomainRequirement(string allowedDomain)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(allowedDomain);

		AllowedDomain = allowedDomain;
	}

	public string AllowedDomain { get; }
}

/// <summary>
/// Authorization handler that checks whether the authenticated user's email belongs to the allowed domain.
/// </summary>
public class DomainRequirementHandler : AuthorizationHandler<DomainRequirement>
{
	protected override Task HandleRequirementAsync(
		AuthorizationHandlerContext context,
		DomainRequirement requirement)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(requirement);

		if (context.User?.Identity?.IsAuthenticated != true)
		{
			return Task.CompletedTask;
		}

		// Google OAuth populates ClaimTypes.Email or "email".
		var emailClaim = context.User.FindFirst(ClaimTypes.Email)
			?? context.User.FindFirst("email");

		if (emailClaim is null || string.IsNullOrWhiteSpace(emailClaim.Value))
		{
			return Task.CompletedTask;
		}

		if (emailClaim.Value.EndsWith($"@{requirement.AllowedDomain}", StringComparison.OrdinalIgnoreCase))
		{
			context.Succeed(requirement);
		}

		return Task.CompletedTask;
	}
}
