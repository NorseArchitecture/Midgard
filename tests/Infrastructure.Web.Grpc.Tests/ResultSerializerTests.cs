using Norse.Primitives;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Tests;

public sealed class ResultSerializerTests
{
	[Fact]
	void Round_trips_a_success_Result_of_DateOnly_and_a_null_optional_Result_of_string()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new ResultEnvelope
		{
			When = new Success<DateOnly>(new DateOnly(2026, 8, 1)),
			Name = null
		});

		var back = TestModel.Deserialize<ResultEnvelope>(model, payload);

		back.When.TryGetValue(out Success<DateOnly> when).ShouldBeTrue();
		when.Value.ShouldBe(new DateOnly(2026, 8, 1));
		back.Name.ShouldBeNull();
	}

	[Fact]
	void Round_trips_a_present_optional_Result_of_string()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new ResultEnvelope
		{
			When = new Success<DateOnly>(new DateOnly(2026, 8, 1)),
			Name = new Success<string>("Bifrost")
		});

		var back = TestModel.Deserialize<ResultEnvelope>(model, payload);

		back.Name!.Value.TryGetValue(out Success<string> name).ShouldBeTrue();
		name.Value.ShouldBe("Bifrost");
	}

	[Fact]
	void Serializing_a_failed_Result_throws()
	{
		var model = TestModel.Create();
		Result<int> failed = new Failure(ParseFailure.Malformed, "x", "Int32");

		Should.Throw<InvalidOperationException>(() =>
			TestModel.Serialize(model, new IntEnvelope { Value = failed }));
	}

	[Fact]
	void Serializing_a_default_Result_throws()
	{
		var model = TestModel.Create();

		Should.Throw<InvalidOperationException>(() =>
			TestModel.Serialize(model, new IntEnvelope { Value = default }));
	}

	[Fact]
	void Serializing_a_failed_optional_Result_throws()
	{
		var model = TestModel.Create();
		Result<int> failed = new Failure(ParseFailure.Malformed, "x", "Int32");

		Should.Throw<InvalidOperationException>(() =>
			TestModel.Serialize(model, new OptionalIntEnvelope { Value = failed }));
	}

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
	void Result_of_Guid_matches_the_platforms_rfc_9562_wire_law_bit_for_bit()
	{
		// Same known GUID/hex pair IdentifierSerializersTests uses for a naked Guid member —
		// Result<Guid> must land on the identical wire bytes, proving the two conventions agree.
		var knownGuid = new Guid("12345678-9abc-def0-1234-56789abcdef0");
		const string KnownWireHex = "0A10123456789ABCDEF0123456789ABCDEF0";

		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new GuidResultEnvelope { Id = new Success<Guid>(knownGuid) });

		Convert.ToHexString(payload).ShouldBe(KnownWireHex);
	}

	[Fact]
	void Result_of_decimal_matches_the_platforms_level_300_wire_law_bit_for_bit() =>
		AssertMatchesLevel300(1234.56m);

	[Fact]
	void Result_of_DateTime_matches_the_platforms_level_300_wire_law_bit_for_bit() =>
		AssertMatchesLevel300(new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc));

	[Fact]
	void Result_of_TimeSpan_matches_the_platforms_level_300_wire_law_bit_for_bit() =>
		AssertMatchesLevel300(new TimeSpan(1, 2, 3, 4));

	[Fact]
	void Result_of_DateOnly_matches_the_platforms_level_300_wire_law_bit_for_bit() =>
		AssertMatchesLevel300(new DateOnly(2026, 8, 1));

	[Fact]
	void Result_of_TimeOnly_matches_the_platforms_level_300_wire_law_bit_for_bit() =>
		AssertMatchesLevel300(new TimeOnly(14, 30, 0));

	[Fact]
	void Round_trips_Result_of_bool() => AssertRoundTrips(true);

	[Fact]
	void Round_trips_Result_of_byte() => AssertRoundTrips((byte)200);

	[Fact]
	void Round_trips_Result_of_sbyte() => AssertRoundTrips((sbyte)-100);

	[Fact]
	void Round_trips_Result_of_short() => AssertRoundTrips((short)-12345);

	[Fact]
	void Round_trips_Result_of_ushort() => AssertRoundTrips((ushort)54321);

	[Fact]
	void Round_trips_Result_of_int() => AssertRoundTrips(-123456);

	[Fact]
	void Round_trips_Result_of_uint() => AssertRoundTrips(3000000000U);

	[Fact]
	void Round_trips_Result_of_long() => AssertRoundTrips(-123456789012345L);

	[Fact]
	void Round_trips_Result_of_ulong() => AssertRoundTrips(18000000000000000000UL);

	[Fact]
	void Round_trips_Result_of_float() => AssertRoundTrips(3.14f);

	[Fact]
	void Round_trips_Result_of_double() => AssertRoundTrips(2.71828182845);

	[Fact]
	void Round_trips_Result_of_decimal() => AssertRoundTrips(1234.56m);

	[Fact]
	void Round_trips_Result_of_char() => AssertRoundTrips('Z');

	[Fact]
	void Round_trips_Result_of_string() => AssertRoundTrips("hello, Norse!");

	[Fact]
	void Round_trips_Result_of_an_empty_string() => AssertRoundTrips("");

	[Fact]
	void Round_trips_Result_of_Guid() => AssertRoundTrips(Guid.NewGuid());

	[Fact]
	void Round_trips_Result_of_DateOnly() => AssertRoundTrips(new DateOnly(2026, 8, 2));

	[Fact]
	void Round_trips_Result_of_TimeOnly() => AssertRoundTrips(new TimeOnly(23, 59, 59, 999));

	[Fact]
	void Round_trips_Result_of_DateTime() => AssertRoundTrips(new DateTime(2026, 8, 2, 1, 2, 3, DateTimeKind.Utc));

	[Fact]
	void Round_trips_Result_of_DateTimeOffset() =>
		AssertRoundTrips(new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.FromHours(-5)));

	[Fact]
	void Round_trips_Result_of_TimeSpan() => AssertRoundTrips(new TimeSpan(3, 4, 5, 6));

	static void AssertRoundTrips<T>(T value) where T : notnull
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new Envelope<T> { Value = new Success<T>(value) });
		var back = TestModel.Deserialize<Envelope<T>>(model, payload);
		back.Value.TryGetValue(out Success<T> success).ShouldBeTrue();
		success.Value.ShouldBe(value);
	}

	// Mirrors IdentifierSerializersTests.Applies_level_300_semantics_per_member_without_touching_the_model_default:
	// a fresh reference model with DefaultCompatibilityLevel pinned to Level300 is the platform's own
	// yardstick for "what does a naked T field look like." Result<T>'s Success-cased wire bytes must be
	// byte-identical to it — a self-consistent round trip against our own custom serializer alone would
	// not catch a regression that broke Level300 cross-model compatibility while still round-tripping
	// against itself.
	static void AssertMatchesLevel300<T>(T value) where T : notnull
	{
		var reference = RuntimeTypeModel.Create();
		reference.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
		var expected = TestModel.Serialize(reference, new PlainEnvelope<T> { Value = value });

		var model = TestModel.Create();
		var actual = TestModel.Serialize(model, new Envelope<T> { Value = new Success<T>(value) });

		actual.ShouldBe(expected);
	}
}

[ProtoContract]
public sealed class Envelope<T> where T : notnull
{
	[ProtoMember(1)]
	public Result<T> Value { get; set; }
}

[ProtoContract]
public sealed class PlainEnvelope<T> where T : notnull
{
	[ProtoMember(1)]
	public T Value { get; set; } = default!;
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

[ProtoContract]
public sealed class GuidResultEnvelope
{
	[ProtoMember(1)]
	public Result<Guid> Id { get; set; }
}
