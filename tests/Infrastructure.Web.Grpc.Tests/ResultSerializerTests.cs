using System.Globalization;
using System.Xml;
using Norse.Primitives;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Tests;

/// <summary>
/// <see cref="Result{T}"/> is a deserialization-only type on the gRPC leg exactly as on the JSON and
/// XML legs: <see cref="ResultSerializer{T}.Write"/> always throws, for every state — success,
/// failure, and default alike — so no test here can manufacture fixture wire bytes by constructing a
/// <see cref="Result{T}"/>-populated envelope and serializing it through the normal model. Every
/// read-path fixture below is instead hand-built at the wire level via <see cref="WireBytes"/>, the
/// same low-level <see cref="ProtoWriter.State"/> technique <c>InputFormatterTests</c>/
/// <c>SecurityCorpusTests</c> already use for hand-authored XML fixtures, adapted to protobuf — write
/// a field header plus a raw string payload directly, bypassing the model (and therefore
/// <see cref="ResultSerializer{T}"/>'s own <c>Write</c>) entirely.
/// </summary>
public sealed class ResultSerializerTests
{
	// Result<T> is a deserialization-only type — Write always throws, for every state, success
	// included. Matches the JSON leg's ResultJsonConverter<T> wording exactly: one platform law, one
	// message, regardless of channel.
	const string DeserializationOnlyMessage = "Result<T> is a deserialization-only type and must never be written";

	[Fact]
	void Round_trips_a_success_Result_of_DateOnly_and_a_null_optional_Result_of_string()
	{
		var payload = WireBytes.StringFields((1, "2026-08-01"));

		var back = TestModel.Deserialize<ResultEnvelope>(TestModel.Create(), payload);

		back.When.TryGetValue(out Success<DateOnly> when).ShouldBeTrue();
		when.Value.ShouldBe(new DateOnly(2026, 8, 1));
		back.Name.ShouldBeNull();
	}

	[Fact]
	void Round_trips_a_present_optional_Result_of_string()
	{
		var payload = WireBytes.StringFields((1, "2026-08-01"), (2, "Bifrost"));

		var back = TestModel.Deserialize<ResultEnvelope>(TestModel.Create(), payload);

		back.Name!.Value.TryGetValue(out Success<string> name).ShouldBeTrue();
		name.Value.ShouldBe("Bifrost");
	}

	[Theory]
	[MemberData(nameof(RequiredResultStates))]
	void Writing_any_state_of_a_required_Result_throws(string label, Result<int> value)
	{
		var model = TestModel.Create();

		var exception = Should.Throw<InvalidOperationException>(() =>
			TestModel.Serialize(model, new IntEnvelope { Value = value }));

		exception.Message.ShouldBe(DeserializationOnlyMessage, label);
	}

	public static TheoryData<string, Result<int>> RequiredResultStates() => new()
	{
		{ "success", new Success<int>(42) },
		{ "failure", new Failure(ParseFailure.Malformed, "x", "Int32") },
		{ "default", default },
	};

	[Theory]
	[MemberData(nameof(OptionalResultStates))]
	void Writing_any_present_state_of_an_optional_Result_throws(string label, Result<int>? value)
	{
		var model = TestModel.Create();

		var exception = Should.Throw<InvalidOperationException>(() =>
			TestModel.Serialize(model, new OptionalIntEnvelope { Value = value }));

		exception.Message.ShouldBe(DeserializationOnlyMessage, label);
	}

	public static TheoryData<string, Result<int>?> OptionalResultStates() => new()
	{
		{ "success", new Success<int>(42) },
		{ "failure", new Failure(ParseFailure.Malformed, "x", "Int32") },
	};

	[Fact]
	void An_absent_field_deserializes_to_a_default_Result()
	{
		var model = TestModel.Create();

		var back = TestModel.Deserialize<IntEnvelope>(model, []);

		back.Value.TryGetValue(out Success<int> _).ShouldBeFalse();
		back.Value.TryGetValue(out Failure _).ShouldBeFalse();
	}

	[Fact]
	void An_absent_optional_field_deserializes_to_null()
	{
		var model = TestModel.Create();

		var back = TestModel.Deserialize<OptionalIntEnvelope>(model, []);

		back.Value.ShouldBeNull();
	}

	[Fact]
	void Registers_idempotently_when_called_twice_on_one_model()
	{
		var model = RuntimeTypeModel.Create();
		Should.NotThrow(() => ResultSerializers.Register(model));
		Should.NotThrow(() => ResultSerializers.Register(model));
	}

