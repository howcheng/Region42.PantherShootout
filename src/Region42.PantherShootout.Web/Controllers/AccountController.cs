using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Region42.PantherShootout.Web.Controllers;

public class AccountController : Controller
{
	/// <summary>
	/// Initiates the Google OAuth login flow and redirects back to a validated local return URL.
	/// </summary>
	[AllowAnonymous]
	public IActionResult Login(string? returnUrl = null)
	{
		var redirectUri = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
			? returnUrl
			: Url.Action("Index", "Home");

		var properties = new AuthenticationProperties
		{
			RedirectUri = redirectUri
		};

		return Challenge(properties, GoogleDefaults.AuthenticationScheme);
	}

	/// <summary>
	/// Signs out the user and clears authentication cookies.
	/// </summary>
	[Authorize]
	public async Task<IActionResult> Logout()
	{
		await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
		return RedirectToAction("Index", "Home");
	}

	/// <summary>
	/// Displays the 403 Access Denied page when an authenticated user lacks permission.
	/// </summary>
	[AllowAnonymous]
	public IActionResult AccessDenied()
	{
		Response.StatusCode = StatusCodes.Status403Forbidden;
		return View();
	}
}
