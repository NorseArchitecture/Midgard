using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     Futhark's XML response-body formatter (design spec §6, §8.4) — the write-side mirror of
///     <see cref="XmlContractInputFormatter" />. The canonical <see cref="XmlWriterSettings" /> are hardcoded
///     in <see cref="CreateWriterSettings" />, never bindable: the XML declaration is always emitted, UTF-8
///     with no byte-order mark, no indentation (output is byte-stable for a given contract/casing/value,
///     spec §6.4), <c>Async</c> = <see langword="true" />. A CLR type with no registered
///     <see cref="IXmlShape" /> is refused loudly with <see cref="InvalidOperationException" /> — never a
///     silent skip or fallback (spec §2): a type outside the exposed closure is not serializable.
/// </summary>
sealed class XmlContractOutputFormatter : TextOutputFormatter
{
	readonly NorseXmlOptions _options;
	readonly XmlShapeRegistry _registry;

	public XmlContractOutputFormatter(XmlShapeRegistry registry, NorseXmlOptions options)
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
	public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
	{
		ArgumentNullException.ThrowIfNull(context);

		var value = context.Object ??
			throw new InvalidOperationException("XmlContractOutputFormatter cannot write a null response body.");

		if (!_registry.TryGet(value.GetType(), out var shape))
			throw new InvalidOperationException(
				$"no XML shape is registered for '{value.GetType()}' — a type outside the exposed closure is not serializable");

		var writer = XmlWriter.Create(context.HttpContext.Response.Body, CreateWriterSettings());
		await using var writerDisposable = writer.ConfigureAwait(false);

		shape.WriteObject(writer, value, _options.CaseStyle);
		await writer.FlushAsync().ConfigureAwait(false);
	}

	static XmlWriterSettings CreateWriterSettings() => new()
	{
		Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
		OmitXmlDeclaration = false,
		Indent = false,
		Async = true
	};
}
