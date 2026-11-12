using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Region42.PantherShootout.Web.Authorization;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Authorization;

public class DomainRequirementHandlerTests
{
	private const string AllowedDomain = "aysoregion42.org";

	[Theory]
	[InlineData("director@aysoregion42.org")]
	[InlineData("Director@AYSORegion42.ORG")]
	[InlineData("referee.admin@aysoregion42.org")]
	public async Task HandleAsync_AuthenticatedUserWithAllowedClaimTypesEmail_Succeeds(string email)
	{
		var requirement = new DomainRequirement(AllowedDomain);
		var handler = new DomainRequirementHandler();
		var user = CreatePrincipal(isAuthenticated: true, new Claim(ClaimTypes.Email, email));
		var context = new AuthorizationHandlerContext([requirement], user, resource: null);

		await handler.HandleAsync(context);

		Assert.True(context.HasSucceeded);
	}

	[Fact]
	public async Task HandleAsync_AuthenticatedUserWithRawEmailClaim_Succeeds()
	{
		var requirement = new DomainRequirement(AllowedDomain);
		var handler = new DomainRequirementHandler();
		var user = CreatePrincipal(isAuthenticated: true, new Claim("email", "coach@aysoregion42.org"));
		var context = new AuthorizationHandlerContext([requirement], user, resource: null);

		await handler.HandleAsync(context);

		Assert.True(context.HasSucceeded);
	}

	[Theory]
	[InlineData("someone@gmail.com")]
	[InlineData("spoof@evil-aysoregion42.org")]
	[InlineData("aysoregion42.org")]
	[InlineData("")]
	public async Task HandleAsync_AuthenticatedUserWithDisallowedDomain_DoesNotSucceed(string email)
	{
		var requirement = new DomainRequirement(AllowedDomain);
		var handler = new DomainRequirementHandler();
		var user = CreatePrincipal(isAuthenticated: true, new Claim(ClaimTypes.Email, email));
		var context = new AuthorizationHandlerContext([requirement], user, resource: null);

		await handler.HandleAsync(context);

		Assert.False(context.HasSucceeded);
	}

	[Fact]
	public async Task HandleAsync_UnauthenticatedUserWithAllowedEmail_DoesNotSucceed()
	{
		var requirement = new DomainRequirement(AllowedDomain);
		var handler = new DomainRequirementHandler();
		var user = CreatePrincipal(isAuthenticated: false, new Claim(ClaimTypes.Email, "director@aysoregion42.org"));
		var context = new AuthorizationHandlerContext([requirement], user, resource: null);

		await handler.HandleAsync(context);

		Assert.False(context.HasSucceeded);
	}

	[Fact]
	public async Task HandleAsync_AuthenticatedUserWithoutEmailClaim_DoesNotSucceed()
	{
		var requirement = new DomainRequirement(AllowedDomain);
		var handler = new DomainRequirementHandler();
		var user = CreatePrincipal(isAuthenticated: true, new Claim(ClaimTypes.Name, "Tournament Admin"));
		var context = new AuthorizationHandlerContext([requirement], user, resource: null);

		await handler.HandleAsync(context);

		Assert.False(context.HasSucceeded);
	}

	[Fact]
	public void DomainRequirement_EmptyAllowedDomain_ThrowsArgumentException()
	{
		Assert.ThrowsAny<ArgumentException>(() => new DomainRequirement(" "));
	}

	private static ClaimsPrincipal CreatePrincipal(bool isAuthenticated, params Claim[] claims)
	{
		var identity = isAuthenticated
			? new ClaimsIdentity(claims, authenticationType: "Google")
			: new ClaimsIdentity(claims);
		return new ClaimsPrincipal(identity);
	}
}
