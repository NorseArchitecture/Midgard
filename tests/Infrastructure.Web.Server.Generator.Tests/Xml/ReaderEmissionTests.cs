using System.Collections;
using System.Reflection;
using System.Xml;
using Norse.Infrastructure.Web.Server.Generator.Xml;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;
// The generator (Generator.Xml, imported by the emission fixtures here) declares its own
// compiler-process-local XmlCaseStyle mirror of the runtime enum this using block also imports —
// an unqualified XmlCaseStyle would be ambiguous, so runtime-enum references ride a
// differently-named alias.
using WireCaseStyle = Norse.Infrastructure.Web.Server.Xml.XmlCaseStyle;

namespace Norse.Infrastructure.Web.Server.Generator.Tests.Xml;

/// <summary>
///     Compiles a fixture contract set through the real generator, loads the emitted assembly, writes a
///     value with the (already-shipped, Task 6) generated writer, and reads it back with the (this task's)
///     generated reader — the presence-aware, accumulating <c>Read</c> design spec §8 describes. Every
///     failure-path test hand-writes the XML fragment directly rather than round-tripping through the
///     writer, since the writer by construction only ever emits input the reader accepts cleanly.
/// </summary>
public sealed class ReaderEmissionTests
{
	const string PersonFixture = """
		#nullable enable
		using System;
		using System.Collections.Generic;
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.ReaderPerson;

		[DataContract]
		public sealed record PersonRequest
		{
			[DataMember]
			public Result<string> Name { get; init; }
			[DataMember]
			public Result<decimal> Limit { get; init; }
			[DataMember]
			public Result<DateOnly> BirthDate { get; init; }
			[DataMember]
			public Result<int> Age { get; init; }
			[DataMember]
			public Result<int>? Score { get; init; }
			[DataMember]
			public Extra? Detail { get; init; }
			[DataMember]
			public List<Tag> Tags { get; init; } = new();
		}

		public sealed record Extra
		{
			[DataMember]
			public Result<string> Note { get; init; }
		}

		public sealed record Tag
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record PersonResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class PersonController : GrpcControllerBase
		{
			public Task<ActionResult<PersonResponse>> Do([FromBody] PersonRequest request) =>
				Task.FromResult(new ActionResult<PersonResponse>(new PersonResponse()));
		}
		""";

	const string RequiredNestedFixture = """
		using System;
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.ReaderRequiredNested;

		public enum Status
		{
			Draft = 1,
			Active = 2
		}

		[DataContract]
		public sealed record OrderRequest
		{
			[DataMember]
			public Result<Status> State { get; init; }
			[DataMember]
			public Payment Payment { get; init; } = null!;
		}

		public sealed record Payment
		{
			[DataMember]
			public Result<decimal> Amount { get; init; }
		}

		public sealed record OrderResponse
		{
			[DataMember]
			public string Ok { get; init; } = "";
		}

		public sealed class OrderController : GrpcControllerBase
		{
			public Task<ActionResult<OrderResponse>> Do([FromBody] OrderRequest request) =>
				Task.FromResult(new ActionResult<OrderResponse>(new OrderResponse()));
		}
		""";

	const string FlagsFixture = """
		#nullable enable
		using System;
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.ReaderFlags;

		[Flags]
		public enum AccessRights
		{
			None = 0,
			Read = 1,
			Write = 2,
			Execute = 4
		}

		[Flags]
		public enum ArchiveMode
		{
			ReadWrite = 3,
			Append = 4
		}

		[DataContract]
		public sealed record GrantRequest
		{
			[DataMember]
			public Result<string> Name { get; init; }
			[DataMember]
			public Result<AccessRights> Rights { get; init; }
			[DataMember]
			public Result<AccessRights>? OptionalRights { get; init; }
		}

		public sealed record GrantResponse
		{
			[DataMember]
			public int Code { get; init; }
			[DataMember]
			public AccessRights Rights { get; init; }
			[DataMember]
			public ArchiveMode Mode { get; init; }
			[DataMember]
			public AccessRights? MaybeRights { get; init; }
		}

		public sealed class GrantController : GrpcControllerBase
		{
			public Task<ActionResult<GrantResponse>> Do([FromBody] GrantRequest request) =>
				Task.FromResult(new ActionResult<GrantResponse>(new GrantResponse()));
		}
		""";

