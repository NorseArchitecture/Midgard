using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

public sealed class EnumLexicalTests
{
	enum TableStatus
	{
		Active = 1,
		Inactive = 2
	}

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
		var exception = Should.Throw<InvalidOperationException>(() => EnumLexical.Format(_table, value, (int)XmlCaseStyle.CamelCase));
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
}
