using System.Text.Json;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Json;

public sealed class EnumLexicalJsonConverterTests
{
	const string IllegalWriteMessage = "a failed or default Result<T> is illegal to write";

	// Columns follow XmlCaseStyle's declared order: Camel, Pascal, Snake, Upper, Lower — the same
	// hand-built idiom EnumLexicalTests (Xml) uses.
	static readonly EnumNameTable _table = new(
		typeof(TableStatus),
		nameof(TableStatus),
		[
			["active", "Active", "active", "ACTIVE", "active"],
			["inactive", "Inactive", "inactive", "INACTIVE", "inactive"]
		],
		[1, 2]);

	static JsonSerializerOptions CreateOptions()
	{
		var registry = new EnumNameRegistry();
		registry.Add(_table);
		return NorseJsonTestOptions.Create(registry);
	}

	[Fact]
	void Write_plain_enum_emits_the_governed_name() =>
		JsonSerializer.Serialize(TableStatus.Active, CreateOptions()).ShouldBe("\"active\"");

	[Fact]
	void Read_plain_enum_string_token_parses_through_the_table() =>
		JsonSerializer.Deserialize<TableStatus>("\"active\"", CreateOptions()).ShouldBe(TableStatus.Active);

	[Fact]
	void Read_plain_enum_wrong_case_throws_json_exception_rendering_the_malformed_failure()
	{
		var exception =
			Should.Throw<JsonException>(() => JsonSerializer.Deserialize<TableStatus>("\"Active\"", CreateOptions()));

		exception.Message.ShouldBe(
			FailureDetail.Render(new Failure(ParseFailure.Malformed, "Active", nameof(TableStatus))));
	}

	[Fact]
	void Read_plain_enum_number_token_throws_json_exception_names_never_numerics() =>
		Should.Throw<JsonException>(() => JsonSerializer.Deserialize<TableStatus>("1", CreateOptions()));

	[Fact]
	void Read_plain_enum_unregistered_type_throws_the_named_gap()
	{
		var options = NorseJsonTestOptions.Create(); // empty registry — no table for TableStatus

		var exception =
			Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<TableStatus>("\"active\"", options));

