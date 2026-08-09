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
}
