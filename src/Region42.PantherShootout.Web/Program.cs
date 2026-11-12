using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Region42.PantherShootout.Application.DependencyInjection;
using Region42.PantherShootout.Domain.Interfaces;
using Region42.PantherShootout.Infrastructure.DependencyInjection;
using Region42.PantherShootout.Web.Authorization;
using Region42.PantherShootout.Web.Middleware;
using Region42.PantherShootout.Web.Services;

var builder = WebApplication.CreateBuilder(args);

var mvcBuilder = builder.Services.AddControllersWithViews();

// Configure forwarded headers to handle HTTPS redirection behind Google Cloud Run's proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
	options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
	options.KnownIPNetworks.Clear();
	options.KnownProxies.Clear();
});

// Enable Razor runtime compilation in Development for faster UI iteration
if (builder.Environment.IsDevelopment())
{
	mvcBuilder.AddRazorRuntimeCompilation();
}

// Configure session and session-backed TempData so schedule import previews never exceed cookie size limits
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
	options.IdleTimeout = TimeSpan.FromMinutes(30);
	options.Cookie.HttpOnly = true;
	options.Cookie.IsEssential = true;
	options.Cookie.SameSite = SameSiteMode.Lax;
	options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddSingleton<ITempDataProvider, SessionStateTempDataProvider>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICspNonceService, CspNonceService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Configure HSTS to use OWASP recommended values (1 year, subdomains, and preload)
builder.Services.AddHsts(options =>
{
	options.Preload = true;
	options.IncludeSubDomains = true;
	options.MaxAge = TimeSpan.FromDays(365);
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

builder.Services.AddAuthentication(options =>
{
	options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
	options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
	options.LoginPath = "/Account/Login";
	options.LogoutPath = "/Account/Logout";
	options.AccessDeniedPath = "/Account/AccessDenied";
})
.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
{
	var clientId = builder.Configuration["Authentication:Google:ClientId"];
	var clientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

	if (!builder.Environment.IsDevelopment() &&
		(string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)))
	{
		throw new InvalidOperationException(
			"Google OAuth ClientId and ClientSecret must be configured in Authentication:Google.");
	}

	options.ClientId = !string.IsNullOrWhiteSpace(clientId)
		? clientId
		: "development-unconfigured-client-id";
	options.ClientSecret = !string.IsNullOrWhiteSpace(clientSecret)
		? clientSecret
		: "development-unconfigured-client-secret";
	options.Scope.Add("email");
	options.SaveTokens = true;
});

builder.Services.AddAuthorizationBuilder()
	.AddPolicy("AdminPolicy", policy =>
	{
		policy.RequireAuthenticatedUser();
		policy.Requirements.Add(new DomainRequirement("aysoregion42.org"));
	});

builder.Services.AddSingleton<IAuthorizationHandler, DomainRequirementHandler>();

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Home/Error");
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseRouting();
app.UseThemeCookie();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllers();

app.MapControllerRoute(
	name: "default",
	pattern: "{controller=Home}/{action=Index}/{id?}")
	.WithStaticAssets();

app.Run();

public partial class Program;
