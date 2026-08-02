using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

public sealed class XmlLexicalTests
{
	[Theory]
	[InlineData(true, "true")]
	[InlineData(false, "false")]
	void Format_bool_emits_lowercase_literal(bool value, string expected) =>
		XmlLexical.Format(value).ShouldBe(expected);

	[Fact]
	void Format_decimal_emits_invariant_plain_notation() =>
		XmlLexical.Format(1234.56m).ShouldBe("1234.56");

	[Fact]
	void Format_double_emits_shortest_round_trip() =>
		XmlLexical.Format(0.1d).ShouldBe("0.1");

	[Fact]
	void Format_float_emits_shortest_round_trip() =>
		XmlLexical.Format(0.1f).ShouldBe("0.1");

	[Theory]
	[MemberData(nameof(NonFiniteDoubles))]
	void Format_double_throws_on_non_finite(double value)
	{
		var exception = Should.Throw<InvalidOperationException>(() => XmlLexical.Format(value));
		exception.Message.ShouldBe("non-finite values are illegal to write");
	}

	[Theory]
	[MemberData(nameof(NonFiniteFloats))]
	void Format_float_throws_on_non_finite(float value)
	{
		var exception = Should.Throw<InvalidOperationException>(() => XmlLexical.Format(value));
		exception.Message.ShouldBe("non-finite values are illegal to write");
	}

	[Fact]
	void Format_guid_emits_lowercase_d_format() =>
		XmlLexical.Format(new Guid("0B917371-0000-0000-0000-000000000000")).ShouldBe("0b917371-0000-0000-0000-000000000000");

	[Fact]
	void Format_datetime_emits_round_trip_o_format() =>
		XmlLexical.Format(new DateTime(2026, 8, 1, 14, 30, 0, DateTimeKind.Utc)).ShouldBe("2026-08-01T14:30:00.0000000Z");

	[Fact]
	void Format_datetimeoffset_emits_round_trip_o_format() =>
		XmlLexical.Format(new DateTimeOffset(2026, 8, 1, 14, 30, 0, TimeSpan.FromHours(2))).ShouldBe("2026-08-01T14:30:00.0000000+02:00");

	[Fact]
	void Format_dateonly_emits_yyyy_mm_dd() =>
		XmlLexical.Format(new DateOnly(2026, 8, 1)).ShouldBe("2026-08-01");

	[Fact]
	void Format_timeonly_emits_round_trip_o_format() =>
		XmlLexical.Format(new TimeOnly(14, 30, 0)).ShouldBe("14:30:00.0000000");

	[Fact]
	void Format_timespan_emits_iso8601_duration() =>
		XmlLexical.Format(new TimeSpan(1, 2, 3, 4)).ShouldBe("P1DT2H3M4S");

	[Fact]
	void Format_char_emits_single_character() =>
		XmlLexical.Format('A').ShouldBe("A");

	[Fact]
	void Format_char_throws_on_xml_illegal_control_character()
	{
		var exception = Should.Throw<InvalidOperationException>(() => XmlLexical.Format('\u0001'));
		exception.Message.ShouldNotBeNullOrWhiteSpace();
	}

	public static TheoryData<double> NonFiniteDoubles() =>
		[double.NaN, double.PositiveInfinity, double.NegativeInfinity];

	public static TheoryData<float> NonFiniteFloats() =>
		[float.NaN, float.PositiveInfinity, float.NegativeInfinity];
}
