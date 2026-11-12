using Microsoft.AspNetCore.Razor.TagHelpers;
using Moq;
using Region42.PantherShootout.Web.Services;
using Region42.PantherShootout.Web.TagHelpers;
using Xunit;

namespace Region42.PantherShootout.Web.Tests.TagHelpers;

public class CspNonceScriptTagHelperTests
{
	[Fact]
	public void Process_StandardScriptTag_AddsNonceAttribute()
	{
		var nonceService = CreateNonceService("test-nonce-123");
		var tagHelper = new CspNonceScriptTagHelper(nonceService);
		var context = CreateContext();
		var output = CreateOutput([new TagHelperAttribute("src", "/js/site.js")]);

		tagHelper.Process(context, output);

		Assert.True(output.Attributes.TryGetAttribute("nonce", out var nonceAttr));
		Assert.Equal("test-nonce-123", nonceAttr.Value);
	}

	[Fact]
	public void Process_PreExistingNonceAttribute_PreservesExplicitNonce()
	{
		var nonceService = CreateNonceService("generated-nonce");
		var tagHelper = new CspNonceScriptTagHelper(nonceService);
		var context = CreateContext();
		var output = CreateOutput([new TagHelperAttribute("nonce", "explicit-nonce")]);

		tagHelper.Process(context, output);

		Assert.True(output.Attributes.TryGetAttribute("nonce", out var nonceAttr));
		Assert.Equal("explicit-nonce", nonceAttr.Value);
	}

	[Fact]
	public void Process_AddNonceFalse_DoesNotAddNonceAttribute()
	{
		var nonceService = CreateNonceService("generated-nonce");
		var tagHelper = new CspNonceScriptTagHelper(nonceService)
		{
			AddNonce = false
		};
		var context = CreateContext();
		var output = CreateOutput([]);

		tagHelper.Process(context, output);

		Assert.False(output.Attributes.ContainsName("nonce"));
	}

	private static ICspNonceService CreateNonceService(string nonce)
	{
		var mock = new Mock<ICspNonceService>();
		mock.Setup(s => s.GetNonce()).Returns(nonce);
		return mock.Object;
	}

	private static TagHelperContext CreateContext()
	{
		return new TagHelperContext(
			tagName: "script",
			allAttributes: new TagHelperAttributeList(),
			items: new Dictionary<object, object>(),
			uniqueId: Guid.NewGuid().ToString("N"));
	}

	private static TagHelperOutput CreateOutput(IReadOnlyList<TagHelperAttribute> attributes)
	{
		return new TagHelperOutput(
			"script",
			new TagHelperAttributeList(attributes),
			(_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
	}
}
