using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Norse.Infrastructure.Web.Server.Json;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

/// <summary>
/// Builds a bare <see cref="JsonSerializerOptions"/> carrying Futhark's JSON converter family,
/// <see cref="OptInContractModifier"/>, and strictness ratchet, mirroring what
/// <see cref="MvcBuilderExtensions.AddNorseJson"/> wires onto MVC's <c>JsonOptions</c> — including the
/// camelCase naming policy ASP.NET Core's own <c>JsonOptions</c> default carries into every host that
/// calls <c>AddControllers().AddNorseJson()</c> — without spinning up an ASP.NET Core host, since
/// these tests exercise the converters directly against <see cref="JsonSerializer"/>.
/// </summary>
static class NorseJsonTestOptions
{
	public static JsonSerializerOptions Create()
	{
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
		return options;
	}
}
