using System.Globalization;
using Norse.Primitives;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Norse.Infrastructure.Web.Grpc.Tests;

/// <summary>
/// <see cref="ResultSerializer{T}.Write"/> unwraps a success <see cref="Result{T}"/> to the scalar's
/// own wire form — the union never rides the wire — and throws <see cref="InvalidOperationException"/>
/// only for the two illegal states, failure and default; <see cref="ResultEnumSerializer{TEnum}"/>
/// mirrors it — a defined success unwraps to the enum's own varint, an undefined success or a
/// failure/default both throw. Most
/// read-path fixtures below are still built by serializing a <em>plain</em> field of the same type
/// (never a <see cref="Result{T}"/> one) through a mirror envelope type on the same model, then feeding
/// those bytes into the real <see cref="Result{T}"/>-typed envelope's <c>Deserialize</c> — proving
/// <see cref="ResultSerializer{T}"/>'s <c>Read</c> decodes exactly what a real client's plain field
/// would produce. That now covers <see cref="DateTimeOffset"/> too: <c>DateTimeOffsetSerializer</c>
/// gives the bare type a registered wire law (the §7 "O" string), so its fixtures ride the same
/// plain-field mirror as every other row; <see cref="WireBytes"/>'s hand-built payloads remain only
/// where the wire text itself is the fixture (the malformed-string case and the byte-level
/// wire-form pin). The success-unwrap oracle tests below invert the technique deliberately: they
/// construct a <see cref="Result{T}"/>-populated envelope and serialize it, proving the write side
/// lands on the exact same bytes as the plain-field mirror.
/// </summary>
public sealed class ResultSerializerTests
{
	// A failed or default Result<T> is illegal to write; a success unwraps to the plain scalar
	// instead. One law, one message: the gRPC serializers throw this exact wording, and the JSON
	// converters and generated XML writer align on the same literal within this change series.
	const string IllegalWriteMessage = "a failed or default Result<T> is illegal to write";

