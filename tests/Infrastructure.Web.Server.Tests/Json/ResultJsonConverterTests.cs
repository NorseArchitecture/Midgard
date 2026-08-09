using System.Text.Json;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

public sealed class ResultJsonConverterTests
{
	// A failed or default Result<T> is illegal to write; a success unwraps to the plain scalar value
	// instead — the same one-law wording the gRPC serializers and generated XML writer throw, so the
	// message never diverges by channel.
	const string IllegalWriteMessage = "a failed or default Result<T> is illegal to write";

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
	void Read_null_is_required_missing_for_required_string_result()
	{
		// The exact pairing the string-presence fix (commit 27ac9c0) was about: a required,
		// non-nullable Result<string> reading JSON null must still fail as required-missing — the
		// string carve-out only bypasses Parser.ParseRequired<string> for a *present* string token
		// (even an empty one), never for the null branch, which stays routed through
		// Parser.ParseRequired<string>(string.Empty, ...) for every type including string.
		var options = NorseJsonTestOptions.Create();

		var result = JsonSerializer.Deserialize<Result<string>>("null", options);

		var failure = result.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Empty);
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

	[Fact]
	void Read_present_empty_string_succeeds_for_required_string_result()
	{
		// Presence is carried entirely by which JSON token was seen, not by routing content through
		// Parser — a present string token (§7: string's wire form is "verbatim"), empty or not, is
		// content, distinct from the null branch's synthesized required-missing failure.
		var options = NorseJsonTestOptions.Create();

		var result = JsonSerializer.Deserialize<Result<string>>("\"\"", options);

		result.Value.ShouldBeOfType<Success<string>>().Value.ShouldBe(string.Empty);
	}

	[Fact]
	void Read_present_empty_string_succeeds_for_optional_string_result_distinct_from_null()
	{
		var options = NorseJsonTestOptions.Create();

		var presentEmpty = JsonSerializer.Deserialize<Result<string>?>("\"\"", options);
		var absent = JsonSerializer.Deserialize<Result<string>?>("null", options);

		presentEmpty.ShouldNotBeNull();
		presentEmpty.Value.Value.ShouldBeOfType<Success<string>>().Value.ShouldBe(string.Empty);
		absent.ShouldBeNull();
	}

	[Fact]
	void Write_success_emits_the_clean_unwrapped_value()
	{
		var options = NorseJsonTestOptions.Create();
		Result<TimeSpan> result = new Success<TimeSpan>(new TimeSpan(1, 2, 3, 4));

		JsonSerializer.Serialize(result, options).ShouldBe("\"P1DT2H3M4S\"");
	}

	[Fact]
	void Write_success_string_round_trips_through_the_wrapped_type()
	{
		var options = NorseJsonTestOptions.Create();
		Result<string> result = "Bifrost";

		var json = JsonSerializer.Serialize(result, options);

		json.ShouldBe("\"Bifrost\"");
		JsonSerializer.Deserialize<Result<string>>(json, options).Value
			.ShouldBeOfType<Success<string>>().Value.ShouldBe("Bifrost");
	}

	[Fact]
	void Write_failed_result_throws()
	{
		var options = NorseJsonTestOptions.Create();
		Result<int> result = new Failure(ParseFailure.Malformed, "nope", nameof(Int32));

		var exception = Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(result, options));
		exception.Message.ShouldBe(IllegalWriteMessage);
	}

	[Fact]
	void Write_default_result_throws_the_illegal_write_law()
	{
		var options = NorseJsonTestOptions.Create();

		var exception = Should.Throw<InvalidOperationException>(() =>
			JsonSerializer.Serialize(default(Result<int>), options));
		exception.Message.ShouldBe(IllegalWriteMessage);
	}

	[Fact]
	void Write_null_optional_result_emits_json_null()
	{
		// The null (absent-optional) case is orthogonal to writing a Result<T> value — nothing to
		// illegal-write-guard against, since there is no Result<T> here at all, just its absence.
		var options = NorseJsonTestOptions.Create();

		JsonSerializer.Serialize((Result<int>?)null, options).ShouldBe("null");
	}

	[Fact]
	void Write_present_optional_success_emits_the_clean_unwrapped_value()
	{
		var options = NorseJsonTestOptions.Create();
		Result<int>? result = new Success<int>(42);

		JsonSerializer.Serialize(result, options).ShouldBe("42");
	}

	[Fact]
	void Write_failed_optional_result_throws()
	{
		var options = NorseJsonTestOptions.Create();
		Result<int>? result = new Failure(ParseFailure.Malformed, "nope", nameof(Int32));

		var exception = Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(result, options));
		exception.Message.ShouldBe(IllegalWriteMessage);
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
