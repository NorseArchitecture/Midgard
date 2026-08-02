using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.Web.Server.Json;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

public sealed class MvcBuilderExtensionsTests
{
	[Fact]
	void AddNorseJson_registers_the_converter_factory_lexical_converters_and_unmapped_member_disallow()
	{
		ServiceCollection services = new();
		var builder = services.AddControllers();

		builder.AddNorseJson();

		using var provider = services.BuildServiceProvider();
		var jsonOptions = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is ResultJsonConverterFactory);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is DateTimeLexicalJsonConverter);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is DateTimeOffsetLexicalJsonConverter);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is TimeOnlyLexicalJsonConverter);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is TimeSpanLexicalJsonConverter);
		jsonOptions.JsonSerializerOptions.UnmappedMemberHandling.ShouldBe(JsonUnmappedMemberHandling.Disallow);
	}
}