	const string AccessRightsFullName = "Norse.Fixtures.ReaderFlags.AccessRights";

	[Fact]
	void Happy_path_round_trip_preserves_a_required_empty_string()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		// Hand-authored rather than built via the shape's own generated Write — Result<T> is a
		// deserialization-only type now, so Write always throws for a Result<T>-wrapped member (every
		// scalar on PersonRequest is one) and can no longer manufacture this fixture itself. Matches the
		// idiom every other fact in this file already uses for its input XML.
		var xml = """<personRequest name="" limit="1234.56" birthDate="2020-01-15" age="42" />""";

		var (value, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.HasFailures.ShouldBeFalse();
		var name = (Result<string>)GetProperty(value!, "Name");
		name.TryGetValue(out Success<string> nameSuccess).ShouldBeTrue();
		nameSuccess.Value.ShouldBe("");

		var limit = (Result<decimal>)GetProperty(value!, "Limit");
		limit.TryGetValue(out Success<decimal> limitSuccess).ShouldBeTrue();
		limitSuccess.Value.ShouldBe(1234.56m);

		var birthDate = (Result<DateOnly>)GetProperty(value!, "BirthDate");
		birthDate.TryGetValue(out Success<DateOnly> birthDateSuccess).ShouldBeTrue();
		birthDateSuccess.Value.ShouldBe(new DateOnly(2020, 1, 15));

		var age = (Result<int>)GetProperty(value!, "Age");
		age.TryGetValue(out Success<int> ageSuccess).ShouldBeTrue();
		ageSuccess.Value.ShouldBe(42);

		GetProperty(value!, "Score").ShouldBeNull();
		GetProperty(value!, "Detail").ShouldBeNull();
	}

