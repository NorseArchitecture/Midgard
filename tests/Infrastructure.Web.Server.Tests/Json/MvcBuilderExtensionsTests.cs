using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.Web.Server.Json;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

public sealed class MvcBuilderExtensionsTests
{
	[Fact]
	void AddNorseJson_registers_the_converter_factory_lexical_converters_and_unmapped_member_disallow()
	{
		ServiceCollection services = new();
		var builder = services.AddControllers();
		var registry = new EnumNameRegistry();

		builder.AddNorseJson(registry);
		// The seam resolves NorseXmlOptions from DI (the style-once law) — mirroring how a real host's
		// call to AddNorseXml would register it.
		services.AddSingleton(new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });

		using var provider = services.BuildServiceProvider();
		var jsonOptions = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is ResultJsonConverterFactory);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is DateTimeLexicalJsonConverter);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is DateTimeOffsetLexicalJsonConverter);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is TimeOnlyLexicalJsonConverter);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is TimeSpanLexicalJsonConverter);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is EnumLexicalJsonConverterFactory);
		jsonOptions.JsonSerializerOptions.Converters.ShouldContain(converter => converter is ResultEnumJsonConverterFactory);
		jsonOptions.JsonSerializerOptions.UnmappedMemberHandling.ShouldBe(JsonUnmappedMemberHandling.Disallow);
		provider.GetRequiredService<EnumNameRegistry>().ShouldBeSameAs(registry);
	}

	[Fact]
	void AddNorseJson_throws_on_a_null_registry()
	{
		ServiceCollection services = new();
		var builder = services.AddControllers();

		Should.Throw<ArgumentNullException>(() => builder.AddNorseJson(null!));
	}
}
