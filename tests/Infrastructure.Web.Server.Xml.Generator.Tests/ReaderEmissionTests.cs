using System.Collections;
using System.Reflection;
using System.Xml;
using Norse.Primitives;
// See GeneratorTestHarness.cs's own remarks on this same alias — Xml.Generator (the nearer ancestor)
// declares its own compiler-process-local XmlCaseStyle mirror that would otherwise shadow the real
// runtime enum via plain enclosing-namespace walk.
using WireCaseStyle = Norse.Infrastructure.Web.Server.Xml.XmlCaseStyle;

namespace Norse.Infrastructure.Web.Server.Xml.Generator.Tests;

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
	}
}
