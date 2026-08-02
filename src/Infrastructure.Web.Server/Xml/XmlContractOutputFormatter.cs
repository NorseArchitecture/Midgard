using System.Text;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// Futhark's XML response-body formatter — the write-side mirror of
/// <see cref="XmlContractInputFormatter"/>. A minimal Task 8 shell:
/// <see cref="WriteResponseBodyAsync"/> throws unconditionally until Task 9 wires it against the
/// registered <see cref="XmlShapeRegistry"/> shape.
/// </summary>
sealed class XmlContractOutputFormatter : TextOutputFormatter
{
	public XmlContractOutputFormatter()
	{
		SupportedMediaTypes.Add("application/xml");
		SupportedMediaTypes.Add("text/xml");
		SupportedEncodings.Add(Encoding.UTF8);
	}

	/// <inheritdoc />
	public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding) =>
		throw new NotSupportedException("XmlContractOutputFormatter has no body-writing implementation yet — Task 9 fills this in.");
}
