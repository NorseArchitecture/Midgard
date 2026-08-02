using System.Text;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// Futhark's XML request-body formatter. A minimal Task 8 shell — registered by
/// <see cref="MvcBuilderExtensions.AddNorseXml"/> so the composition seam exists and is testable — not
/// yet real: <see cref="ReadRequestBodyAsync"/> throws unconditionally rather than silently
/// half-working. Task 9 fills in the actual body read against the registered
/// <see cref="XmlShapeRegistry"/> shape.
/// </summary>
sealed class XmlContractInputFormatter : TextInputFormatter
{
	public XmlContractInputFormatter()
	{
		SupportedMediaTypes.Add("application/xml");
		SupportedMediaTypes.Add("text/xml");
		SupportedEncodings.Add(Encoding.UTF8);
	}

	/// <inheritdoc />
	public override Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding) =>
		throw new NotSupportedException("XmlContractInputFormatter has no body-reading implementation yet — Task 9 fills this in.");
}
