using System.Text;
using Norse.Infrastructure.Web.Server.Tests.Xml.FormatterFixtures;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

/// <summary>
/// Tests <see cref="XmlContractOutputFormatter"/>'s plumbing — the canonical <c>XmlWriterSettings</c>
/// (declaration on, UTF-8 no BOM, no indent) and the unregistered-type refusal — against a hand-rolled
/// <see cref="WidgetXmlShape"/>, never generated code (matches <c>InputFormatterTests</c>'s own
/// instruction). Task 6's own correction is honored literally: .NET's <see cref="System.Xml.XmlWriter"/>
/// always renders a self-closing element with a space before <c>/&gt;</c> — the exact-string assertion
/// below reflects that, not the design doc's original (now-corrected) no-space examples.
/// </summary>
public sealed class OutputFormatterTests
{
	[Fact]
	async Task Happy_path_write_emits_the_canonical_XML_exactly()
	{
		var registry = new XmlShapeRegistry();
		registry.Add(new WidgetXmlShape());
		var formatter = new XmlContractOutputFormatter(registry, new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		var (context, responseBody) = FormatterTestSupport.BuildWriteContext(new Widget { Name = "lantern" }, typeof(Widget));

		await formatter.WriteResponseBodyAsync(context, Encoding.UTF8);

		var xml = Encoding.UTF8.GetString(responseBody.ToArray());
		xml.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><widget name="lantern" />""");
	}

	[Fact]
	async Task Output_carries_no_byte_order_mark()
	{
		var registry = new XmlShapeRegistry();
		registry.Add(new WidgetXmlShape());
		var formatter = new XmlContractOutputFormatter(registry, new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		var (context, responseBody) = FormatterTestSupport.BuildWriteContext(new Widget { Name = "x" }, typeof(Widget));

		await formatter.WriteResponseBodyAsync(context, Encoding.UTF8);

		var bytes = responseBody.ToArray();
		bytes[0].ShouldBe((byte)'<'); // a UTF-8 BOM would put 0xEF here instead.
	}

	[Fact]
	async Task An_unregistered_type_is_refused_loudly()
	{
		var registry = new XmlShapeRegistry(); // Widget's shape deliberately never registered.
		var formatter = new XmlContractOutputFormatter(registry, new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		var (context, _) = FormatterTestSupport.BuildWriteContext(new Widget(), typeof(Widget));

		var exception = await Should.ThrowAsync<InvalidOperationException>(() => formatter.WriteResponseBodyAsync(context, Encoding.UTF8));
		exception.Message.ShouldContain("Widget");
	}

	[Fact]
	async Task A_null_response_body_is_refused_loudly()
	{
		var registry = new XmlShapeRegistry();
		var formatter = new XmlContractOutputFormatter(registry, new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		var (context, _) = FormatterTestSupport.BuildWriteContext(null, typeof(Widget));

		await Should.ThrowAsync<InvalidOperationException>(() => formatter.WriteResponseBodyAsync(context, Encoding.UTF8));
	}

	[Fact]
	void Constructor_throws_on_a_null_registry_or_options()
	{
		Should.Throw<ArgumentNullException>(() => new XmlContractOutputFormatter(null!, new NorseXmlOptions()));
		Should.Throw<ArgumentNullException>(() => new XmlContractOutputFormatter(new XmlShapeRegistry(), null!));
	}
}
