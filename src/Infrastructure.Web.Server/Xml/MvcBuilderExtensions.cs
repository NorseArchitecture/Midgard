using Microsoft.AspNetCore.Mvc;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     Composition-root wiring for Futhark's XML leg: registers <see cref="NorseXmlOptions" /> and
///     the caller-supplied <see cref="XmlShapeRegistry" /> singleton, constructs the
///     <see cref="XmlContractInputFormatter" />/<see cref="XmlContractOutputFormatter" /> pair against that
///     same registry and options and inserts them into MVC's formatter list, registers the
///     <see cref="ProblemXmlOutputFormatter" />/<see cref="InvalidModelStateProblemFactory" /> pair so
///     <c>ModelState</c> 400s and <c>GrpcControllerBase.FoldAsync</c> failures negotiate to
///     <c>application/problem+xml</c>/<c>application/problem+json</c> (spec §11), and registers the
///     library-controller tripwire (<see cref="XmlShapeTripwireStartupFilter" />) — a startup-time,
///     never-runtime, assertion that every facade controller's body-bound parameter and
///     <c>ActionResult&lt;T&gt;</c> payload types carry a generated shape (spec §3, ratified 2026-08-02).
///     The host calls this as <c>AddNorseXml(style, NorseXmlShapeRegistration.Build())</c> — the generated
///     registration function this method's caller supplies, never built here.
/// </summary>
public static class MvcBuilderExtensions
{
	extension(IMvcBuilder builder)
	{
		/// <summary>Wires Futhark's XML composition seam onto <paramref name="builder" />'s MVC pipeline.</summary>
		/// <param name="caseStyle">The wire-name casing convention <see cref="NorseXmlOptions.CaseStyle" /> is set to.</param>
		/// <param name="registry">The generated shape registry — the host passes <c>NorseXmlShapeRegistration.Build()</c>.</param>
		public IMvcBuilder AddNorseXml(XmlCaseStyle caseStyle, XmlShapeRegistry registry)
		{
			ArgumentNullException.ThrowIfNull(registry);

			var xmlOptions = new NorseXmlOptions { CaseStyle = caseStyle };
			builder.Services.AddSingleton(registry);
			builder.Services.AddSingleton(xmlOptions);
			builder.Services.AddSingleton<IStartupFilter, XmlShapeTripwireStartupFilter>();
			builder.Services.Configure<MvcOptions>(options =>
			{
				options.InputFormatters.Insert(0, new XmlContractInputFormatter(registry, xmlOptions));
				options.OutputFormatters.Insert(0, new XmlContractOutputFormatter(registry, xmlOptions));
				options.OutputFormatters.Insert(0, new ProblemXmlOutputFormatter());
				// Required-ness on Futhark contracts is carried by Result<T> presence semantics (spec
				// §8.2) plus the pipeline's ResultRules validation — never by MVC's DataAnnotations
				// layer. Without this switch, [ApiController]'s implicit [Required] on the non-nullable
				// [FromBody] parameter double-fires whenever an input formatter returns Failure (the
				// parameter binds null), layering a "The request field is required" ModelState entry
				// under the parameter's own name on top of the formatter's real accumulated failures.
				options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
			});
			builder.Services.Configure<ApiBehaviorOptions>(options =>
				options.InvalidModelStateResponseFactory = InvalidModelStateProblemFactory.Create);

			return builder;
		}
	}
}
