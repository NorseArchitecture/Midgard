using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

/// <summary>
///     Drives MVC's real formatter-selection algorithm (<see cref="DefaultOutputFormatterSelector" />) with the
///     full registered formatter list a host actually carries — <see cref="ProblemXmlOutputFormatter" />,
///     <see cref="XmlContractOutputFormatter" />, and the default JSON formatter — to prove the negotiation
///     <em>outcome</em> for a <see cref="ProblemDetails" /> value, not merely the precondition
///     (<see cref="ObjectResult.ContentTypes" />) other tests already assert. <see cref="XmlContractOutputFormatter" />
///     carries no shape for <see cref="ProblemDetails" /> and throws if the selector ever hands it one — this
///     is the scenario the content-negotiation fix in <c>GrpcControllerBase.ToProblemResult</c>/
///     <c>InvalidModelStateProblemFactory</c> exists to prevent.
/// </summary>
public sealed class ProblemNegotiationTests
{
	[Fact]
	void An_XML_Accept_header_selects_ProblemXmlOutputFormatter_never_XmlContractOutputFormatter()
	{
		var selected = SelectFormatter(acceptHeader: "application/problem+xml");

		selected.ShouldBeOfType<ProblemXmlOutputFormatter>();
	}

	[Fact]
	void
		A_generic_XML_Accept_header_the_RFC_9457_media_type_never_matches_still_never_selects_XmlContractOutputFormatter()
	{
		// "application/xml" is not a subset match of "application/problem+xml" under standard media-type
		// matching (no RFC 6839 "+xml" suffix-awareness) — an RFC-unaware XML client sending this header
		// falls back to whichever formatter the allowed ContentTypes admit, but it must never be
		// XmlContractOutputFormatter, which carries no shape for ProblemDetails and would throw.
		var selected = SelectFormatter(acceptHeader: "application/xml");

		selected.ShouldNotBeOfType<XmlContractOutputFormatter>();
	}

	[Fact]
	void A_JSON_Accept_header_selects_the_JSON_formatter_never_XmlContractOutputFormatter()
	{
		var selected = SelectFormatter(acceptHeader: "application/problem+json");

		selected.ShouldBeOfType<SystemTextJsonOutputFormatter>();
	}

	[Fact]
	void No_Accept_header_at_all_never_selects_XmlContractOutputFormatter()
	{
		var selected = SelectFormatter(acceptHeader: null);

		selected.ShouldNotBeOfType<XmlContractOutputFormatter>();
	}

	static IOutputFormatter? SelectFormatter(string? acceptHeader)
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.Configure<MvcOptions>(_ => { });
		using var provider = services.BuildServiceProvider();

		DefaultOutputFormatterSelector selector = new(
			provider.GetRequiredService<IOptions<MvcOptions>>(),
			NullLoggerFactory.Instance);

		List<IOutputFormatter> formatters =
		[
			new ProblemXmlOutputFormatter(),
			new XmlContractOutputFormatter(new XmlShapeRegistry(), new NorseXmlOptions()),
			new SystemTextJsonOutputFormatter(new JsonSerializerOptions
			{
				TypeInfoResolver = new DefaultJsonTypeInfoResolver()
			})
		];

		ProblemDetails problem = new() { Title = "Conflict", Status = 409 };
		DefaultHttpContext httpContext = new();
		if (acceptHeader is not null)
			httpContext.Request.Headers.Accept = acceptHeader;

		OutputFormatterWriteContext context = new(
			httpContext,
			static (stream, encoding) => new StreamWriter(stream, encoding),
			typeof(ProblemDetails),
			problem);

		MediaTypeCollection contentTypes = [];
		contentTypes.Add("application/problem+json");
		contentTypes.Add("application/problem+xml");

		return selector.SelectFormatter(context, formatters, contentTypes);
	}
}
