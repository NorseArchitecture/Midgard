using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Norse.Infrastructure.Web.Server.Xml;

#pragma warning disable IDE0130 // Namespace does not match folder structure — deliberate, one level
// deeper than the folder, mirroring the sibling TripwireFixtures.cs convention: keeps these
// fixture-only types out of the ambient Xml test namespace so InputFormatterTests/OutputFormatterTests/
// SecurityCorpusTests import them explicitly rather than tripping over them by proximity.

namespace Norse.Infrastructure.Web.Server.Tests.Xml.FormatterFixtures;

/// <summary>
///     A minimal contract used by the formatter-pair tests (<c>InputFormatterTests</c>,
///     <c>OutputFormatterTests</c>, <c>SecurityCorpusTests</c>). Deliberately hand-rolled, never generated —
///     the brief is explicit that the formatter's own tests must not depend on Task 5-8's generator, so this
///     stands in for what <c>XmlShapeGenerator</c> would otherwise emit.
/// </summary>
public sealed record Widget
{
	public string Name { get; init; } = "";
}

/// <summary>
///     A minimal, hand-rolled <see cref="IXmlShape{T}" /> for <see cref="Widget" />: root element <c>widget</c>, one
///     <c>name</c> attribute. <see cref="Read" /> never throws on a missing attribute — it accumulates a failure into
///     <c>context</c>, exactly like generated code would.
/// </summary>
public sealed class WidgetXmlShape : IXmlShape<Widget>
{
	public Type ContractType =>
		typeof(Widget);

	public string RootName(XmlCaseStyle style) =>
		"widget";

	public void WriteObject(XmlWriter writer, object value, XmlCaseStyle style) =>
		Write(writer, (Widget)value, style);

	public object? ReadObject(XmlReader reader, XmlCaseStyle style, XmlReadContext context) =>
		Read(reader, style, context);

	public void Write(XmlWriter writer, Widget value, XmlCaseStyle style)
	{
		writer.WriteStartElement("widget");
		writer.WriteAttributeString("name", value.Name);
		writer.WriteEndElement();
	}

	public Widget? Read(XmlReader reader, XmlCaseStyle style, XmlReadContext context)
	{
		var name = reader.GetAttribute("name");
		if (string.IsNullOrEmpty(name))
			context.AddFailure(context.PathTo("name"), "required value missing");

		reader.Skip();
		return new Widget { Name = name ?? "" };
	}
}

/// <summary>
///     A shape that mirrors Task 7's generator law on purpose: it always accumulates a failure AND always
///     constructs and returns an object anyway — with an unmistakable sentinel value — so a test can prove
///     the formatter discards that object rather than letting it leak through as a successful read. This is
///     the exact contract Task 7's report flagged as a forward-looking concern for this task.
/// </summary>
public sealed class AlwaysFailsButConstructsWidgetShape : IXmlShape<Widget>
{
	public Type ContractType =>
		typeof(Widget);

	public string RootName(XmlCaseStyle style) =>
		"widget";

	public void WriteObject(XmlWriter writer, object value, XmlCaseStyle style) =>
		throw new NotSupportedException("write-side behavior is not exercised by this fixture.");

	public object? ReadObject(XmlReader reader, XmlCaseStyle style, XmlReadContext context) =>
		Read(reader, style, context);

	public void Write(XmlWriter writer, Widget value, XmlCaseStyle style) =>
		throw new NotSupportedException("write-side behavior is not exercised by this fixture.");

	public Widget? Read(XmlReader reader, XmlCaseStyle style, XmlReadContext context)
	{
		context.AddFailure(context.PathTo("name"), "required value missing");
		reader.Skip();
		return new Widget { Name = "SHOULD-NEVER-LEAK" };
	}
}

/// <summary>
///     A shape that records how many times <see cref="Read" /> was actually invoked — the security corpus's proof
///     that a session-fatal payload never reaches the shape at all, not merely that the formatter happened to return a
///     failure.
/// </summary>
public sealed class SpyWidgetShape : IXmlShape<Widget>
{
	public int ReadInvocations { get; private set; }

	public Type ContractType =>
		typeof(Widget);

	public string RootName(XmlCaseStyle style) =>
		"widget";

	public void WriteObject(XmlWriter writer, object value, XmlCaseStyle style)
	{
	}

	public object? ReadObject(XmlReader reader, XmlCaseStyle style, XmlReadContext context) =>
		Read(reader, style, context);

	public void Write(XmlWriter writer, Widget value, XmlCaseStyle style)
	{
	}

	public Widget? Read(XmlReader reader, XmlCaseStyle style, XmlReadContext context)
	{
		ReadInvocations++;
		return new Widget();
	}
}

/// <summary>
///     Shared plumbing for building the MVC formatter-context types the real ASP.NET Core pipeline would hand a
///     formatter — kept in one place so <c>InputFormatterTests</c>, <c>OutputFormatterTests</c>, and
///     <c>SecurityCorpusTests</c> can't drift from each other on setup mechanics.
/// </summary>
public static class FormatterTestSupport
{
	public static InputFormatterContext BuildReadContext(byte[] body, Type modelType)
	{
		DefaultHttpContext httpContext = new()
		{
			Request = { Body = new MemoryStream(body), ContentType = "application/xml" }
		};

		var metadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType);
		return new InputFormatterContext(
			httpContext,
			"model",
			new ModelStateDictionary(),
			metadata,
			static (stream, encoding) => new StreamReader(stream, encoding));
	}

	public static InputFormatterContext BuildReadContext(string xml, Type modelType) =>
		BuildReadContext(Encoding.UTF8.GetBytes(xml), modelType);

	public static (OutputFormatterWriteContext Context, MemoryStream ResponseBody) BuildWriteContext(object? value,
		Type objectType)
	{
		MemoryStream responseBody = new();
		DefaultHttpContext httpContext = new() { Response = { Body = responseBody } };

		var context = new OutputFormatterWriteContext(
			httpContext,
			static (stream, encoding) => new StreamWriter(stream, encoding),
			objectType,
			value);

		return (context, responseBody);
	}

	/// <summary>
	///     A UTF-16 (little-endian) BOM followed by UTF-16-encoded content — the corpus's "declared-or-BOM-signaled
	///     non-UTF-8 encoding" payload (spec §8.1).
	/// </summary>
	public static byte[] Utf16WithBom(string xml) =>
		[.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(xml)];
}

#pragma warning restore IDE0130
