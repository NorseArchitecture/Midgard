using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     Futhark's XML request-body formatter (design spec §8) — the security-critical half of the pair.
///     Every non-negotiable <see cref="XmlReaderSettings" /> value (§8.4) is hardcoded in
///     <see cref="CreateReaderSettings" />, never bindable: <see cref="XmlReaderSettings.DtdProcessing" /> =
///     <see cref="DtdProcessing.Prohibit" /> (kills the four DOCTYPE-rooted attack classes — plain DOCTYPE,
///     internal-entity "billion laughs", external-entity XXE, parameter entities — at the reader's own
///     well-formedness check; confirmed empirically that a DOCTYPE of any shape throws before a single
///     entity is ever expanded), <see cref="XmlReaderSettings.XmlResolver" /> = <see langword="null" /> (XXE
///     dead by construction — redundant-but-required defense-in-depth alongside DTD prohibition),
///     <see cref="XmlReaderSettings.MaxCharactersFromEntities" /> = 0 (defense-in-depth should DTD processing
///     ever be loosened — it does no work today because DTD prohibition already forecloses every entity
///     declaration), a hand-tracked max-depth guard of 32 (there is no depth knob on
///     <see cref="XmlReaderSettings" /> itself; <see cref="XmlReader.Depth" /> is walked and asserted
///     manually in <see cref="ValidateDocument" />), <c>IgnoreComments</c>/<c>IgnoreWhitespace</c> =
///     <see langword="true" />, and <c>IgnoreProcessingInstructions</c> = <see langword="false" /> —
///     deliberately: the reader does <b>not</b> auto-skip a PI the way <see cref="XmlReader.MoveToContent" />
///     would, because a PI encountered anywhere is itself session-fatal (spec §8.1) and must be caught, never
///     silently dropped.
/// </summary>
/// <remarks>
///     <para>
///         <b>Two-pass design, empirically justified.</b> The generated <c>Read</c> method (Task 7) is a
///         trusting, accumulating walk — its unrecognized-node fallback is a bare <c>reader.Read()</c> that
///         silently steps over anything that isn't an element/text/CDATA node, including a
///         <see cref="XmlNodeType.ProcessingInstruction" /> buried mid-document, and it tracks no depth at all.
///         Handing the shape a live reader directly would let a PI or a depth bomb slip straight through to
///         generated code — a real, empirically confirmed gap (verified with a throwaway repro before writing
///         this class), not a hypothetical one. This formatter therefore buffers the request body once and
///         reads it <b>twice</b>: <see cref="ValidateDocument" /> walks every single node from the start of the
///         document to <see cref="XmlReader.EOF" /> against the same hardened settings, rejecting every
///         session-fatal condition before the shape ever sees a reader. Only once that full walk completes
///         clean does a second, fresh reader over the identical bytes get handed to
///         <see cref="IXmlShape.ReadObject" />. This is why the security corpus can assert the shape's
///         <c>Read</c>/<c>ReadObject</c> was never invoked for any session-fatal payload — it provably cannot
///         be, by construction, since that call site sits after the validating pass returns.
///     </para>
///     <para>
///         <b>The <see cref="XmlReadContext.HasFailures" /> gate.</b> Task 7's generated <c>Read</c> always
///         constructs and returns an object even when required members are missing, forcing <c>null!</c> into
///         non-nullable properties — this formatter is the first real caller of that contract and checks
///         <see cref="XmlReadContext.HasFailures" /> before ever trusting the returned object; a constructed
///         object is discarded, never surfaced as <see cref="InputFormatterResult.Success" />, whenever any
///         failure — session-fatal or accumulable — was recorded.
///     </para>
/// </remarks>
sealed class XmlContractInputFormatter : TextInputFormatter
{
	/// <summary>
	///     The reader's own <see cref="XmlReader.Depth" /> for the outermost (root) element is 0 — so a
	///     document nested 32 elements deep (root plus 31 further descendants) reaches a maximum observed
	///     depth of 31, and the 33rd nested level reaches depth 32. Rejecting at <c>Depth &gt;= MaxDepth</c>
	///     is therefore the check that actually rejects a 33-deep document while still admitting a 32-deep
	///     one — confirmed empirically against both shapes before this constant was chosen.
	/// </summary>
	const int MaxDepth = 32;

	readonly NorseXmlOptions _options;

	readonly XmlShapeRegistry _registry;

	public XmlContractInputFormatter(XmlShapeRegistry registry, NorseXmlOptions options)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(options);

