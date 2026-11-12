using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using Region42.PantherShootout.Web.Services;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Services;

public class CurrentUserServiceTests
{
	[Fact]
	public void UserIdentifier_AuthenticatedWithEmailClaim_ReturnsEmail()
	{
		var httpContext = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity(
			[
				new Claim(ClaimTypes.Name, "Tournament Director"),
				new Claim(ClaimTypes.Email, "td@aysoregion42.org")
			], "Google"))
		};
		var accessor = CreateAccessor(httpContext);
		var service = new CurrentUserService(accessor);

		Assert.Equal("td@aysoregion42.org", service.UserIdentifier);
	}

	[Fact]
	public void UserIdentifier_AuthenticatedWithRawEmailClaim_ReturnsEmail()
	{
		var httpContext = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity(
			[
				new Claim("email", "scores@aysoregion42.org")
			], "Google"))
		};
		var service = new CurrentUserService(CreateAccessor(httpContext));

		Assert.Equal("scores@aysoregion42.org", service.UserIdentifier);
	}

	[Fact]
	public void UserIdentifier_AuthenticatedWithoutEmailClaim_FallsBackToIdentityName()
	{
		var httpContext = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity(
			[
				new Claim(ClaimTypes.Name, "Region 42 Staff")
			], "Google"))
		};
		var service = new CurrentUserService(CreateAccessor(httpContext));

		Assert.Equal("Region 42 Staff", service.UserIdentifier);
	}

	[Fact]
	public void UserIdentifier_Unauthenticated_ReturnsSystem()
	{
		var httpContext = new DefaultHttpContext();
		var service = new CurrentUserService(CreateAccessor(httpContext));

		Assert.Equal(CurrentUserService.SystemUserIdentifier, service.UserIdentifier);
	}

	[Fact]
	public void UserIdentifier_NullHttpContext_ReturnsSystem()
	{
		var service = new CurrentUserService(CreateAccessor(null));

		Assert.Equal(CurrentUserService.SystemUserIdentifier, service.UserIdentifier);
	}

	private static IHttpContextAccessor CreateAccessor(HttpContext? context)
	{
		var mock = new Mock<IHttpContextAccessor>();
		mock.SetupGet(a => a.HttpContext).Returns(context);
		return mock.Object;
	}
}
