namespace Region42.PantherShootout.Web.Services;

/// <summary>
/// Provides a per-request cryptographic nonce for Content Security Policy <c>script-src</c> headers
/// and Razor <c>&lt;script&gt;</c> tag helpers.
/// </summary>
public interface ICspNonceService
{
	/// <summary>
	/// Returns the Base64-encoded cryptographic nonce for the current HTTP request,
	/// generating one on first access.
	/// </summary>
	string GetNonce();
}