	[Fact]
	void Round_trips_a_success_Result_of_DateOnly_and_a_null_optional_Result_of_string()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new PlainResultEnvelope { When = new DateOnly(2026, 8, 1) });

		var back = TestModel.Deserialize<ResultEnvelope>(model, payload);

		back.When.TryGetValue(out Success<DateOnly> when).ShouldBeTrue();
		when.Value.ShouldBe(new DateOnly(2026, 8, 1));
		back.Name.ShouldBeNull();
	}

	[Fact]
	void Round_trips_a_present_optional_Result_of_string()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new PlainResultEnvelope { When = new DateOnly(2026, 8, 1), Name = "Bifrost" });

		var back = TestModel.Deserialize<ResultEnvelope>(model, payload);

		back.Name!.Value.TryGetValue(out Success<string> name).ShouldBeTrue();
		name.Value.ShouldBe("Bifrost");
	}

	[Theory]
	[MemberData(nameof(IllegalWriteStates))]
	void Writing_a_failed_or_default_Result_throws(string label, Result<int> value)
	{
		var exception = Should.Throw<InvalidOperationException>(() =>
			TestModel.Serialize(TestModel.Create(), new IntEnvelope { Value = value }));
		exception.Message.ShouldBe(IllegalWriteMessage, label);
	}

	public static TheoryData<string, Result<int>> IllegalWriteStates() => new()
	{
		{ "failure", new Failure(ParseFailure.Malformed, "x", "Int32") },
		{ "default", default },
	};

	[Fact]
	void Writing_a_success_emits_the_plain_fields_exact_wire_bytes()
	{
		// The success-unwrap law's oracle: Envelope<T>{Success(v)} and PlainEnvelope<T>{v} must be
		// byte-identical — the union never rides the wire (spec §2). One assertion per taxonomy row.
		AssertSuccessWriteMatchesPlain(true);
		AssertSuccessWriteMatchesPlain((byte)200);
		AssertSuccessWriteMatchesPlain((sbyte)-100);
		AssertSuccessWriteMatchesPlain((short)-12345);
		AssertSuccessWriteMatchesPlain((ushort)54321);
		AssertSuccessWriteMatchesPlain(-123456);
		AssertSuccessWriteMatchesPlain(3000000000U);
		AssertSuccessWriteMatchesPlain(-123456789012345L);
		AssertSuccessWriteMatchesPlain(18000000000000000000UL);
		AssertSuccessWriteMatchesPlain(3.14f);
		AssertSuccessWriteMatchesPlain(2.71828182845);
		AssertSuccessWriteMatchesPlain(1234.56m);
		AssertSuccessWriteMatchesPlain('Z');
		AssertSuccessWriteMatchesPlain("hello, Norse!");
		AssertSuccessWriteMatchesPlain(Guid.NewGuid());
		AssertSuccessWriteMatchesPlain(new DateOnly(2026, 8, 2));
		AssertSuccessWriteMatchesPlain(new TimeOnly(23, 59, 59, 999));
		AssertSuccessWriteMatchesPlain(new DateTime(2026, 8, 2, 1, 2, 3, DateTimeKind.Utc));
		AssertSuccessWriteMatchesPlain(new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.FromHours(-5)));
		AssertSuccessWriteMatchesPlain(new TimeSpan(3, 4, 5, 6));
	}

	static void AssertSuccessWriteMatchesPlain<T>(T value) where T : notnull
	{
		var model = TestModel.Create();
		var wrapped = TestModel.Serialize(model, new Envelope<T> { Value = new Success<T>(value) });
		var plain = TestModel.Serialize(model, new PlainEnvelope<T> { Value = value });
		wrapped.ShouldBe(plain, $"Result<{typeof(T).Name}> success wire bytes");
	}

	[Fact]
	void A_success_written_by_the_wrapped_type_round_trips_through_the_wrapped_type()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new Envelope<int> { Value = new Success<int>(42) });
		var back = TestModel.Deserialize<Envelope<int>>(model, payload);
		back.Value.TryGetValue(out Success<int> success).ShouldBeTrue();
		success.Value.ShouldBe(42);
	}

	[Theory]
	[MemberData(nameof(OptionalResultStates))]
	void Writing_any_present_state_of_an_optional_Result_throws(string label, Result<int>? value)
	{
		var model = TestModel.Create();

		var exception = Should.Throw<InvalidOperationException>(() =>
			TestModel.Serialize(model, new OptionalIntEnvelope { Value = value }));

		exception.Message.ShouldBe(IllegalWriteMessage, label);
	}

	public static TheoryData<string, Result<int>?> OptionalResultStates() => new()
	{
		{ "failure", new Failure(ParseFailure.Malformed, "x", "Int32") },
		{ "default", (Result<int>?)default(Result<int>) },
	};

	[Fact]
	void Writing_a_present_optional_success_emits_the_plain_fields_exact_wire_bytes()
	{
		var model = TestModel.Create();

		var wrapped = TestModel.Serialize(model, new ResultEnvelope { When = new DateOnly(2026, 8, 2), Name = new Success<string>("Bifrost") });
		var plain = TestModel.Serialize(model, new PlainResultEnvelope { When = new DateOnly(2026, 8, 2), Name = "Bifrost" });

		wrapped.ShouldBe(plain);
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
	void A_malformed_DateTimeOffset_wire_string_reads_as_a_typed_failure_not_a_throw()
	{
		// DateTimeOffset is the one type in the taxonomy where a malformed value is genuinely
		// representable on the wire — protobuf-net has no native encoding for it at all, so it falls
		// back to a plain string funneled through Parser.ParseRequired<DateTimeOffset>, producing the
		// platform's one typed Failure rather than a thrown exception. Every other type in the taxonomy
		// reads its own native binary encoding directly — there is no invalid byte pattern for, say, a
		// malformed DateOnly; it just decodes to some valid date, per spec §9.3 — so this failure mode
		// is specific to DateTimeOffset alone.
		var payload = WireBytes.StringFields((1, "2026-02-30T00:00:00.0000000+00:00"));

		var back = TestModel.Deserialize<Envelope<DateTimeOffset>>(TestModel.Create(), payload);

		var failure = back.Value.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Result_of_Guid_matches_the_platforms_rfc_9562_wire_law_bit_for_bit()
	{
		// Same known GUID/hex pair IdentifierSerializersTests uses for a naked Guid member —
		// Result<Guid> must land on the identical wire bytes, proving the two independently-implemented
		// conventions (IdentifierSerializers' DataFormat.FixedSize sweep for a naked field, GuidWire for
		// ResultSerializer<Guid>) agree bit-for-bit.
		var knownGuid = new Guid("12345678-9abc-def0-1234-56789abcdef0");
		const string KnownWireHex = "0A10123456789ABCDEF0123456789ABCDEF0";

		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new PlainEnvelope<Guid> { Value = knownGuid });

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

	[Fact]
	void Round_trips_Result_of_an_enum() => AssertRoundTrips(WireStatus.Inactive);

	[Fact]
	void Round_trips_Result_of_a_flags_enum_composite() => AssertRoundTrips(WireAccess.Read | WireAccess.Write);

	[Fact]
	void An_absent_enum_field_deserializes_to_a_default_Result()
	{
		var back = TestModel.Deserialize<Envelope<WireStatus>>(TestModel.Create(), []);

		back.Value.TryGetValue(out Success<WireStatus> _).ShouldBeFalse();
		back.Value.TryGetValue(out Failure _).ShouldBeFalse();
	}

	[Fact]
	void An_absent_optional_enum_field_deserializes_to_null()
	{
		var back = TestModel.Deserialize<OptionalEnumEnvelope>(TestModel.Create(), []);

		back.Value.ShouldBeNull();
	}

	[Fact]
	void An_undefined_enum_value_reads_as_a_typed_failure_not_a_throw()
	{
		// The binary channel's counterpart to the text channels' undefined-enum-name accumulable (spec
		// §6.5/§8.1): a varint carrying no defined member is representable on the wire, so it funnels to
		// the platform's one typed Failure exactly like a malformed DateTimeOffset wire string does.
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new PlainEnvelope<int> { Value = 99 });

		var back = TestModel.Deserialize<Envelope<WireStatus>>(model, payload);

		var failure = back.Value.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe("99");
		failure.ExpectedType.ShouldBe(nameof(WireStatus));
	}

	[Fact]
	void A_flags_enum_value_with_leftover_bits_reads_as_a_typed_failure_not_a_throw()
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new PlainEnvelope<int> { Value = 8 });

		var back = TestModel.Deserialize<Envelope<WireAccess>>(model, payload);

		var failure = back.Value.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Writing_a_success_enum_emits_the_plain_fields_exact_wire_bytes()
	{
		var model = TestModel.Create();
		var wrapped = TestModel.Serialize(model, new Envelope<WireStatus> { Value = new Success<WireStatus>(WireStatus.Inactive) });
		var plain = TestModel.Serialize(model, new PlainEnvelope<WireStatus> { Value = WireStatus.Inactive });
		wrapped.ShouldBe(plain);
	}

	[Fact]
	void Writing_an_undefined_enum_success_throws_the_illegal_write_law()
	{
		var exception = Should.Throw<InvalidOperationException>(() =>
			TestModel.Serialize(TestModel.Create(), new Envelope<WireStatus> { Value = new Success<WireStatus>((WireStatus)99) }));
		exception.Message.ShouldBe($"'{(WireStatus)99}' is an undefined value of '{typeof(WireStatus)}' and is illegal to write.");
	}

	[Fact]
	void Writing_a_failed_enum_Result_still_throws()
	{
		var exception = Should.Throw<InvalidOperationException>(() =>
			TestModel.Serialize(TestModel.Create(), new Envelope<WireStatus> { Value = new Failure(ParseFailure.Malformed, "x", nameof(WireStatus)) }));
		exception.Message.ShouldBe(IllegalWriteMessage);
	}

	[Fact]
	void Round_trips_a_bare_DateTimeOffset_field()
	{
		// The general (non-Result-wrapped) DateTimeOffset wire law — spec §7's row on the response side,
		// where scalars never wrap (§5.4). Before DateTimeOffsetSerializer, a bare [ProtoMember]
		// DateTimeOffset threw "No serializer defined for type: System.DateTimeOffset" on any registered
		// model, and the tri-protocol swoop had to carry a test-local stopgap serializer.
		var value = new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.FromHours(-5));
		var model = TestModel.Create();

		var payload = TestModel.Serialize(model, new PlainEnvelope<DateTimeOffset> { Value = value });
		var back = TestModel.Deserialize<PlainEnvelope<DateTimeOffset>>(model, payload);

		back.Value.ShouldBe(value);
	}

	[Fact]
	void A_bare_DateTimeOffset_write_and_a_Result_read_share_one_wire_form_by_construction()
	{
		// The wire-compatibility guarantee the swoop's mirror-contract technique depends on, structurally
		// guarded here instead of by registration order in a test fixture: the bytes a bare
		// DateTimeOffset field writes are exactly what ResultSerializer<DateTimeOffset>.Read consumes.
		// AssertRoundTrips above already proves it end-to-end; this pins the wire text itself so a future
		// format drift on either side fails a byte-level assertion, not just a parse.
		var value = new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.FromHours(-5));
		var model = TestModel.Create();

		var bare = TestModel.Serialize(model, new PlainEnvelope<DateTimeOffset> { Value = value });
		var handBuilt = WireBytes.StringFields((1, value.ToString("O", CultureInfo.InvariantCulture)));

		bare.ShouldBe(handBuilt);
	}

	/// <summary>
	/// Builds a fixture by serializing <paramref name="value"/> through a <em>plain</em>
	/// <typeparamref name="T"/> field (<see cref="PlainEnvelope{T}"/> — never a <see cref="Result{T}"/>
	/// one, since <see cref="ResultSerializer{T}.Write"/> always throws) on the same model, then proves
	/// <see cref="ResultSerializer{T}.Read"/> decodes those exact bytes back to <paramref name="value"/>
	/// through the <see cref="Result{T}"/>-typed <see cref="Envelope{T}"/>. Valid for every §7 row —
	/// <see cref="DateTimeOffset"/> included, now that <c>DateTimeOffsetSerializer</c> gives the bare
	/// type a registered wire law — and for enums, whose plain fields ride protobuf-net's native varint.
	/// </summary>
	static void AssertRoundTrips<T>(T value) where T : notnull
	{
		var model = TestModel.Create();
		var payload = TestModel.Serialize(model, new PlainEnvelope<T> { Value = value });
		var back = TestModel.Deserialize<Envelope<T>>(model, payload);
		back.Value.TryGetValue(out Success<T> success).ShouldBeTrue();
		success.Value.ShouldBe(value);
	}

	// Mirrors IdentifierSerializersTests.Applies_level_300_semantics_per_member_without_touching_the_model_default:
	// a fresh reference model with DefaultCompatibilityLevel pinned to Level300 is the platform's own
	// yardstick for "what does a naked T field look like." The production model's per-field Level300
	// sweep (IdentifierSerializers.ApplyWireLaw) must land on byte-identical output for a plain T field
	// — the exact same bytes ResultSerializer<T>.Read consumes — or a real external Level300 producer
	// and this platform's own reader would silently disagree despite every self-consistent round-trip
	// test above still passing.
	static void AssertMatchesLevel300<T>(T value) where T : notnull
	{
		var reference = RuntimeTypeModel.Create();
		reference.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
		var expected = TestModel.Serialize(reference, new PlainEnvelope<T> { Value = value });

		var model = TestModel.Create();
		var actual = TestModel.Serialize(model, new PlainEnvelope<T> { Value = value });

		actual.ShouldBe(expected);
	}
}

