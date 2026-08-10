using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     The <c>application/problem+xml</c> output formatter — the write-side host for
///     <see cref="ProblemXmlWriter" />, registered separately from <see cref="XmlContractOutputFormatter" />
///     because problem responses are not Futhark contract shapes (spec §11.1) and carry a distinct media
///     type the RFC reserves for them. Mirrors <see cref="XmlContractOutputFormatter" />'s canonical
///     <see cref="XmlWriterSettings" /> (XML declaration always emitted, UTF-8 no byte-order mark, no
///     indentation, async) so both formatters write byte-identical plumbing.
/// </summary>
sealed class ProblemXmlOutputFormatter : TextOutputFormatter
{
	public ProblemXmlOutputFormatter()
	{
		SupportedMediaTypes.Add("application/problem+xml");
		SupportedEncodings.Add(Encoding.UTF8);
	}

	/// <inheritdoc />
	protected override bool CanWriteType(Type? type) =>
		type is not null && typeof(ProblemDetails).IsAssignableFrom(type);

	/// <inheritdoc />
	public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
	{
		ArgumentNullException.ThrowIfNull(context);

		var problem = context.Object as ProblemDetails ??
			throw new InvalidOperationException(
				$"ProblemXmlOutputFormatter cannot write a '{context.Object?.GetType().ToString() ?? "null"}' response body — only {nameof(ProblemDetails)} is supported.");

		var writer = XmlWriter.Create(context.HttpContext.Response.Body, CreateWriterSettings());
		await using var writerDisposable = writer.ConfigureAwait(false);

		ProblemXmlWriter.Write(writer, problem);
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
