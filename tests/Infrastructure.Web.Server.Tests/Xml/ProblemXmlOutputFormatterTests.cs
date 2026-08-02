using System.Text;
using Microsoft.AspNetCore.Mvc;
using Norse.Infrastructure.Web.Server.Tests.Xml.FormatterFixtures;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

/// <summary>
/// Tests <see cref="ProblemXmlOutputFormatter"/>'s plumbing — supports only
/// <c>application/problem+xml</c>, delegates to <see cref="ProblemXmlWriter"/> with the same canonical
/// <c>XmlWriterSettings</c> as <see cref="XmlContractOutputFormatter"/>, and refuses a non-
/// <see cref="ProblemDetails"/> value loudly rather than silently mis-writing it.
/// </summary>
public sealed class ProblemXmlOutputFormatterTests
{
	[Fact]
	async Task Writes_the_problem_as_byte_exact_RFC_9457_XML()
	{
		var formatter = new ProblemXmlOutputFormatter();
		ProblemDetails problem = new() { Title = "Conflict", Status = 409 };
		var (context, responseBody) = FormatterTestSupport.BuildWriteContext(problem, typeof(ProblemDetails));

		await formatter.WriteResponseBodyAsync(context, Encoding.UTF8);

		var xml = Encoding.UTF8.GetString(responseBody.ToArray());
		xml.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><problem xmlns="urn:ietf:rfc:7807"><title>Conflict</title><status>409</status></problem>""");
	}

	[Fact]
	async Task Output_carries_no_byte_order_mark()
	{
		var formatter = new ProblemXmlOutputFormatter();
		ProblemDetails problem = new() { Title = "Conflict", Status = 409 };
		var (context, responseBody) = FormatterTestSupport.BuildWriteContext(problem, typeof(ProblemDetails));

		await formatter.WriteResponseBodyAsync(context, Encoding.UTF8);

		var bytes = responseBody.ToArray();
		bytes[0].ShouldBe((byte)'<'); // a UTF-8 BOM would put 0xEF here instead.
	}

	[Fact]
	async Task A_non_ProblemDetails_value_is_refused_loudly()
	{
		var formatter = new ProblemXmlOutputFormatter();
		var (context, _) = FormatterTestSupport.BuildWriteContext(new Widget(), typeof(Widget));

		await Should.ThrowAsync<InvalidOperationException>(() => formatter.WriteResponseBodyAsync(context, Encoding.UTF8));
	}

	[Fact]
	void Supports_only_the_application_problem_plus_xml_media_type()
	{
		var formatter = new ProblemXmlOutputFormatter();
		formatter.SupportedMediaTypes.ShouldBe(["application/problem+xml"]);
	}
}