	[Fact]
	void A_malformed_wire_string_reads_as_a_typed_failure_not_a_throw()
	{
		// The whole point of the string wire form (spec directive): a value that is structurally
		// unrepresentable as a valid native binary decode — an invalid calendar date — is perfectly
		// representable as a string, and Read funnels it through Parser.ParseRequired<T> exactly like
		// the JSON and XML legs, producing the platform's one typed Failure rather than either a thrown
		// exception or a silently-wrong decoded value.
		var payload = WireBytes.StringFields((1, "2026-02-30"));

		var back = TestModel.Deserialize<Envelope<DateOnly>>(TestModel.Create(), payload);

		var failure = back.Value.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Round_trips_Result_of_bool() => AssertRoundTrips(true, "true");

	[Fact]
	void Round_trips_Result_of_byte() => AssertRoundTrips((byte)200, "200");

	[Fact]
	void Round_trips_Result_of_sbyte() => AssertRoundTrips((sbyte)-100, "-100");

	[Fact]
	void Round_trips_Result_of_short() => AssertRoundTrips((short)-12345, "-12345");

	[Fact]
	void Round_trips_Result_of_ushort() => AssertRoundTrips((ushort)54321, "54321");

	[Fact]
	void Round_trips_Result_of_int() => AssertRoundTrips(-123456, "-123456");

	[Fact]
	void Round_trips_Result_of_uint() => AssertRoundTrips(3000000000U, "3000000000");

	[Fact]
	void Round_trips_Result_of_long() => AssertRoundTrips(-123456789012345L, "-123456789012345");

	[Fact]
	void Round_trips_Result_of_ulong() => AssertRoundTrips(18000000000000000000UL, "18000000000000000000");

	[Fact]
	void Round_trips_Result_of_float() => AssertRoundTrips(3.14f, 3.14f.ToString(CultureInfo.InvariantCulture));

	[Fact]
	void Round_trips_Result_of_double() => AssertRoundTrips(2.71828182845, 2.71828182845.ToString(CultureInfo.InvariantCulture));

	[Fact]
	void Round_trips_Result_of_decimal() => AssertRoundTrips(1234.56m, "1234.56");

	[Fact]
	void Round_trips_Result_of_char() => AssertRoundTrips('Z', "Z");

	[Fact]
	void Round_trips_Result_of_string() => AssertRoundTrips("hello, Norse!", "hello, Norse!");

	[Fact]
	void Round_trips_Result_of_an_empty_string() => AssertRoundTrips("", "");

	[Fact]
	void Round_trips_Result_of_Guid()
	{
		var value = Guid.NewGuid();
		AssertRoundTrips(value, value.ToString("D", CultureInfo.InvariantCulture));
	}

	[Fact]
	void Round_trips_Result_of_DateOnly() => AssertRoundTrips(new DateOnly(2026, 8, 2), "2026-08-02");

	[Fact]
	void Round_trips_Result_of_TimeOnly()
	{
		var value = new TimeOnly(23, 59, 59, 999);
		AssertRoundTrips(value, value.ToString("O", CultureInfo.InvariantCulture));
	}

	[Fact]
	void Round_trips_Result_of_DateTime()
	{
		var value = new DateTime(2026, 8, 2, 1, 2, 3, DateTimeKind.Utc);
		AssertRoundTrips(value, value.ToString("O", CultureInfo.InvariantCulture));
	}

	[Fact]
	void Round_trips_Result_of_DateTimeOffset()
	{
		var value = new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.FromHours(-5));
		AssertRoundTrips(value, value.ToString("O", CultureInfo.InvariantCulture));
	}

	[Fact]
	void Round_trips_Result_of_TimeSpan()
	{
		var value = new TimeSpan(3, 4, 5, 6);
		AssertRoundTrips(value, XmlConvert.ToString(value));
	}

	/// <summary>
	/// Hand-builds a one-field message carrying <paramref name="wireText"/> — the exact §7 lexical
	/// string the JSON and XML legs would also write for <typeparamref name="T"/> — and proves
	/// <see cref="ResultSerializer{T}.Read"/> funnels it through <see cref="Parser.ParseRequired{T}"/>
	/// back to <paramref name="value"/>. This is the read-path replacement for the old byte-exactness
	/// assertions: a plain string field's encoding is unambiguous (UTF-8 bytes, length-prefixed), so
	/// there is no separate "does it match Level300's native binary form" question left to ask — the
	/// only question worth asking is "does the canonical lexical text parse back correctly," which this
	/// proves directly.
	/// </summary>
	static void AssertRoundTrips<T>(T value, string wireText) where T : notnull
	{
		var payload = WireBytes.StringFields((1, wireText));
		var back = TestModel.Deserialize<Envelope<T>>(TestModel.Create(), payload);
		back.Value.TryGetValue(out Success<T> success).ShouldBeTrue();
		success.Value.ShouldBe(value);
	}
}

/// <summary>
/// Hand-constructs protobuf wire bytes field-by-field via the low-level <see cref="ProtoWriter.State"/>
/// API — the technique every read-path fixture in <see cref="ResultSerializerTests"/> uses now that
/// <see cref="ResultSerializer{T}.Write"/> always throws and a <see cref="Result{T}"/>-populated
/// envelope can no longer be serialized through the normal model. Verified byte-identical to
/// protobuf-net's own encoding of a plain <c>string</c> field at the same field number (spiked against
/// a reference <see cref="RuntimeTypeModel"/> before this technique was adopted here).
/// </summary>
static class WireBytes
{
	/// <summary>Writes one or more string fields, in order, into a single message payload.</summary>
	internal static byte[] StringFields(params (int Field, string Value)[] fields)
	{
		using MemoryStream stream = new();
		var state = ProtoWriter.State.Create(stream, RuntimeTypeModel.Create(), null);
		try
		{
			foreach (var (field, value) in fields)
			{
				state.WriteFieldHeader(field, WireType.String);
				state.WriteString(value, null);
			}
		}
		finally
		{
			state.Close();
		}

		return stream.ToArray();
	}
}

[ProtoContract]
public sealed class Envelope<T> where T : notnull
{
	[ProtoMember(1)]
	public Result<T> Value { get; set; }
}

[ProtoContract]
public sealed class ResultEnvelope
{
	[ProtoMember(1)]
	public Result<DateOnly> When { get; set; }

	[ProtoMember(2)]
	public Result<string>? Name { get; set; }
}

[ProtoContract]
public sealed class IntEnvelope
{
	[ProtoMember(1)]
	public Result<int> Value { get; set; }
}

[ProtoContract]
public sealed class OptionalIntEnvelope
{
	[ProtoMember(1)]
	public Result<int>? Value { get; set; }
}