/// <summary>
/// Hand-constructs protobuf wire bytes field-by-field via the low-level <see cref="ProtoWriter.State"/>
/// API — needed only where the wire text itself is the fixture (a malformed <see cref="DateTimeOffset"/>
/// string, the byte-level wire-form pin); everything else builds via a plain field on the model
/// (<see cref="PlainEnvelope{T}"/>). Verified byte-identical to protobuf-net's own encoding of a plain
/// <c>string</c> field at the same field number (spiked against a reference <see cref="RuntimeTypeModel"/>
/// before this technique was adopted here).
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

/// <summary>The plain-field mirror of <see cref="Envelope{T}"/> — same field number, raw <typeparamref name="T"/> instead of <c>Result&lt;T&gt;</c>, so it can actually serialize (Write always throws on the Result-wrapped side).</summary>
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

/// <summary>The plain-field mirror of <see cref="ResultEnvelope"/> — same field numbers, raw types.</summary>
[ProtoContract]
public sealed class PlainResultEnvelope
{
	[ProtoMember(1)]
	public DateOnly When { get; set; }

	[ProtoMember(2)]
	public string? Name { get; set; }
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
public sealed class OptionalEnumEnvelope
{
	[ProtoMember(1)]
	public Result<WireStatus>? Value { get; set; }
}

/// <summary>§7's enum row on the gRPC leg — explicit values per platform enum convention.</summary>
public enum WireStatus
{
	Active = 1,
	Inactive = 2
}

/// <summary>The flags variant — composite values and leftover-bit rejection both need coverage (spec §6.5).</summary>
[Flags]
public enum WireAccess
{
	None = 0,
	Read = 1,
	Write = 2
}
