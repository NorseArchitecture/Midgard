using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

public sealed class EnumLexicalTests
{
	// Columns follow XmlCaseStyle's declared order: Camel, Pascal, Snake, Upper, Lower.
	static readonly EnumNameTable _table = new(
		typeof(TableStatus),
		nameof(TableStatus),
		[
			["active", "Active", "active", "ACTIVE", "active"],
			["inactive", "Inactive", "inactive", "INACTIVE", "inactive"]
		],
		[1, 2]);

	[Fact]
	void Format_returns_the_name_in_the_requested_style() =>
		EnumLexical.Format(_table, TableStatus.Active, (int)XmlCaseStyle.SnakeCase).ShouldBe("active");

	[Fact]
	void Format_throws_the_exact_undefined_message_for_an_undefined_value()
	{
		var value = (TableStatus)99;
		var exception =
			Should.Throw<InvalidOperationException>(() =>
				EnumLexical.Format(_table, value, (int)XmlCaseStyle.CamelCase));
		exception.Message.ShouldBe($"'{value}' is an undefined value of '{_table.EnumType}' and is illegal to write.");
	}

	[Fact]
	void Parse_exact_match_returns_success()
	{
		var result = EnumLexical.Parse<TableStatus>(_table, "Active", (int)XmlCaseStyle.PascalCase);

		result.TryGetValue(out Success<TableStatus> success).ShouldBeTrue();
		success.Value.ShouldBe(TableStatus.Active);
	}

	[Fact]
	void Parse_wrong_case_returns_malformed_failure()
	{
		var result = EnumLexical.Parse<TableStatus>(_table, "Active", (int)XmlCaseStyle.CamelCase);

		result.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe("Active");
		failure.ExpectedType.ShouldBe(_table.TypeName);
	}

	[Fact]
	void Parse_off_list_returns_malformed_failure()
	{
		var result = EnumLexical.Parse<TableStatus>(_table, "Bogus", (int)XmlCaseStyle.CamelCase);

		result.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe("Bogus");
		failure.ExpectedType.ShouldBe(_table.TypeName);
	}

	[Fact]
	void Parse_empty_content_returns_malformed_failure()
	{
		var result = EnumLexical.Parse<TableStatus>(_table, string.Empty, (int)XmlCaseStyle.CamelCase);

		result.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe(string.Empty);
		failure.ExpectedType.ShouldBe(_table.TypeName);
	}

	[Fact]
	void Registry_tryget_finds_an_added_table()
	{
		var registry = new EnumNameRegistry();
		registry.Add(_table);

		registry.TryGet(typeof(TableStatus), out var found).ShouldBeTrue();
		found.ShouldBeSameAs(_table);
	}

	[Fact]
	void Registry_tryget_misses_an_unregistered_type()
	{
		var registry = new EnumNameRegistry();

		registry.TryGet(typeof(TableStatus), out var found).ShouldBeFalse();
		found.ShouldBeNull();
	}

	[Fact]
	void Registry_add_throws_on_duplicate_enum_type()
	{
		var registry = new EnumNameRegistry();
		registry.Add(_table);

		var duplicate = new EnumNameTable(typeof(TableStatus), nameof(TableStatus), Table_Names(), [1, 2]);
		Should.Throw<InvalidOperationException>(() => registry.Add(duplicate));
	}

	static string[][] Table_Names() =>
	[
		["active", "Active", "active", "ACTIVE", "active"],
		["inactive", "Inactive", "inactive", "INACTIVE", "inactive"]
	];

	enum TableStatus
	{
		Active = 1,
		Inactive = 2
	}

