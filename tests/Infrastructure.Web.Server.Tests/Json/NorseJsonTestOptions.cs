using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Norse.Infrastructure.Web.Server.Json;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

/// <summary>
/// Builds a bare <see cref="JsonSerializerOptions"/> carrying Futhark's JSON converter family,
/// <see cref="OptInContractModifier"/>, and strictness ratchet, mirroring what
/// <c>AddNorseJson</c> wires onto MVC's <c>JsonOptions</c> — including the
/// camelCase naming policy ASP.NET Core's own <c>JsonOptions</c> default carries into every host that
/// calls <c>AddControllers().AddNorseJson()</c> — without spinning up an ASP.NET Core host, since
/// these tests exercise the converters directly against <see cref="JsonSerializer"/>.
/// </summary>
static class NorseJsonTestOptions
{
	/// <param name="registry">The enum name-table registry the two enum factories resolve against. Defaults to an empty registry — existing (non-enum) tests never need a table.</param>
	/// <param name="caseStyle">The active <see cref="XmlCaseStyle"/> the enum factories style names through. Defaults to <see cref="XmlCaseStyle.CamelCase"/>.</param>
	public static JsonSerializerOptions Create(EnumNameRegistry? registry = null, XmlCaseStyle caseStyle = XmlCaseStyle.CamelCase)
	{
		registry ??= new EnumNameRegistry();
		var xmlOptions = new NorseXmlOptions { CaseStyle = caseStyle };
		var options = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
			TypeInfoResolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(OptInContractModifier.Apply)
		};
		options.Converters.Add(new ResultJsonConverterFactory());
		options.Converters.Add(new DateTimeLexicalJsonConverter());
		options.Converters.Add(new DateTimeOffsetLexicalJsonConverter());
		options.Converters.Add(new TimeOnlyLexicalJsonConverter());
		options.Converters.Add(new TimeSpanLexicalJsonConverter());
		options.Converters.Add(new EnumLexicalJsonConverterFactory(registry, xmlOptions));
		options.Converters.Add(new ResultEnumJsonConverterFactory(registry, xmlOptions));
		return options;
	}
}
