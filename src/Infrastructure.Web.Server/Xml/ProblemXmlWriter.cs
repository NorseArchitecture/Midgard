using System.Collections;
using System.Globalization;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Norse.Abstractions.Web.Server.Facade;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// Hand-written emitter for <c>application/problem+xml</c> — RFC 9457's own XML format, not Futhark's
/// (design spec §11.1): elements, not attributes; the <c>urn:ietf:rfc:7807</c> namespace carried forward
/// from RFC 7807; array-shaped extension members as <c>&lt;i&gt;</c> item elements. This is the one
/// deliberate exception to Futhark's "everything is an attribute" ethos — it is not our document, the
/// IETF already decided the one way, and <see cref="ProblemDetails.Extensions"/>'s
/// <c>IDictionary&lt;string, object?&gt;</c> shape has no Futhark shape and never will, so this is a
/// small bespoke emitter either way, not generator territory.
/// </summary>
/// <remarks>
/// <b>Extension member support is deliberately narrow, not a generic object graph writer.</b> The only
/// two shapes this platform's problem responses actually carry today are a scalar (<c>correlationId</c>,
/// a <see cref="Guid"/>) and the <c>errors</c> array (<see cref="ProblemErrorEntry"/> entries, spec
/// §11.1's <c>[{path, detail}]</c> shape — GrpcControllerBase's fold and the <c>ModelState</c>-driven 400
/// factory both populate it with this exact type, so JSON and XML render the identical payload by
/// construction). The <c>Erased</c> 410 fold's receipt extensions (2026-08-03 PII spec §2.4) ship as
/// two scalars — a <see cref="Guid"/> and a pre-formatted round-trip timestamp string — deliberately, so
/// the scalar default renders them and no bespoke case was ever needed.
/// </remarks>
public static class ProblemXmlWriter
{
	const string Namespace = "urn:ietf:rfc:7807";

	/// <summary>Writes <paramref name="problem"/> to <paramref name="writer"/> as RFC 9457 XML.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="problem"/> is null.</exception>
	public static void Write(XmlWriter writer, ProblemDetails problem)
	{
		ArgumentNullException.ThrowIfNull(writer);
		ArgumentNullException.ThrowIfNull(problem);

		writer.WriteStartElement("problem", Namespace);

		WriteScalarIfPresent(writer, "type", problem.Type);
		WriteScalarIfPresent(writer, "title", problem.Title);
		if (problem.Status is { } status)
			WriteScalarIfPresent(writer, "status", status.ToString(CultureInfo.InvariantCulture));
		WriteScalarIfPresent(writer, "detail", problem.Detail);
		WriteScalarIfPresent(writer, "instance", problem.Instance);

		foreach (var (key, value) in problem.Extensions)
			WriteExtension(writer, key, value);

		writer.WriteEndElement();
	}

	static void WriteExtension(XmlWriter writer, string key, object? value)
	{
		switch (value)
		{
			case null:
				return;
			case IEnumerable<ProblemErrorEntry> entries:
				writer.WriteStartElement(key, Namespace);
				foreach (var entry in entries)
				{
					writer.WriteStartElement("i", Namespace);
					WriteScalarIfPresent(writer, "path", entry.Path);
					WriteScalarIfPresent(writer, "detail", entry.Detail);
					writer.WriteEndElement();
				}
				writer.WriteEndElement();
				return;
			case IEnumerable and not string:
				throw new NotSupportedException($"ProblemXmlWriter does not support the extension member '{key}''s collection element type — only IEnumerable<ProblemErrorEntry> is supported today.");
			default:
				WriteScalarIfPresent(writer, key, Convert.ToString(value, CultureInfo.InvariantCulture));
				return;
		}
	}

	static void WriteScalarIfPresent(XmlWriter writer, string name, string? value)
	{
		if (value is null)
			return;

		writer.WriteStartElement(name, Namespace);
		writer.WriteString(value);
		writer.WriteEndElement();
	}
}
