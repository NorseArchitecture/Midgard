using System.Text.Json;
using System.Text.Json.Serialization;
using Norse.Infrastructure.Web.Server.Json;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

/// <summary>
/// Builds a bare <see cref="JsonSerializerOptions"/> carrying Futhark's JSON converter family and
/// strictness ratchet, mirroring what <see cref="MvcBuilderExtensions.AddNorseJson"/> wires onto MVC's
/// <c>JsonOptions</c> — without spinning up an ASP.NET Core host, since these tests exercise the
/// converters directly against <see cref="JsonSerializer"/>.
/// </summary>
static class NorseJsonTestOptions
{
	public static JsonSerializerOptions Create()
	{
		var options = new JsonSerializerOptions
		{
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
		};
		options.Converters.Add(new ResultJsonConverterFactory());
		options.Converters.Add(new DateTimeLexicalJsonConverter());
		options.Converters.Add(new DateTimeOffsetLexicalJsonConverter());
		options.Converters.Add(new TimeOnlyLexicalJsonConverter());
		options.Converters.Add(new TimeSpanLexicalJsonConverter());
		return options;
	}
}