		exception.Message.ShouldBe(
			"no generated name table for enum 'TableStatus' — an enum outside every facade closure has no text wire law");
	}

	[Fact]
	void Read_result_enum_string_token_succeeds()
	{
		var result = JsonSerializer.Deserialize<Result<TableStatus>>("\"active\"", CreateOptions());

		result.Value.ShouldBeOfType<Success<TableStatus>>().Value.ShouldBe(TableStatus.Active);
	}

	[Fact]
	void Read_result_enum_wrong_case_captures_a_malformed_failure_never_throws()
	{
		var result = JsonSerializer.Deserialize<Result<TableStatus>>("\"Active\"", CreateOptions());

		result.Value.ShouldBeOfType<Failure>().Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Read_result_enum_number_token_captures_a_malformed_failure_never_throws()
	{
		var result = JsonSerializer.Deserialize<Result<TableStatus>>("1", CreateOptions());

		result.Value.ShouldBeOfType<Failure>().Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Read_result_enum_null_captures_required_value_missing()
	{
		var result = JsonSerializer.Deserialize<Result<TableStatus>>("null", CreateOptions());

		var failure = result.Value.ShouldBeOfType<Failure>();
		FailureDetail.Render(failure).ShouldBe("required value missing");
	}

	[Fact]
	void Read_optional_result_enum_null_is_the_clr_null() =>
		JsonSerializer.Deserialize<Result<TableStatus>?>("null", CreateOptions()).ShouldBeNull();

	[Fact]
	void Write_result_enum_success_emits_the_governed_name()
	{
		Result<TableStatus> result = TableStatus.Active;

		JsonSerializer.Serialize(result, CreateOptions()).ShouldBe("\"active\"");
	}

	[Fact]
	void Write_result_enum_failure_throws_the_illegal_write_message()
	{
		Result<TableStatus> result = new Failure(ParseFailure.Malformed, "nope", nameof(TableStatus));

		var exception =
			Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(result, CreateOptions()));

		exception.Message.ShouldBe(IllegalWriteMessage);
	}

	[Fact]
	void Write_result_enum_default_throws_the_illegal_write_message()
	{
		var exception = Should.Throw<InvalidOperationException>(() =>
			JsonSerializer.Serialize(default(Result<TableStatus>), CreateOptions()));

		exception.Message.ShouldBe(IllegalWriteMessage);
	}

	enum TableStatus
	{
		Active = 1,
		Inactive = 2
	}

	// Same fixture shape as the XML shape generator's flags fixture (Generator.Tests/Xml/
	// {Writer,Reader}EmissionTests.FlagsFixture): None=0, Read=1, Write=2, Execute=4, All=7 (composite),
	// and a zero-less ArchiveMode carrying only a composite (ReadWrite=3) and a single-bit (Append=4)
	// member — the array/flags twin of the plain-enum tests above.
	static readonly EnumNameTable _flagsTable = new(
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

	static JsonSerializerOptions CreateFlagsOptions()
	{
		var registry = new EnumNameRegistry();
		registry.Add(_flagsTable);
		registry.Add(_archiveModeTable);
		return NorseJsonTestOptions.Create(registry);
	}

	[Fact]
	void Write_flags_enum_decomposes_set_bits_into_a_governed_name_array() =>
		JsonSerializer.Serialize(AccessRights.Read | AccessRights.Write, CreateFlagsOptions())
			.ShouldBe("[\"read\",\"write\"]");

	[Fact]
	void Write_flags_enum_zero_value_emits_an_empty_array() =>
		JsonSerializer.Serialize(AccessRights.None, CreateFlagsOptions()).ShouldBe("[]");

	[Fact]
	void Write_flags_enum_never_emits_the_composite_member() =>
		JsonSerializer.Serialize(AccessRights.All, CreateFlagsOptions()).ShouldBe("[\"read\",\"write\",\"execute\"]");

	[Fact]
	void Write_flags_enum_leftover_bits_throw_the_illegal_write_message()
	{
		var value = (AccessRights)8;
		var exception =
			Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(value, CreateFlagsOptions()));

		exception.Message.ShouldBe(
			$"'{value}' carries bits with no single-bit member of '{_flagsTable.EnumType}' and is illegal to write.");
	}

	[Fact]
	void Write_flags_enum_composite_only_defined_value_throws_the_illegal_write_message()
	{
		var exception = Should.Throw<InvalidOperationException>(() =>
			JsonSerializer.Serialize(ArchiveMode.ReadWrite, CreateFlagsOptions()));

		exception.Message.ShouldBe(
			$"'{ArchiveMode.ReadWrite}' carries bits with no single-bit member of '{_archiveModeTable.EnumType}' and is illegal to write.");
	}

	[Fact]
	void Read_flags_enum_array_OR_accumulates_its_tokens() =>
		JsonSerializer.Deserialize<AccessRights>("[\"read\",\"write\"]", CreateFlagsOptions())
			.ShouldBe(AccessRights.Read | AccessRights.Write);

	[Fact]
	void Read_flags_enum_empty_array_is_the_zero_value() =>
		JsonSerializer.Deserialize<AccessRights>("[]", CreateFlagsOptions()).ShouldBe(AccessRights.None);

	[Fact]
	void Read_flags_enum_empty_array_is_legal_without_a_named_zero_member() =>
		JsonSerializer.Deserialize<ArchiveMode>("[]", CreateFlagsOptions()).ShouldBe((ArchiveMode)0);

	[Fact]
	void Read_flags_enum_unknown_token_throws_with_a_did_you_mean_suggestion()
	{
		var exception = Should.Throw<JsonException>(() =>
			JsonSerializer.Deserialize<AccessRights>("[\"raed\"]", CreateFlagsOptions()));

		exception.Message.ShouldBe(
			FailureDetail.Render(new Failure(ParseFailure.Malformed, "raed", nameof(AccessRights), detail: "did you mean 'read'?")));
	}

	[Fact]
	void Read_flags_enum_duplicate_token_throws()
	{
		var exception = Should.Throw<JsonException>(() =>
			JsonSerializer.Deserialize<AccessRights>("[\"read\",\"read\"]", CreateFlagsOptions()));

		exception.Message.ShouldBe("duplicate value 'read'");
	}

	[Fact]
	void Read_flags_enum_non_array_token_throws() =>
		Should.Throw<JsonException>(() => JsonSerializer.Deserialize<AccessRights>("\"read\"", CreateFlagsOptions()));

	[Fact]
	void Read_flags_enum_non_string_array_element_throws() =>
		Should.Throw<JsonException>(() => JsonSerializer.Deserialize<AccessRights>("[1]", CreateFlagsOptions()));

	[Fact]
	void Read_result_flags_enum_array_succeeds()
	{
		var result = JsonSerializer.Deserialize<Result<AccessRights>>("[\"read\",\"write\"]", CreateFlagsOptions());

		result.Value.ShouldBeOfType<Success<AccessRights>>().Value.ShouldBe(AccessRights.Read | AccessRights.Write);
	}

	[Fact]
	void Read_result_flags_enum_unknown_token_captures_a_malformed_failure_never_throws()
	{
		var result = JsonSerializer.Deserialize<Result<AccessRights>>("[\"raed\"]", CreateFlagsOptions());

		var failure = result.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Detail.ShouldBe("did you mean 'read'?");
	}

	[Fact]
	void Read_result_flags_enum_wrong_cased_known_name_captures_a_malformed_failure_with_a_suggestion()
	{
		var result = JsonSerializer.Deserialize<Result<AccessRights>>("[\"Read\"]", CreateFlagsOptions());

		var failure = result.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe("Read");
		failure.Detail.ShouldBe("did you mean 'read'?");
	}

	[Fact]
	void Read_result_flags_enum_empty_string_token_captures_a_malformed_failure_without_a_suggestion()
	{
		var result = JsonSerializer.Deserialize<Result<AccessRights>>("[\"\"]", CreateFlagsOptions());

		var failure = result.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe(string.Empty);
		failure.Detail.ShouldBeNull();
	}

	[Fact]
	void Read_result_flags_enum_duplicate_token_captures_a_duplicate_failure_never_throws()
	{
		var result = JsonSerializer.Deserialize<Result<AccessRights>>("[\"read\",\"read\"]", CreateFlagsOptions());

		var failure = result.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Duplicate);
		failure.Input.ShouldBe("read");
		failure.Detail.ShouldBeNull();
	}

	[Fact]
	void Read_result_flags_enum_non_array_token_captures_a_malformed_failure_never_throws()
	{
		var result = JsonSerializer.Deserialize<Result<AccessRights>>("\"read\"", CreateFlagsOptions());

		result.Value.ShouldBeOfType<Failure>().Reason.ShouldBe(ParseFailure.Malformed);
	}

	[Fact]
	void Read_result_flags_enum_non_string_array_element_captures_a_malformed_failure_never_throws()
	{
		var result = JsonSerializer.Deserialize<Result<AccessRights>>("[1]", CreateFlagsOptions());

		var failure = result.Value.ShouldBeOfType<Failure>();
		failure.Reason.ShouldBe(ParseFailure.Malformed);
		failure.Input.ShouldBe("1");
	}

	[Fact]
	void Read_result_flags_enum_malformed_array_element_leaves_the_reader_valid_for_the_next_property()
	{
		// The array-skip machinery (ReadArray -> SkipToEndOfArray) has to leave the reader positioned
		// exactly where a well-formed array read would — proven here by a sibling property parsing
		// correctly right after a captured (never thrown) mid-array failure, inside one document.
		var wrapper = JsonSerializer.Deserialize<FlagsWrapper>("{\"rights\":[1],\"next\":\"ok\"}", CreateFlagsOptions());

		wrapper!.Rights.Value.ShouldBeOfType<Failure>().Reason.ShouldBe(ParseFailure.Malformed);
		wrapper.Next.ShouldBe("ok");
	}

	sealed record FlagsWrapper
	{
		public Result<AccessRights> Rights { get; init; }
		public string Next { get; init; } = string.Empty;
	}

	[Fact]
	void Read_result_flags_enum_null_captures_required_value_missing()
	{
		var result = JsonSerializer.Deserialize<Result<AccessRights>>("null", CreateFlagsOptions());

		FailureDetail.Render(result.Value.ShouldBeOfType<Failure>()).ShouldBe("required value missing");
	}

	[Fact]
	void Read_optional_result_flags_enum_null_is_the_clr_null() =>
		JsonSerializer.Deserialize<Result<AccessRights>?>("null", CreateFlagsOptions()).ShouldBeNull();

	[Fact]
	void Write_optional_result_flags_enum_null_emits_json_null() =>
		JsonSerializer.Serialize<Result<AccessRights>?>(null, CreateFlagsOptions()).ShouldBe("null");

	[Fact]
	void Write_optional_result_flags_enum_present_emits_the_governed_name_array()
	{
		Result<AccessRights>? result = AccessRights.Read | AccessRights.Write;

		JsonSerializer.Serialize(result, CreateFlagsOptions()).ShouldBe("[\"read\",\"write\"]");
	}

	[Fact]
	void Write_result_flags_enum_success_emits_the_governed_name_array()
	{
		Result<AccessRights> result = AccessRights.Read | AccessRights.Write;

		JsonSerializer.Serialize(result, CreateFlagsOptions()).ShouldBe("[\"read\",\"write\"]");
	}

	[Fact]
	void Write_result_flags_enum_failure_throws_the_illegal_write_message()
	{
		Result<AccessRights> result = new Failure(ParseFailure.Malformed, "nope", nameof(AccessRights));

		var exception =
			Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(result, CreateFlagsOptions()));

		exception.Message.ShouldBe(IllegalWriteMessage);
	}

	[Fact]
	void Write_result_flags_enum_default_throws_the_illegal_write_message()
	{
		var exception = Should.Throw<InvalidOperationException>(() =>
			JsonSerializer.Serialize(default(Result<AccessRights>), CreateFlagsOptions()));

		exception.Message.ShouldBe(IllegalWriteMessage);
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
}