	[Fact]
	void An_unknown_attribute_with_a_close_match_and_the_real_attribute_missing_yields_exactly_two_failures()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		var xml = """<personRequest name="ok" limit="1.00" birthday="2020-01-15" age="1" />""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.Count.ShouldBe(2);
		context.Failures.ShouldContain(f =>
			f.Path == "personRequest/@birthday" && f.Detail == "unknown attribute — did you mean 'birthDate'?");
		context.Failures.ShouldContain(f =>
			f.Path == "personRequest/@birthDate" && f.Detail == "required value missing");
	}

	[Fact]
	void Three_malformed_scalars_yield_three_accumulated_failures()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		var xml = """<personRequest name="ok" limit="not-a-decimal" birthDate="not-a-date" age="not-a-number" />""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.Count.ShouldBe(3);
		context.Failures.ShouldContain(f =>
			f.Path == "personRequest/@limit" && f.Detail == "cannot parse 'not-a-decimal' as Decimal");
		context.Failures.ShouldContain(f =>
			f.Path == "personRequest/@birthDate" && f.Detail == "cannot parse 'not-a-date' as DateOnly");
		context.Failures.ShouldContain(f =>
			f.Path == "personRequest/@age" && f.Detail == "cannot parse 'not-a-number' as Int32");
	}

	[Fact]
	void Text_content_and_a_duplicate_singleton_element_accumulate_with_correct_paths()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		var xml =
			"""<personRequest name="ok" limit="1.00" birthDate="2020-01-15" age="1"><extra note="a" /><extra note="b" />stray text</personRequest>""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.Count.ShouldBe(2);
		context.Failures.ShouldContain(f => f.Path == "personRequest/extra" && f.Detail == "duplicate element");
		context.Failures.ShouldContain(f => f.Path == "personRequest" && f.Detail == "text content is not permitted");
	}

	[Fact]
	void An_absent_optional_scalar_reads_as_null_with_no_failure()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		var xml = """<personRequest name="ok" limit="1.00" birthDate="2020-01-15" age="1" />""";

		var (value, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.HasFailures.ShouldBeFalse();
		GetProperty(value!, "Score").ShouldBeNull();
	}

	[Fact]
	void A_present_optional_scalar_parses_its_content()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		var xml = """<personRequest name="ok" limit="1.00" birthDate="2020-01-15" age="1" score="7" />""";

		var (value, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.HasFailures.ShouldBeFalse();
		var score = (Result<int>?)GetProperty(value!, "Score");
		score.HasValue.ShouldBeTrue();
		score!.Value.TryGetValue(out Success<int> scoreSuccess).ShouldBeTrue();
		scoreSuccess.Value.ShouldBe(7);
	}

	[Fact]
	void An_unknown_element_with_a_close_match_is_accumulated_with_a_suggestion()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		// "extar" (transposed) is a distance-2 typo of "extra" — the complex member Detail's element name.
		var xml =
			"""<personRequest name="ok" limit="1.00" birthDate="2020-01-15" age="1"><extar note="a" /></personRequest>""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.ShouldContain(f =>
			f.Path == "personRequest/extar" && f.Detail == "unknown element — did you mean 'extra'?");
	}

	[Fact]
	void Collection_items_dispatch_order_insensitively_and_preserve_document_order()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		// Tags (a collection) interleaved with Detail (a singleton) — order-insensitive dispatch (§8.3)
		// means this is legal, and the three tags land in the result list in document order regardless.
		var xml = """
			<personRequest name="ok" limit="1.00" birthDate="2020-01-15" age="1">
				<tag value="first" />
				<extra note="mid" />
				<tag value="second" />
				<tag value="third" />
			</personRequest>
			""";

		var (value, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.HasFailures.ShouldBeFalse();
		var tags = (IList)GetProperty(value!, "Tags");
		tags.Count.ShouldBe(3);
		TagValue(tags[0]!).ShouldBe("first");
		TagValue(tags[1]!).ShouldBe("second");
		TagValue(tags[2]!).ShouldBe("third");

		static string TagValue(object tag)
		{
			var result = (Result<string>)GetProperty(tag, "Value");
			result.TryGetValue(out Success<string> success).ShouldBeTrue();
			return success.Value;
		}
	}

	[Fact]
	void A_mismatched_root_element_is_accumulated_and_the_walk_still_continues()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		var xml = """<wrongRoot name="ok" limit="1.00" birthDate="2020-01-15" age="1" />""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.ShouldContain(f =>
			f.Path == "wrongRoot" && f.Detail == "unexpected root element — expected 'personRequest'");
		// The walk continued past the mismatch — no unknown-attribute or required-missing noise for the
		// four attributes that were all actually present and valid.
		context.Failures.Count.ShouldBe(1);
	}

	[Fact]
	void A_missing_required_singleton_element_is_accumulated_as_required_value_missing()
	{
		var compiled = CompiledFixture.Build(RequiredNestedFixture);
		var shape = compiled.Shape("OrderRequest");

		// State is present and valid; Payment (a required, non-nullable singleton complex member) never appears.
		var xml = """<OrderRequest State="Draft" />""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.PascalCase);

		context.Failures.ShouldHaveSingleItem()
			.ShouldBe(new XmlReadFailure("OrderRequest/Payment", "required value missing"));
	}

	[Fact]
	void An_undefined_non_flags_enum_name_is_a_malformed_scalar_failure()
	{
		var compiled = CompiledFixture.Build(RequiredNestedFixture);
		var shape = compiled.Shape("OrderRequest");

		var xml = """<OrderRequest State="Cancelled"><Payment Amount="1.00" /></OrderRequest>""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.PascalCase);

		context.Failures.ShouldHaveSingleItem()
			.ShouldBe(new XmlReadFailure("OrderRequest/@State", "cannot parse 'Cancelled' as Status"));
	}

	[Fact]
	void An_entirely_absent_required_enum_attribute_yields_required_value_missing_distinct_from_present_empty()
	{
		var compiled = CompiledFixture.Build(RequiredNestedFixture);
		var shape = compiled.Shape("OrderRequest");

		// State never appears on the element at all (spec §8.2 presence law) — must yield the
		// required-missing failure, never routed through EnumLexical.Parse (which would report
		// "cannot parse '' as Status", a Malformed failure, since it treats "" as content, never absence).
		var xml = """<OrderRequest><Payment Amount="1.00" /></OrderRequest>""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.PascalCase);

		context.Failures.ShouldHaveSingleItem()
			.ShouldBe(new XmlReadFailure("OrderRequest/@State", "required value missing"));
	}

	[Fact]
	void A_present_empty_required_enum_attribute_is_malformed_not_required_missing()
	{
		var compiled = CompiledFixture.Build(RequiredNestedFixture);
		var shape = compiled.Shape("OrderRequest");

		// State is present with empty content — distinct from entire absence above: this is Malformed
		// (EnumLexical.Parse sees "" as unrecognized content), never the required-missing failure.
		var xml = """<OrderRequest State=""><Payment Amount="1.00" /></OrderRequest>""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.PascalCase);

		context.Failures.ShouldHaveSingleItem()
			.ShouldBe(new XmlReadFailure("OrderRequest/@State", "cannot parse '' as Status"));
	}

	[Fact]
	void Flags_tokens_OR_accumulate_into_the_bare_member_regardless_of_document_order()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("GrantResponse");

		// Execute before read in the document — the parsed set is the OR of its tokens, so document
		// order never matters to the value.
		var xml = """<grantResponse code="7"><rights>execute</rights><rights>read</rights></grantResponse>""";

		var (value, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.HasFailures.ShouldBeFalse();
		GetProperty(value!, "Code").ShouldBe(7);
		GetProperty(value!, "Rights").ShouldBe(Enum.ToObject(compiled.ResolveType(AccessRightsFullName), 5));
	}

	[Fact]
	void Flags_tokens_OR_accumulate_into_a_success_for_a_Result_wrapped_member()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("GrantRequest");

		var xml = """<grantRequest name="ok"><rights>read</rights><rights>write</rights></grantRequest>""";

		var (value, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.HasFailures.ShouldBeFalse();
		GetProperty(value!, "Rights").ShouldBe(compiled.CreateEnumSuccess(AccessRightsFullName, 3));
	}

	[Fact]
	void An_entirely_absent_flags_member_reads_as_the_zero_value_with_no_failure()
	{
		// The empty array is the zero value, legal with or without a named zero member — never the
		// required-value-missing failure a required plain scalar's absence yields: AccessRights carries
		// a named zero (None), ArchiveMode carries none, and both read as their zero value.
		var compiled = CompiledFixture.Build(FlagsFixture);

		var (request, requestContext) =
			ReadRoot(compiled.Shape("GrantRequest"), """<grantRequest name="ok" />""", WireCaseStyle.CamelCase);
		requestContext.HasFailures.ShouldBeFalse();
		GetProperty(request!, "Rights").ShouldBe(compiled.CreateEnumSuccess(AccessRightsFullName, 0));

		var (response, responseContext) =
			ReadRoot(compiled.Shape("GrantResponse"), """<grantResponse code="1" />""", WireCaseStyle.CamelCase);
		responseContext.HasFailures.ShouldBeFalse();
		GetProperty(response!, "Rights")
			.ShouldBe(Enum.ToObject(compiled.ResolveType(AccessRightsFullName), 0));
		GetProperty(response!, "Mode")
			.ShouldBe(Enum.ToObject(compiled.ResolveType("Norse.Fixtures.ReaderFlags.ArchiveMode"), 0));
	}

	[Fact]
	void An_absent_nullable_flags_member_reads_as_the_zero_value_never_CLR_null()
	{
		// Deliberate law: the repeated-element (array) form has no absence marker — zero elements IS the
		// zero value, so a nullable flags member can never read back as CLR null the way a nullable
		// attribute-shaped scalar does. Both nullable shapes (Result<T>? and bare T?) land on the
		// non-null zero.
		var compiled = CompiledFixture.Build(FlagsFixture);

		var (request, requestContext) =
			ReadRoot(compiled.Shape("GrantRequest"), """<grantRequest name="ok" />""", WireCaseStyle.CamelCase);
		requestContext.HasFailures.ShouldBeFalse();
		var optionalRights = GetProperty(request!, "OptionalRights");
		optionalRights.ShouldNotBeNull();
		optionalRights.ShouldBe(compiled.CreateEnumSuccess(AccessRightsFullName, 0));

		var (response, responseContext) =
			ReadRoot(compiled.Shape("GrantResponse"), """<grantResponse code="1" />""", WireCaseStyle.CamelCase);
		responseContext.HasFailures.ShouldBeFalse();
		var maybeRights = GetProperty(response!, "MaybeRights");
		maybeRights.ShouldNotBeNull();
		maybeRights.ShouldBe(Enum.ToObject(compiled.ResolveType(AccessRightsFullName), 0));
	}

	[Fact]
	void A_present_nullable_flags_member_OR_accumulates_its_tokens()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);

		var xml =
			"""<grantRequest name="ok"><optionalRights>read</optionalRights><optionalRights>write</optionalRights></grantRequest>""";
		var (request, context) = ReadRoot(compiled.Shape("GrantRequest"), xml, WireCaseStyle.CamelCase);

		context.HasFailures.ShouldBeFalse();
		GetProperty(request!, "OptionalRights").ShouldBe(compiled.CreateEnumSuccess(AccessRightsFullName, 3));
	}

	[Theory]
	[InlineData("rread", "unknown value — did you mean 'read'?")]
	[InlineData("Read", "unknown value — did you mean 'read'?")]
	[InlineData("banana", "unknown value")]
	void An_unknown_flags_token_accumulates_with_the_did_you_mean_suggestion(string token, string expectedDetail)
	{
		// Tokens resolve by exact governed-name match only — a typo ("rread") and a wrong-case spelling
		// ("Read") both miss, and both get the case-insensitive nearest-name suggestion; an unrelated
		// token gets none.
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("GrantRequest");

		var xml = $"""<grantRequest name="ok"><rights>{token}</rights></grantRequest>""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.ShouldHaveSingleItem()
			.ShouldBe(new XmlReadFailure("grantRequest/rights[1]", expectedDetail));
	}

	[Fact]
	void A_duplicate_flags_token_accumulates_a_failure_and_the_set_carries_the_token_once()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("GrantRequest");

		var xml = """<grantRequest name="ok"><rights>read</rights><rights>read</rights></grantRequest>""";

		var (value, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.ShouldHaveSingleItem()
			.ShouldBe(new XmlReadFailure("grantRequest/rights[2]", "duplicate value"));
		GetProperty(value!, "Rights").ShouldBe(compiled.CreateEnumSuccess(AccessRightsFullName, 1));
	}

	[Fact]
	void Nested_markup_inside_a_flags_element_accumulates_a_failure_and_the_walk_continues()
	{
		// A flags element carries a governed-name token as text content, never markup — but nested markup
		// is an accumulable failure like every sibling failure path (unknown token, duplicate, text
		// content), never an exception escaping the reader: the offending element is consumed whole and
		// the walk continues to the valid sibling.
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("GrantRequest");

		var xml = """<grantRequest name="ok"><rights><x /></rights><rights>read</rights></grantRequest>""";

		var (value, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.ShouldHaveSingleItem()
			.ShouldBe(new XmlReadFailure("grantRequest/rights[1]", "nested markup is not permitted"));
		GetProperty(value!, "Rights").ShouldBe(compiled.CreateEnumSuccess(AccessRightsFullName, 1));
	}

	[Fact]
	void An_unknown_element_near_a_flags_member_name_suggests_it()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("GrantRequest");

		// "rigths" (transposed) is a distance-2 typo of the flags member's element name "rights".
		var xml = """<grantRequest name="ok"><rigths>read</rigths></grantRequest>""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.ShouldHaveSingleItem()
			.ShouldBe(new XmlReadFailure("grantRequest/rigths", "unknown element — did you mean 'rights'?"));
	}

	static object GetProperty(object instance, string name)
	{
		var property = instance.GetType().GetProperty(name) ??
			throw new InvalidOperationException($"Property '{name}' was not found on '{instance.GetType()}'.");
		return property.GetValue(instance)!;
	}

	/// <summary>
	///     Simulates the not-yet-built Task 8 formatter's contract with a generated shape: position an
	///     <see cref="XmlReader" /> on the document's content, push the root element's own observed name
	///     (the caller's responsibility per <see cref="ReaderEmitter" />'s remarks — <c>Read</c> itself never
	///     pushes its own path segment), call <c>ReadObject</c>, then pop.
	/// </summary>
	static (object? Value, XmlReadContext Context) ReadRoot(IXmlShape shape, string xml, WireCaseStyle style)
	{
		using var stringReader = new StringReader(xml);
		using var reader = XmlReader.Create(stringReader);
		reader.MoveToContent();
		var context = new XmlReadContext();
		context.PushElement(reader.LocalName);
		var value = shape.ReadObject(reader, style, context);
		context.Pop();
		return (value, context);
	}

	/// <summary>
	///     Same protocol as <see cref="ReadRoot" />, but for a standalone fragment (no XML declaration) — the shape of a
	///     nested member read in isolation, exactly like <c>WriterEmissionTests</c>' <c>WriteFragment</c> counterpart.
	/// </summary>
	static (object? Value, XmlReadContext Context) ReadFragment(IXmlShape shape, string xml, string expectedRootName,
		WireCaseStyle style)
	{
		using var stringReader = new StringReader(xml);
		var settings = new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment };
		using var reader = XmlReader.Create(stringReader, settings);
		reader.MoveToContent();
		var context = new XmlReadContext();
		context.PushElement(expectedRootName);
		var value = shape.ReadObject(reader, style, context);
		context.Pop();
		return (value, context);
	}

	sealed class CompiledFixture
	{
		readonly Assembly _assembly;
		readonly string _rootNamespace;

		CompiledFixture(Assembly assembly, string rootNamespace)
		{
			_assembly = assembly;
			_rootNamespace = rootNamespace;
		}

		public static CompiledFixture Build(params string[] sources)
		{
			var (diagnostics, outputCompilation) = GeneratorTestHarness.Run(sources);
			diagnostics.ShouldBeEmpty();

			using MemoryStream stream = new();
			var emitResult = outputCompilation.Emit(stream);
			emitResult.Success.ShouldBeTrue(string.Join("\n", emitResult.Diagnostics));

			stream.Position = 0;
			var assembly = Assembly.Load(stream.ToArray());
			return new CompiledFixture(assembly, outputCompilation.AssemblyName ?? "Norse.Generated");
		}

		public Type ResolveType(string fullyQualifiedName) =>
			_assembly.GetType(fullyQualifiedName) ??
			throw new InvalidOperationException(
				$"Type '{fullyQualifiedName}' was not found in the compiled fixture assembly.");

		public IXmlShape Shape(string contractShortName)
		{
			var shapeType = ResolveType($"{_rootNamespace}.NorseXmlShapes.{contractShortName}XmlShape");
			return (IXmlShape)Activator.CreateInstance(shapeType)!;
		}

		/// <summary>
		///     Builds a <c>Result&lt;TEnum&gt;</c> success case for a fixture-local enum only known by name at
		///     this test project's own compile time — <c>WriterEmissionTests</c>' helper of the same name,
		///     reflected the same way: <c>Result&lt;T&gt;.op_Implicit</c> is the only public surface that
		///     constructs a success case without a compile-time <c>T</c>. <c>Result&lt;T&gt;</c> is a record
		///     struct, so the boxed instance compares by value against a generated reader's own — the flags
		///     facts' Shouldly assertions ride exactly that.
		/// </summary>
		public object CreateEnumSuccess(string enumFullyQualifiedName, int underlyingValue)
		{
			var enumType = ResolveType(enumFullyQualifiedName);
			var resultType = typeof(Result<>).MakeGenericType(enumType);
			var implicitOperator = resultType.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, null,
					[enumType], null)
				?? throw new InvalidOperationException(
					$"'{resultType}' has no implicit conversion operator from '{enumType}'.");
			return implicitOperator.Invoke(null, [Enum.ToObject(enumType, underlyingValue)])!;
		}
	}
}
