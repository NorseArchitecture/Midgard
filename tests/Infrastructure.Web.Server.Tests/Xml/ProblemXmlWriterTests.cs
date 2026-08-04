using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Norse.Abstractions.Web.Server.Facade;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

/// <summary>
/// Byte-exact tests for <see cref="ProblemXmlWriter"/> — Futhark's one deliberate exception to its own
/// "everything is an attribute" ethos (spec §11.1): RFC 9457 XML, <c>urn:ietf:rfc:7807</c> namespace,
/// elements not attributes, <c>errors</c> extension rendered as <c>[{path, detail}]</c> array entries
/// via <c>&lt;i&gt;</c> item elements — the identical <see cref="ProblemErrorEntry"/> shape Asgard's
/// <c>GrpcControllerBase</c> and Midgard's <c>ModelState</c>-driven 400 factory both populate.
/// </summary>
public sealed class ProblemXmlWriterTests
{
	[Fact]
	void The_fixture_problem_writes_byte_exact_XML()
	{
		ProblemDetails problem = new()
		{
			Type = "https://example.com/probs/validation",
			Title = "Validation",
			Status = 400,
			Detail = "one or more fields failed validation"
		};
		problem.Extensions["errors"] = new[]
		{
			new ProblemErrorEntry("Policy/@birthDate", "cannot parse 'x' as DateOnly"),
			new ProblemErrorEntry("Policy/Coverage[2]/@limit", "cannot parse 'y' as decimal")
		};

		var xml = WriteToString(problem);

		xml.ShouldBe(
			"""<?xml version="1.0" encoding="utf-8"?><problem xmlns="urn:ietf:rfc:7807"><type>https://example.com/probs/validation</type><title>Validation</title><status>400</status><detail>one or more fields failed validation</detail><errors><i><path>Policy/@birthDate</path><detail>cannot parse 'x' as DateOnly</detail></i><i><path>Policy/Coverage[2]/@limit</path><detail>cannot parse 'y' as decimal</detail></i></errors></problem>""");
	}

	[Fact]
	void Null_members_and_an_absent_errors_extension_are_omitted_entirely()
	{
		ProblemDetails problem = new()
		{
			Title = "Conflict",
			Status = 409
		};

		var xml = WriteToString(problem);

		xml.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><problem xmlns="urn:ietf:rfc:7807"><title>Conflict</title><status>409</status></problem>""");
	}

	[Fact]
	void A_scalar_extension_member_renders_as_its_own_child_element()
	{
		ProblemDetails problem = new() { Title = "Fault", Status = 500 };
		var correlationId = Guid.Parse("0b917371-1111-2222-3333-444455556666");
		problem.Extensions["correlationId"] = correlationId;

		var xml = WriteToString(problem);

		xml.ShouldBe(
			"""<?xml version="1.0" encoding="utf-8"?><problem xmlns="urn:ietf:rfc:7807"><title>Fault</title><status>500</status><correlationId>0b917371-1111-2222-3333-444455556666</correlationId></problem>""");
	}

	[Fact]
	void An_empty_errors_array_writes_an_empty_element()
	{
		ProblemDetails problem = new() { Title = "Validation", Status = 400 };
		problem.Extensions["errors"] = Array.Empty<ProblemErrorEntry>();

		var xml = WriteToString(problem);

		xml.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><problem xmlns="urn:ietf:rfc:7807"><title>Validation</title><status>400</status><errors /></problem>""");
	}

	[Fact]
	void Writes_receipt_and_severed_at_extension_scalars()
	{
		var receiptId = Guid.NewGuid();
		ProblemDetails problem = new()
		{
			Title = "Erased",
			Status = 410,
			Extensions =
			{
				["receipt"] = receiptId,
				["severedAt"] = "2026-08-03T12:00:00.0000000+00:00"
			}
		};
		var xml = WriteToString(problem);
		xml.ShouldContain($"<receipt>{receiptId}</receipt>");
		xml.ShouldContain("<severedAt>2026-08-03T12:00:00.0000000+00:00</severedAt>");
	}

	[Fact]
	void A_null_writer_or_problem_is_refused_loudly()
	{
		using MemoryStream stream = new();
		using var writer = XmlWriter.Create(stream);

		Should.Throw<ArgumentNullException>(() => ProblemXmlWriter.Write(null!, new ProblemDetails()));
		Should.Throw<ArgumentNullException>(() => ProblemXmlWriter.Write(writer, null!));
	}

	static string WriteToString(ProblemDetails problem)
	{
		MemoryStream stream = new();
		XmlWriterSettings settings = new()
		{
			Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			OmitXmlDeclaration = false,
			Indent = false
		};
		using (var writer = XmlWriter.Create(stream, settings))
			ProblemXmlWriter.Write(writer, problem);

		return Encoding.UTF8.GetString(stream.ToArray());
	}
}
