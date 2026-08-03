using Microsoft.Extensions.DependencyInjection;
using Norse.Abstractions.Backend.Serialization;
using Norse.Infrastructure.Backend.Serialization;

namespace Norse.Infrastructure.Backend.Tests.Serialization;

public sealed class SystemTextJsonSerializerTests
{
	sealed record Payload
	{
		public required string FirstName { get; init; }
		public string? MiddleName { get; init; }
		public required int Age { get; init; }
	}

	static readonly ISerializerProvider _provider = BuildProvider();

	static ISerializerProvider BuildProvider()
	{
		ServiceCollection services = new();
		services.AddNorseSerialization();
		return services.BuildServiceProvider().GetRequiredService<ISerializerProvider>();
	}

	[Theory]
	[InlineData(NamingStrategy.CamelCase, "firstName")]
	[InlineData(NamingStrategy.PascalCase, "FirstName")]
	[InlineData(NamingStrategy.SnakeCase, "first_name")]
	[InlineData(NamingStrategy.KebabCase, "first-name")]
	void Serializes_property_names_per_strategy(NamingStrategy strategy, string expectedName)
	{
		var json = _provider[strategy].Serialize(new Payload { FirstName = "Buvy", Age = 40 });
		json.ShouldContain($"\"{expectedName}\"");
	}

	[Theory]
	[InlineData(NamingStrategy.CamelCase)]
	[InlineData(NamingStrategy.PascalCase)]
	[InlineData(NamingStrategy.SnakeCase)]
	[InlineData(NamingStrategy.KebabCase)]
	void Round_trips_through_string_bytes_and_stream_per_strategy(NamingStrategy strategy)
	{
		var serializer = _provider[strategy];
		Payload original = new() { FirstName = "Buvy", MiddleName = "B", Age = 40 };

		serializer.Deserialize<Payload>(serializer.Serialize(original)).ShouldBe(original);
		serializer.Deserialize<Payload>(serializer.SerializeToUtf8Bytes(original)).ShouldBe(original);

		using MemoryStream stream = new();
		serializer.Serialize(stream, original);
		stream.Position = 0;
		serializer.Deserialize<Payload>(stream).ShouldBe(original);
	}

	[Fact]
	async Task Async_round_trip_works_and_the_contract_defaults_hold()
	{
		var serializer = _provider[NamingStrategy.CamelCase];
		Payload original = new() { FirstName = "Buvy", Age = 40 };

		using MemoryStream stream = new();
		await serializer.SerializeAsync(stream, original, cancellationToken: TestContext.Current.CancellationToken);
		stream.Position = 0;
		(await serializer.DeserializeAsync<Payload>(stream, TestContext.Current.CancellationToken)).ShouldBe(original);

		serializer.ContentType.ShouldBe("application/json");
		serializer.HasAsyncSupport.ShouldBeTrue();
	}

	[Fact]
	void Omits_nulls_by_default_and_writes_them_on_request()
	{
		var serializer = _provider[NamingStrategy.CamelCase];
		Payload payload = new() { FirstName = "Buvy", Age = 40 };
		serializer.Serialize(payload).ShouldNotContain("middleName");
		serializer.Serialize(payload, serializeNulls: true).ShouldContain("\"middleName\":null");
	}

	[Fact]
	void Pretty_print_indents_and_default_is_compact()
	{
		var serializer = _provider[NamingStrategy.CamelCase];
		Payload payload = new() { FirstName = "Buvy", Age = 40 };
		serializer.Serialize(payload).ShouldNotContain("\n");
		serializer.Serialize(payload, prettyPrint: true).ShouldContain("\n");
	}

	[Fact]
	void Dictionary_keys_are_data_and_pass_through_unrewritten()
	{
		// The seam serializes shapes, not data: property names follow the strategy, dictionary
		// KEYS are values and are never case-rewritten (the personal-data download depends on it).
		var json = _provider[NamingStrategy.CamelCase]
			.Serialize(new Dictionary<string, string> { ["Authenticator Key"] = "x" });
		json.ShouldContain("\"Authenticator Key\"");
	}
}