	// Same fixture shape (member names/values) as the XML shape generator's own flags fixture
	// (Infrastructure.Web.Server.Generator.Tests/Xml/{Writer,Reader}EmissionTests.FlagsFixture) — the
	// array/flags twin of EnumLexical.Format/Parse is provably testing the same law on the same table
	// shape as the XML channel, even though the two projects can't assert against each other directly.
	static readonly EnumNameTable _accessRightsTable = new(
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

	static readonly EnumNameTable _archiveModeTable = new(
		typeof(ArchiveMode),
		nameof(ArchiveMode),
		[
			["readWrite", "ReadWrite", "read_write", "READ_WRITE", "readwrite"],
			["append", "Append", "append", "APPEND", "append"]
		],
		[3, 4]);

	[Fact]
	void FormatFlags_decomposes_set_bits_into_declaration_order_names() =>
		EnumLexical.FormatFlags(_accessRightsTable, AccessRights.Read | AccessRights.Write, (int)XmlCaseStyle.CamelCase)
			.ShouldBe(["read", "write"]);

	[Fact]
	void FormatFlags_zero_value_renders_as_an_empty_array() =>
		EnumLexical.FormatFlags(_accessRightsTable, AccessRights.None, (int)XmlCaseStyle.CamelCase).ShouldBeEmpty();

	[Fact]
	void FormatFlags_never_emits_the_composite_member_even_when_every_bit_is_set() =>
		EnumLexical.FormatFlags(_accessRightsTable, AccessRights.All, (int)XmlCaseStyle.CamelCase)
			.ShouldBe(["read", "write", "execute"]);

	[Fact]
	void FormatFlags_throws_on_a_leftover_bit_with_no_single_bit_member()
	{
		var value = (AccessRights)8;
		var exception = Should.Throw<InvalidOperationException>(() =>
			EnumLexical.FormatFlags(_accessRightsTable, value, (int)XmlCaseStyle.CamelCase));

		exception.Message.ShouldBe(
			$"'{value}' carries bits with no single-bit member of '{_accessRightsTable.EnumType}' and is illegal to write.");
	}

	[Fact]
	void FormatFlags_throws_when_the_only_matching_member_is_composite()
	{
		var exception = Should.Throw<InvalidOperationException>(() =>
			EnumLexical.FormatFlags(_archiveModeTable, ArchiveMode.ReadWrite, (int)XmlCaseStyle.CamelCase));

		exception.Message.ShouldBe(
			$"'{ArchiveMode.ReadWrite}' carries bits with no single-bit member of '{_archiveModeTable.EnumType}' and is illegal to write.");
	}

	[Fact]
	void ParseFlags_OR_accumulates_two_tokens()
	{
		var result = EnumLexical.ParseFlags<AccessRights>(_accessRightsTable, ["read", "write"],
			(int)XmlCaseStyle.CamelCase);

		result.TryGetValue(out Success<AccessRights> success).ShouldBeTrue();
		success.Value.ShouldBe(AccessRights.Read | AccessRights.Write);
	}

	[Fact]
	void ParseFlags_empty_token_list_is_the_zero_value()
	{
		var result = EnumLexical.ParseFlags<AccessRights>(_accessRightsTable, [], (int)XmlCaseStyle.CamelCase);

		result.TryGetValue(out Success<AccessRights> success).ShouldBeTrue();
		success.Value.ShouldBe(AccessRights.None);
	}

	[Fact]
	void ParseFlags_empty_token_list_is_legal_without_a_named_zero_member()
	{
		var result = EnumLexical.ParseFlags<ArchiveMode>(_archiveModeTable, [], (int)XmlCaseStyle.CamelCase);

		result.TryGetValue(out Success<ArchiveMode> success).ShouldBeTrue();
		success.Value.ShouldBe((ArchiveMode)0);
	}

	[Fact]
	void ParseFlags_unknown_token_fails_with_a_did_you_mean_suggestion()
	{
		var result = EnumLexical.ParseFlags<AccessRights>(_accessRightsTable, ["raed"], (int)XmlCaseStyle.CamelCase);

		result.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe("raed");
		failure.Detail.ShouldBe("did you mean 'read'?");
	}

	[Fact]
	void ParseFlags_unknown_token_with_no_near_match_has_no_suggestion()
	{
		var result = EnumLexical.ParseFlags<AccessRights>(_accessRightsTable, ["zzzzz"], (int)XmlCaseStyle.CamelCase);

		result.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Detail.ShouldBeNull();
	}

	[Fact]
	void ParseFlags_duplicate_token_fails_with_the_duplicate_reason()
	{
		var result = EnumLexical.ParseFlags<AccessRights>(_accessRightsTable, ["read", "read"],
			(int)XmlCaseStyle.CamelCase);

		result.TryGetValue(out Failure failure).ShouldBeTrue();
		failure.Reason.ShouldBe(ParseFailure.Duplicate);
		failure.Input.ShouldBe("read");
		failure.ExpectedType.ShouldBe(_accessRightsTable.TypeName);
		failure.Detail.ShouldBeNull();
	}

	// An int-backed enum's sign bit (1 << 31) stores negative in the CLR but rides the table as the
	// zero-extended positive 2147483648L — exactly what the generator emits, and what ToBits produces
	// for a 4-byte enum (uint reinterpret, zero-extended to long). This table proves the runtime
	// mechanism end to end at that boundary.
	static readonly EnumNameTable _wideRightsTable = new(
		typeof(WideRights),
		nameof(WideRights),
		[
			["read", "Read", "read", "READ", "read"],
			["high", "High", "high", "HIGH", "high"]
		],
		[1, 2147483648L]);

	[Fact]
	void FormatFlags_and_ParseFlags_round_trip_a_sign_bit_member_of_an_int_backed_enum()
	{
		var value = WideRights.Read | WideRights.High;

		var names = EnumLexical.FormatFlags(_wideRightsTable, value, (int)XmlCaseStyle.CamelCase);
		names.ShouldBe(["read", "high"]);

		var result = EnumLexical.ParseFlags<WideRights>(_wideRightsTable, names, (int)XmlCaseStyle.CamelCase);
		result.TryGetValue(out Success<WideRights> success).ShouldBeTrue();
		success.Value.ShouldBe(value);
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

	[Flags]
	enum ArchiveMode
	{
		ReadWrite = 3,
		Append = 4
	}

	[Flags]
	enum WideRights
	{
		Read = 1,
		High = 1 << 31
	}
}
