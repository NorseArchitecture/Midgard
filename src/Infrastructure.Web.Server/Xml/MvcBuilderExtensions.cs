using Microsoft.AspNetCore.Mvc;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
/// Composition-root wiring for Futhark's XML leg (Task 8): registers <see cref="NorseXmlOptions"/> and
/// the caller-supplied <see cref="XmlShapeRegistry"/> singleton, inserts the (currently shell)
/// <see cref="XmlContractInputFormatter"/>/<see cref="XmlContractOutputFormatter"/> pair into MVC's
/// formatter list, and registers the library-controller tripwire
/// (<see cref="XmlShapeTripwireStartupFilter"/>) — a startup-time, never-runtime, assertion that every
/// facade controller's body-bound parameter and <c>ActionResult&lt;T&gt;</c> payload types carry a
/// generated shape (spec §3, ratified 2026-08-02). The host calls this as
/// <c>AddNorseXml(style, NorseXmlShapeRegistration.Build())</c> — the generated registration function
/// this method's caller supplies, never built here.
/// </summary>
public static class MvcBuilderExtensions
{
	extension(IMvcBuilder builder)
	{
		/// <summary>Wires Futhark's XML composition seam onto <paramref name="builder"/>'s MVC pipeline.</summary>
		/// <param name="caseStyle">The wire-name casing convention <see cref="NorseXmlOptions.CaseStyle"/> is set to.</param>
		/// <param name="registry">The generated shape registry — the host passes <c>NorseXmlShapeRegistration.Build()</c>.</param>
		public IMvcBuilder AddNorseXml(XmlCaseStyle caseStyle, XmlShapeRegistry registry)
		{
			ArgumentNullException.ThrowIfNull(registry);

			builder.Services.AddSingleton(registry);
			builder.Services.AddSingleton(new NorseXmlOptions { CaseStyle = caseStyle });
			builder.Services.AddSingleton<IStartupFilter, XmlShapeTripwireStartupFilter>();
			builder.Services.Configure<MvcOptions>(options =>
			{
				options.InputFormatters.Insert(0, new XmlContractInputFormatter());
				options.OutputFormatters.Insert(0, new XmlContractOutputFormatter());
			});

			return builder;
		}
	}
}
