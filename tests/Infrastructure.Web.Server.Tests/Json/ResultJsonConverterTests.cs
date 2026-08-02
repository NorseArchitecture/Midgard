using System.Text.Json;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

public sealed class ResultJsonConverterTests
{
	[Fact]
	void Read_string_token_funnels_to_parser()
	{
		var options = NorseJsonTestOptions.Create();

		var result = JsonSerializer.Deserialize<Result<DateOnly>>("\"2026-08-01\"", options);

		result.Value.ShouldBeOfType<Success<DateOnly>>().Value.ShouldBe(new DateOnly(2026, 8, 1));
	}

	[Fact]
	void Read_string_token_that_fails_the_parser_captures_a_typed_failure()
	{
		var options = NorseJsonTestOptions.Create();

		var result = JsonSerializer.Deserialize<Result<DateOnly>>("\"it's a beautiful life\"", options);

		var failure = result.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Read_number_token_is_invariant_stringified_into_the_same_funnel()
	{
		var options = NorseJsonTestOptions.Create();

		var result = JsonSerializer.Deserialize<Result<int>>("42", options);

		result.Value.ShouldBeOfType<Success<int>>().Value.ShouldBe(42);
	}

	[Fact]
	void Read_bool_token_is_invariant_stringified_into_the_same_funnel()
	{
		var options = NorseJsonTestOptions.Create();

		var result = JsonSerializer.Deserialize<Result<bool>>("true", options);

		result.Value.ShouldBeOfType<Success<bool>>().Value.ShouldBeTrue();
	}

	[Fact]
	void Read_null_is_required_missing_for_required_and_null_for_optional()
	{
		var options = NorseJsonTestOptions.Create();

		JsonSerializer.Deserialize<Result<int>>("null", options).Value.ShouldBeOfType<Failure>();
		JsonSerializer.Deserialize<Result<int>?>("null", options).ShouldBeNull();
	}

	[Fact]
	void Read_object_token_is_skipped_whole_and_captured_as_a_typed_failure_not_thrown()
	{
		var options = NorseJsonTestOptions.Create();

		var result = JsonSerializer.Deserialize<Result<int>>("""{"unexpected":"shape"}""", options);

		var failure = result.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Read_array_token_is_skipped_whole_and_captured_as_a_typed_failure_not_thrown()
	{
		var options = NorseJsonTestOptions.Create();

		var result = JsonSerializer.Deserialize<Result<int>>("[1,2,3]", options);

		var failure = result.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	// NOTE: spec §8.2/§9.1 states a present-empty string succeeds ("Required Result<string> carrying
	// "" round-trips"). As shipped, Parser.ParseRequired<string>("") returns Failure(Empty) — the
	// generic-fallback path treats any trimmed-empty span as the "required missing" failure before
	// ever reaching string's ISpanParsable.TryParse, with no type-specific carve-out. This converter
	// funnels every present string token through Parser.ParseRequired uniformly (per §9.1: "so every
	// failure message comes from one place"), so it inherits that behavior rather than special-casing
	// string here — a per-converter carve-out would be a second way to fund the same content the
	// ethos (§1.2) rejects. Flagged in the task report as a cross-repo gap for Svartálfheim/Task 0 or
	// a future increment to resolve, not patched here.

	[Fact]
	void Write_success_unwraps_the_clean_value()
	{
		var options = NorseJsonTestOptions.Create();
		Result<int> result = new Success<int>(42);

		JsonSerializer.Serialize(result, options).ShouldBe("42");
	}

	[Fact]
	void Write_success_string_unwraps_the_clean_value()
	{
		var options = NorseJsonTestOptions.Create();
		Result<string> result = new Success<string>("hello");

		JsonSerializer.Serialize(result, options).ShouldBe("\"hello\"");
	}

	[Fact]
	void Write_failed_result_throws()
	{
		var options = NorseJsonTestOptions.Create();
		Result<int> result = new Failure(ParseFailure.Malformed, "nope", nameof(Int32));

		var exception = Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(result, options));
		exception.Message.ShouldBe("a failed Result<T> is illegal to write");
	}

	[Fact]
	void Write_default_result_throws()
	{
		var options = NorseJsonTestOptions.Create();

		var exception = Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(default(Result<int>), options));
		exception.Message.ShouldBe("a failed Result<T> is illegal to write");
	}

	[Fact]
	void Write_null_optional_result_emits_json_null()
	{
		var options = NorseJsonTestOptions.Create();

		JsonSerializer.Serialize((Result<int>?)null, options).ShouldBe("null");
	}

	[Fact]
	void Write_failed_optional_result_throws()
	{
		var options = NorseJsonTestOptions.Create();
		Result<int>? result = new Failure(ParseFailure.Malformed, "nope", nameof(Int32));

		var exception = Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(result, options));
		exception.Message.ShouldBe("a failed Result<T> is illegal to write");
	}

	[Theory]
	[InlineData("true", typeof(bool))]
	[InlineData("42", typeof(int))]
	[InlineData("1234.56", typeof(decimal))]
	[InlineData("\"A\"", typeof(char))]
	[InlineData("\"hello\"", typeof(string))]
	[InlineData("\"0b917371-0000-0000-0000-000000000000\"", typeof(Guid))]
	[InlineData("\"2026-08-01\"", typeof(DateOnly))]
	void Read_covers_the_full_scalar_taxonomy_including_string(string json, Type scalarType)
	{
		var options = NorseJsonTestOptions.Create();
		var resultType = typeof(Result<>).MakeGenericType(scalarType);

		var result = JsonSerializer.Deserialize(json, resultType, options);

		result.ShouldNotBeNull();
		resultType.GetProperty(nameof(Result<>.HasValue))!.GetValue(result).ShouldBe(true);
	}
}