		_registry = registry;
		_options = options;
		SupportedMediaTypes.Add("application/xml");
		SupportedMediaTypes.Add("text/xml");
		SupportedEncodings.Add(Encoding.UTF8);
	}

	/// <inheritdoc />
	public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context,
		Encoding encoding)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(encoding);

		if (!_registry.TryGet(context.ModelType, out var shape))
			throw new InvalidOperationException(
				$"no XML shape is registered for '{context.ModelType}' — a type outside the exposed closure is not serializable");

		byte[] body;
		using (MemoryStream buffer = new())
		{
			await context.HttpContext.Request.Body.CopyToAsync(buffer, context.HttpContext.RequestAborted)
				.ConfigureAwait(false);
			body = buffer.ToArray();
		}

		// Non-UTF-8 encoding (declared or BOM-signaled) is session-fatal per spec §8.1. Checked against
		// the raw bytes, independent of whatever the Content-Type header claims — a mismatched BOM lies
		// about the charset the header advertised.
		if (HasNonUtf8ByteOrderMark(body))
		{
			context.ModelState.TryAddModelError("$", "request body is not UTF-8 encoded");
			return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
		}

		try
		{
			ValidateDocument(body, encoding);
		}
		catch (XmlException exception)
		{
			context.ModelState.TryAddModelError("$", $"malformed XML — {exception.Message}");
			return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
		}

		XmlReadContext readContext = new();
		object? value;
		using (MemoryStream stream = new(body))
		using (var textReader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false))
		using (var reader = XmlReader.Create(textReader, CreateReaderSettings()))
		{
			try
			{
				await reader.MoveToContentAsync().ConfigureAwait(false);
				readContext.PushElement(reader.LocalName);
				value = shape.ReadObject(reader, _options.CaseStyle, readContext);
				readContext.Pop();
			}
			catch (XmlException exception)
			{
				// Defense-in-depth: ValidateDocument already walked this exact document with identical
				// settings, so this should be unreachable — but a formatter never lets an unexpected
				// XmlException 500 out uncaught when it can fail loudly and structurally instead.
				context.ModelState.TryAddModelError("$", $"malformed XML — {exception.Message}");
				return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
			}
		}

		if (readContext.HasFailures)
		{
			foreach (var failure in readContext.Failures)
				context.ModelState.TryAddModelError(failure.Path, failure.Detail);

			return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
		}

		return await InputFormatterResult.SuccessAsync(value).ConfigureAwait(false);
	}

	/// <summary>
	///     Walks every node in the document, start to <see cref="XmlReader.EOF" />, against the same hardened
	///     settings the real read below will use. The DTD-rooted attack classes and encoding/well-formedness
	///     violations (duplicate attributes included) throw natively from <see cref="XmlReader.Read" />
	///     itself; processing instructions and excess nesting depth do not (confirmed empirically) and are
	///     checked explicitly here, before the shape ever sees a reader.
	/// </summary>
	static void ValidateDocument(byte[] body, Encoding encoding)
	{
		using MemoryStream stream = new(body);
		using var textReader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false);
		using var reader = XmlReader.Create(textReader, CreateReaderSettings());

		var sawRootElement = false;
		while (reader.Read())
		{
			switch (reader.NodeType)
			{
				case XmlNodeType.ProcessingInstruction:
					throw new XmlException("processing instructions are not permitted");
				case XmlNodeType.CDATA:
					throw new XmlException("CDATA sections are not permitted");
				case XmlNodeType.Element:
					sawRootElement = true;
					if (reader.Depth >= MaxDepth)
						throw new XmlException($"maximum document depth of {MaxDepth} exceeded");
					if (HasNamespaceDeclaration(reader))
						throw new XmlException("XML namespaces are not permitted");
					break;
			}
		}

		if (!sawRootElement)
			throw new XmlException("no root element found");
	}

	/// <summary>
	///     True when the current element or any of its attributes carries a resolved namespace URI — covers both a
	///     default <c>xmlns="..."</c> and a prefixed <c>xmlns:p="..."</c>/<c>p:name</c> form; Futhark documents carry no
	///     namespaces, ever (spec §6.6, §8.1).
	/// </summary>
	static bool HasNamespaceDeclaration(XmlReader reader)
	{
		if (!string.IsNullOrEmpty(reader.NamespaceURI))
			return true;

		if (!reader.MoveToFirstAttribute())
			return false;

		try
		{
			do
			{
				if (!string.IsNullOrEmpty(reader.NamespaceURI))
					return true;
			} while (reader.MoveToNextAttribute());

			return false;
		}
		finally
		{
			reader.MoveToElement();
		}
	}

	/// <summary>
	///     UTF-16 (LE/BE) and UTF-32 byte-order marks — the corpus's "declared-or-BOM-signaled non-UTF-8
	///     encoding" case (spec §8.1). A UTF-8 BOM is deliberately left alone: <see cref="XmlReader" />
	///     tolerates a leading UTF-8 BOM character natively (confirmed empirically), so there is nothing to
	///     reject there.
	/// </summary>
	static bool HasNonUtf8ByteOrderMark(ReadOnlySpan<byte> body) =>
		body.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || // UTF-16 BE
		body.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || // UTF-16 LE (also the UTF-32 LE prefix)
		body.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]); // UTF-32 BE

	static XmlReaderSettings CreateReaderSettings() => new()
	{
		DtdProcessing = DtdProcessing.Prohibit,
		XmlResolver = null,
		MaxCharactersFromEntities = 0,
		IgnoreComments = true,
		IgnoreWhitespace = true,
		IgnoreProcessingInstructions = false,
		Async = true
	};
}
