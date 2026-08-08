using System.Text.Json;
using Norse.Primitives;
using Norse.Primitives.Pii;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

/// <summary>
///     The PII rows of the JSON leg — <c>PiiResultJsonConverter&lt;T&gt;</c> resolved through
///     <c>ResultJsonConverterFactory</c>'s routing (the factory previously claimed these
///     shapes and then failed at converter construction, at runtime). Wire form is the scalar's
///     canonical <c>WireValue</c> string; read is the parse event through <c>T.Parse</c>; a failed or
///     default stamp is illegal to write. Raw input never appears in a <see cref="Failure" /> for PII
///     (the parsers record empty input) — pinned here because the privacy invariant depends on it.
/// </summary>
public sealed class PiiResultJsonConverterTests
{
	static readonly JsonSerializerOptions _options = NorseJsonTestOptions.Create();

	[Fact]
	void Round_trips_a_success_Result_of_EmailAddress_as_a_plain_json_string()
	{
		var json = JsonSerializer.Serialize(EmailAddress.Parse("buvy@example.com"), _options);
		json.ShouldBe("\"buvy@example.com\"");

		var back = JsonSerializer.Deserialize<Result<EmailAddress>>(json, _options);
		back.TryGetValue(out Success<EmailAddress> email).ShouldBeTrue();
		email.Value.WireValue.ShouldBe("buvy@example.com");
	}

	[Fact]
	void Read_restamps_malformed_text_as_a_typed_Failure_carrying_no_raw_input()
	{
		var back = JsonSerializer.Deserialize<Result<EmailAddress>>("\"not-an-email\"", _options);

		back.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBeEmpty();
	}

	[Fact]
	void A_json_null_parses_the_empty_span_to_the_required_missing_failure()
	{
		var back = JsonSerializer.Deserialize<Result<EmailAddress>>("null", _options);

		back.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Empty);
	}

	[Fact]
	void A_nullable_stamp_maps_json_null_to_absent_and_round_trips_present()
	{
		JsonSerializer.Deserialize<Result<EmailAddress>?>("null", _options).ShouldBeNull();

		var back = JsonSerializer.Deserialize<Result<EmailAddress>?>("\"buvy@example.com\"", _options);
		back.ShouldNotBeNull();
		back.Value.TryGetValue(out Success<EmailAddress> email).ShouldBeTrue();
		email.Value.WireValue.ShouldBe("buvy@example.com");

		JsonSerializer.Serialize<Result<EmailAddress>?>(null, _options).ShouldBe("null");
	}

	[Fact]
	void An_object_token_is_skipped_whole_and_captured_as_Malformed()
	{
		var back = JsonSerializer.Deserialize<Result<EmailAddress>>("""{"sneaky":"payload"}""", _options);

		back.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void A_failed_or_default_stamp_is_illegal_to_write()
	{
		Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(EmailAddress.Parse("garbage"), _options))
			.Message.ShouldContain("a failed or default Result<T> is illegal to write");
		Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(default(Result<EmailAddress>), _options))
			.Message.ShouldContain("a failed or default Result<T> is illegal to write");
	}

	[Fact]
	void The_remaining_taxonomy_rows_round_trip_PersonalName_PhoneNumber_BirthDate()
	{
		JsonSerializer.Deserialize<Result<PersonalName>>("\"Brian\"", _options)
			.TryGetValue(out Success<PersonalName> name).ShouldBeTrue();
		name.Value.WireValue.ShouldBe("Brian");

		JsonSerializer.Deserialize<Result<PhoneNumber>>("\"+15125550143\"", _options)
			.TryGetValue(out Success<PhoneNumber> phone).ShouldBeTrue();
		phone.Value.WireValue.ShouldBe("+15125550143");

		JsonSerializer.Deserialize<Result<BirthDate>>("\"1980-01-02\"", _options)
			.TryGetValue(out Success<BirthDate> born).ShouldBeTrue();
		born.Value.WireValue.ShouldBe("1980-01-02");
	}
}
