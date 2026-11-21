using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Region42.PantherShootout.Web.Controllers;
using Region42.PantherShootout.Web.Models;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.Controllers;

public class AccountControllerTests
{
	[Fact]
	public void Login_WithLocalReturnUrl_ChallengesGoogleWithReturnUrl()
	{
		var controller = CreateAccountController(out var urlHelperMock, out _);
		urlHelperMock.Setup(u => u.IsLocalUrl("/Admin/Import")).Returns(true);

		var result = controller.Login("/Admin/Import");

		var challenge = Assert.IsType<ChallengeResult>(result);
		Assert.Contains(GoogleDefaults.AuthenticationScheme, challenge.AuthenticationSchemes);
		Assert.Equal("/Admin/Import", challenge.Properties?.RedirectUri);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("https://evil.example.com/phish")]
	[InlineData("//evil.example.com")]
	public void Login_WithNullOrExternalReturnUrl_FallsBackToHomeIndex(string? returnUrl)
	{
		var controller = CreateAccountController(out var urlHelperMock, out _);
		urlHelperMock.Setup(u => u.IsLocalUrl(It.IsAny<string?>())).Returns(false);
		urlHelperMock.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/");

		var result = controller.Login(returnUrl);

		var challenge = Assert.IsType<ChallengeResult>(result);
		Assert.Contains(GoogleDefaults.AuthenticationScheme, challenge.AuthenticationSchemes);
		Assert.Equal("/", challenge.Properties?.RedirectUri);
	}

	[Fact]
	public async Task Logout_SignsOutCookieSchemeAndRedirectsToHomeIndex()
	{
		var controller = CreateAccountController(out _, out var authServiceMock);

		var result = await controller.Logout();

		authServiceMock.Verify(
			a => a.SignOutAsync(
				controller.HttpContext,
				CookieAuthenticationDefaults.AuthenticationScheme,
				It.IsAny<AuthenticationProperties?>()),
			Times.Once);

		var redirect = Assert.IsType<RedirectToActionResult>(result);
		Assert.Equal("Index", redirect.ActionName);
		Assert.Equal("Home", redirect.ControllerName);
	}

	[Fact]
	public void AccessDenied_Sets403StatusCodeAndReturnsView()
	{
		var controller = CreateAccountController(out _, out _);

		var result = controller.AccessDenied();

		Assert.IsType<ViewResult>(result);
		Assert.Equal(StatusCodes.Status403Forbidden, controller.Response.StatusCode);
	}

	[Fact]
	public void HomeController_Error_ReturnsErrorViewModelWithRequestId()
	{
		var controller = new HomeController(
			Mock.Of<Region42.PantherShootout.Application.Interfaces.ITournamentYearService>(),
			Mock.Of<Region42.PantherShootout.Domain.Interfaces.ITournamentDataContext>())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-42" }
			}
		};

		var result = controller.Error();

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<ErrorViewModel>(view.Model);
		Assert.Equal("trace-42", model.RequestId);
		Assert.True(model.ShowRequestId);
	}

	private static AccountController CreateAccountController(
		out Mock<IUrlHelper> urlHelperMock,
		out Mock<IAuthenticationService> authServiceMock)
	{
		authServiceMock = new Mock<IAuthenticationService>();
		var services = new ServiceCollection()
			.AddSingleton(authServiceMock.Object)
			.BuildServiceProvider();

		var httpContext = new DefaultHttpContext
		{
			RequestServices = services
		};

		urlHelperMock = new Mock<IUrlHelper>();

		return new AccountController
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = httpContext
			},
			TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>()),
			Url = urlHelperMock.Object
		};
	}
}
