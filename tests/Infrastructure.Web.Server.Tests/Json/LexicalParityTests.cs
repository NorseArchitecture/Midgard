using System.Text.Json;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

/// <summary>
///     Parity between the JSON lexical converters and <see cref="XmlLexical" /> — the §7 pinned forms must
///     be byte-identical on both text channels, for the types where STJ's built-in defaults disagree.
/// </summary>
public sealed class LexicalParityTests
{
	[Fact]
	void Write_timespan_emits_iso_duration_byte_exact()
	{
		var options = NorseJsonTestOptions.Create();

		JsonSerializer.Serialize(new TimeSpan(1, 2, 3, 4), options).ShouldBe("\"P1DT2H3M4S\"");
	}

	[Fact]
	void Write_datetime_matches_XmlLexical_byte_exact()
	{
		var options = NorseJsonTestOptions.Create();
		var value = new DateTime(2026, 8, 1, 14, 30, 0, DateTimeKind.Utc);

		var json = JsonSerializer.Serialize(value, options);

		json.ShouldBe("\"2026-08-01T14:30:00.0000000Z\"");
		DecodedString(json).ShouldBe(XmlLexical.Format(value));
	}

	[Fact]
	void Write_datetimeoffset_matches_XmlLexical_byte_exact()
	{
		// STJ's default string encoder escapes '+' to + for XSS-safety — a JSON transport
		// concern orthogonal to the pinned lexical *content*, which is what this parity claim is
		// actually about. Decoding strips that escaping the same way any JSON reader would.
		var options = NorseJsonTestOptions.Create();
		var value = new DateTimeOffset(2026, 8, 1, 14, 30, 0, TimeSpan.FromHours(2));

		var json = JsonSerializer.Serialize(value, options);

		DecodedString(json).ShouldBe(XmlLexical.Format(value));
	}

	[Fact]
	void Write_timeonly_matches_XmlLexical_byte_exact()
	{
		var options = NorseJsonTestOptions.Create();
		var value = new TimeOnly(14, 30, 0);

		var json = JsonSerializer.Serialize(value, options);

		json.ShouldBe($"\"{XmlLexical.Format(value)}\"");
	}

	[Fact]
	void Write_timespan_matches_XmlLexical_byte_exact()
	{
		var options = NorseJsonTestOptions.Create();
		var value = new TimeSpan(1, 2, 3, 4);

		var json = JsonSerializer.Serialize(value, options);

		json.ShouldBe($"\"{XmlLexical.Format(value)}\"");
	}

	static string DecodedString(string json) =>
		JsonElement.Parse(json).GetString()!;

	[Fact]
	void Round_trips_datetime_through_the_pinned_form()
	{
		var options = NorseJsonTestOptions.Create();
		var value = new DateTime(2026, 8, 1, 14, 30, 0, DateTimeKind.Utc);

		var json = JsonSerializer.Serialize(value, options);
		var roundTripped = JsonSerializer.Deserialize<DateTime>(json, options);

		roundTripped.ShouldBe(value);
	}

	[Fact]
	void Round_trips_timespan_through_the_pinned_form()
	{
		var options = NorseJsonTestOptions.Create();
		var value = new TimeSpan(1, 2, 3, 4);

		var json = JsonSerializer.Serialize(value, options);
		var roundTripped = JsonSerializer.Deserialize<TimeSpan>(json, options);

		roundTripped.ShouldBe(value);
	}

	[Fact]
	void Read_malformed_timespan_throws_with_domain_worded_message()
	{
		var options = NorseJsonTestOptions.Create();

		var exception =
			Should.Throw<JsonException>(() => JsonSerializer.Deserialize<TimeSpan>("\"not a duration\"", options));
		exception.Message.ShouldContain("not a duration");
	}

	// Full byte-for-byte JSON/XML parity for [Flags] enums lives one level up, in the XML shape
	// generator's own harness (Infrastructure.Web.Server.Generator.Tests/Xml/WriterEmissionTests —
	// CompiledFixture over the identical AccessRights { None=0, Read=1, Write=2, Execute=4,
	// All=Read|Write|Execute } shape) — a different project this test class cannot reach. This fact
	// pins the JSON-side half of the same law (governed names, member-declaration order, composite
	// members never decomposed) so both channels are provably testing the same table/order/decompose
	// rule, even though they can't be asserted against each other directly from here.
	[Fact]
	void Write_flags_array_renders_governed_names_in_declaration_order_matching_the_XML_channels_law()
	{
		var table = new EnumNameTable(
			typeof(AccessRights),
			nameof(AccessRights),
			[
				["none", "None", "none", "NONE", "none"],
				["read", "Read", "read", "READ", "read"],
				["write", "Write", "write", "WRITE", "write"],
				["execute", "Execute", "execute", "EXECUTE", "execute"],
				["all", "All", "all", "ALL", "all"]
			],
			[0, 1, 2, 4, 7]);
		var registry = new EnumNameRegistry();
		registry.Add(table);
		var options = NorseJsonTestOptions.Create(registry);

		JsonSerializer.Serialize(AccessRights.Execute | AccessRights.Read | AccessRights.Write, options)
			.ShouldBe("[\"read\",\"write\",\"execute\"]");
	}

	[Flags]
	enum AccessRights
	{
		None = 0,
		Read = 1,
		Write = 2,
		Execute = 4,
		All = Read | Write | Execute
	}
}
