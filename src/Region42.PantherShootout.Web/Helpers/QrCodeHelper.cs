using Microsoft.AspNetCore.Http.Extensions;
using QRCoder;

namespace Region42.PantherShootout.Web.Helpers;

/// <summary>
/// Builds dynamic absolute public URLs from the incoming <see cref="HttpRequest"/> and generates
/// cross-platform, CSP-safe <c>data:image/png;base64,...</c> QR code images via <see cref="PngByteQRCode"/>.
/// </summary>
public static class QrCodeHelper
{
	public const int DefaultPixelsPerModule = 10;

	public static string BuildAbsoluteUrl(HttpRequest request, string relativePath)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

		var normalizedPath = relativePath.StartsWith('/') ? relativePath : $"/{relativePath}";
		var scheme = !string.IsNullOrWhiteSpace(request.Scheme) ? request.Scheme : "https";
		var host = request.Host.HasValue ? request.Host : new HostString("localhost");

		return UriHelper.BuildAbsolute(scheme, host, request.PathBase, new PathString(normalizedPath));
	}

	public static string GeneratePngDataUri(string payload, int pixelsPerModule = DefaultPixelsPerModule)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(payload);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelsPerModule, 0);

		using var qrGenerator = new QRCodeGenerator();
		using var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
		using var pngQrCode = new PngByteQRCode(qrData);
		var pngBytes = pngQrCode.GetGraphic(pixelsPerModule);

		return $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
	}

	public static string FormatDisplayUrl(string absoluteUrl)
	{
		if (string.IsNullOrWhiteSpace(absoluteUrl))
		{
			return string.Empty;
		}

		var display = absoluteUrl.Trim();
		if (display.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			display = display["https://".Length..];
		}
		else if (display.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
		{
			display = display["http://".Length..];
		}

		return display.TrimEnd('/');
	}
}
