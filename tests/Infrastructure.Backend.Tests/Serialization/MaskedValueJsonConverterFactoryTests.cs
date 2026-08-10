using System.Text.Json;
using Norse.Infrastructure.Backend.Serialization;
using Norse.Primitives.Pii;

namespace Norse.Infrastructure.Backend.Tests.Serialization;

public sealed class MaskedValueJsonConverterFactoryTests
{
	static readonly JsonSerializerOptions _options = BuildOptions();

	static JsonSerializerOptions BuildOptions()
	{
		JsonSerializerOptions options = new();
		options.Converters.Add(new MaskedValueJsonConverterFactory());
		return options;
	}

	[Fact]
	void Writes_the_masked_rendering_for_any_masked_value_struct() =>
		JsonSerializer.Serialize(new FakePii("buvy@example.com"), _options).ShouldBe("\"***\"");

	[Fact]
	void Refuses_to_deserialize_because_masked_forms_can_be_valid_inputs() =>
		Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<FakePii>("\"***\"", _options));

	[Fact]
	void Leaves_non_masked_types_untouched() =>
		JsonSerializer.Serialize(new { Name = "plain" }, _options).ShouldBe("{\"Name\":\"plain\"}");

	readonly record struct FakePii(string Secret) : IMaskedValue
	{
		public string Masked => "***";
		public string ToMasked(DateOnly asOf) => Masked;
	}
}
