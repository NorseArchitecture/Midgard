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
/// Compiles a fixture contract set through the real generator, loads the emitted assembly, writes a
/// value with the (already-shipped, Task 6) generated writer, and reads it back with the (this task's)
/// generated reader — the presence-aware, accumulating <c>Read</c> design spec §8 describes. Every
/// failure-path test hand-writes the XML fragment directly rather than round-tripping through the
/// writer, since the writer by construction only ever emits input the reader accepts cleanly.
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
			public Result<string> Name { get; init; }
			public Result<decimal> Limit { get; init; }
			public Result<DateOnly> BirthDate { get; init; }
			public Result<int> Age { get; init; }
			public Result<int>? Score { get; init; }
			public Extra? Detail { get; init; }
			public List<Tag> Tags { get; init; } = new();
		}

		public sealed record Extra
		{
			public Result<string> Note { get; init; }
		}

		public sealed record Tag
		{
			public Result<string> Value { get; init; }
		}

		public sealed record PersonResponse
		{
			public string Status { get; init; } = "";
		}

		public sealed class PersonController : GrpcControllerBase
		{
			public Task<ActionResult<PersonResponse>> Do([FromBody] PersonRequest request) =>
				Task.FromResult(new ActionResult<PersonResponse>(new PersonResponse()));
		}
		""";

	[Fact]
	void Happy_path_round_trip_preserves_a_required_empty_string()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		var request = compiled.CreateInstance("Norse.Fixtures.ReaderPerson.PersonRequest",
			("Name", new Result<string>(new Success<string>(""))),
			("Limit", new Result<decimal>(new Success<decimal>(1234.56m))),
			("BirthDate", new Result<DateOnly>(new Success<DateOnly>(new DateOnly(2020, 1, 15)))),
			("Age", new Result<int>(new Success<int>(42))),
			("Tags", compiled.CreateList("Norse.Fixtures.ReaderPerson.Tag")));

		var xml = WriteRoot(shape, request, WireCaseStyle.CamelCase);

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
		context.Failures.ShouldContain(f => f.Path == "personRequest/@birthday" && f.Detail == "unknown attribute — did you mean 'birthDate'?");
		context.Failures.ShouldContain(f => f.Path == "personRequest/@birthDate" && f.Detail == "required value missing");
	}

	[Fact]
	void Three_malformed_scalars_yield_three_accumulated_failures()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		var xml = """<personRequest name="ok" limit="not-a-decimal" birthDate="not-a-date" age="not-a-number" />""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.Count.ShouldBe(3);
		context.Failures.ShouldContain(f => f.Path == "personRequest/@limit" && f.Detail == "cannot parse 'not-a-decimal' as Decimal");
		context.Failures.ShouldContain(f => f.Path == "personRequest/@birthDate" && f.Detail == "cannot parse 'not-a-date' as DateOnly");
		context.Failures.ShouldContain(f => f.Path == "personRequest/@age" && f.Detail == "cannot parse 'not-a-number' as Int32");
	}

	[Fact]
	void Text_content_and_a_duplicate_singleton_element_accumulate_with_correct_paths()
	{
		var compiled = CompiledFixture.Build(PersonFixture);
		var shape = compiled.Shape("PersonRequest");

		var xml = """<personRequest name="ok" limit="1.00" birthDate="2020-01-15" age="1"><extra note="a" /><extra note="b" />stray text</personRequest>""";

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
		var xml = """<personRequest name="ok" limit="1.00" birthDate="2020-01-15" age="1"><extar note="a" /></personRequest>""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.CamelCase);

		context.Failures.ShouldContain(f => f.Path == "personRequest/extar" && f.Detail == "unknown element — did you mean 'extra'?");
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

		context.Failures.ShouldContain(f => f.Path == "wrongRoot" && f.Detail == "unexpected root element — expected 'personRequest'");
		// The walk continued past the mismatch — no unknown-attribute or required-missing noise for the
		// four attributes that were all actually present and valid.
		context.Failures.Count.ShouldBe(1);
	}

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
			public Result<Status> State { get; init; }
			public Payment Payment { get; init; } = null!;
		}

		public sealed record Payment
		{
			public Result<decimal> Amount { get; init; }
		}

		public sealed record OrderResponse
		{
			public string Ok { get; init; } = "";
		}

		public sealed class OrderController : GrpcControllerBase
		{
			public Task<ActionResult<OrderResponse>> Do([FromBody] OrderRequest request) =>
				Task.FromResult(new ActionResult<OrderResponse>(new OrderResponse()));
		}
		""";

	[Fact]
	void A_missing_required_singleton_element_is_accumulated_as_required_value_missing()
	{
		var compiled = CompiledFixture.Build(RequiredNestedFixture);
		var shape = compiled.Shape("OrderRequest");

		// State is present and valid; Payment (a required, non-nullable singleton complex member) never appears.
		var xml = """<OrderRequest State="Draft" />""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.PascalCase);

		context.Failures.ShouldHaveSingleItem().ShouldBe(new XmlReadFailure("OrderRequest/Payment", "required value missing"));
	}

	[Fact]
	void An_undefined_non_flags_enum_name_is_a_malformed_scalar_failure()
	{
		var compiled = CompiledFixture.Build(RequiredNestedFixture);
		var shape = compiled.Shape("OrderRequest");

		var xml = """<OrderRequest State="Cancelled"><Payment Amount="1.00" /></OrderRequest>""";

		var (_, context) = ReadRoot(shape, xml, WireCaseStyle.PascalCase);

		context.Failures.ShouldHaveSingleItem().ShouldBe(new XmlReadFailure("OrderRequest/@State", "cannot parse 'Cancelled' as Status"));
	}

	const string FlagsFixture = """
		using System;
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.ReaderFlags;

		[Flags]
		public enum Access
		{
			Read = 1,
			Write = 2,
			Execute = 4,
			ReadWrite = Read | Write
		}

		[DataContract]
		public sealed record AccessRequest
		{
			public Result<Access> Perm { get; init; }
		}

		public sealed record FlagsResponse
		{
			public string Ok { get; init; } = "";
		}

		public sealed class AccessController : GrpcControllerBase
		{
			public Task<ActionResult<FlagsResponse>> Do([FromBody] AccessRequest request) =>
				Task.FromResult(new ActionResult<FlagsResponse>(new FlagsResponse()));
		}
		""";

	[Fact]
	void A_flags_value_written_canonically_and_a_space_separated_decomposition_both_read_back_correctly()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("AccessRequest");
		var accessType = compiled.ResolveType("Norse.Fixtures.ReaderFlags.Access");

		// Canonical write form for Read|Write is the exact-match compound name "ReadWrite".
		var readWrite = Enum.ToObject(accessType, 3);
		var request = compiled.CreateInstance("Norse.Fixtures.ReaderFlags.AccessRequest",
			("Perm", CompiledFixture.CreateResultSuccess(accessType, readWrite)));
		var canonicalXml = WriteFragment(shape, request, WireCaseStyle.PascalCase);

		var (canonicalValue, canonicalContext) = ReadFragment(shape, canonicalXml, "AccessRequest", WireCaseStyle.PascalCase);
		canonicalContext.HasFailures.ShouldBeFalse();

		// A hand-written space-separated decomposition of the same value must read back identically —
		// the reader accepts both forms even though the writer only ever emits the canonical one (§7).
		var decomposedXml = """<AccessRequest Perm="Read Write" />""";
		var (decomposedValue, decomposedContext) = ReadFragment(shape, decomposedXml, "AccessRequest", WireCaseStyle.PascalCase);
		decomposedContext.HasFailures.ShouldBeFalse();

		var canonicalBits = EnumBits(GetProperty(canonicalValue!, "Perm"), accessType);
		var decomposedBits = EnumBits(GetProperty(decomposedValue!, "Perm"), accessType);
		decomposedBits.ShouldBe(canonicalBits);
		decomposedBits.ShouldBe(3);
	}

	[Fact]
	void A_duplicate_flags_token_is_accumulated_as_its_own_failure()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("AccessRequest");

		var xml = """<AccessRequest Perm="Read Read" />""";

		var (_, context) = ReadFragment(shape, xml, "AccessRequest", WireCaseStyle.PascalCase);

		context.Failures.ShouldContain(f => f.Path == "AccessRequest/@Perm" && f.Detail == "duplicate flags token 'Read'");
	}

	static long EnumBits(object? boxedResult, Type enumType)
	{
		var resultType = typeof(Result<>).MakeGenericType(enumType);
		var successType = typeof(Success<>).MakeGenericType(enumType);
		var tryGetValue = resultType.GetMethod("TryGetValue", [successType.MakeByRefType()])!;
		var args = new object?[] { null };
		tryGetValue.Invoke(boxedResult, args).ShouldBe(true);
		var value = successType.GetProperty("Value")!.GetValue(args[0]);
		return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
	}

	static object GetProperty(object instance, string name)
	{
		var property = instance.GetType().GetProperty(name) ?? throw new InvalidOperationException($"Property '{name}' was not found on '{instance.GetType()}'.");
		return property.GetValue(instance)!;
	}

	static string WriteRoot(IXmlShape shape, object value, WireCaseStyle style)
	{
		using MemoryStream stream = new();
		var settings = new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), OmitXmlDeclaration = false, Indent = false };
		using (var writer = XmlWriter.Create(stream, settings))
			shape.WriteObject(writer, value, style);

		return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(stream.ToArray());
	}

	static string WriteFragment(IXmlShape shape, object value, WireCaseStyle style)
	{
		var sb = new System.Text.StringBuilder();
		var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment, Indent = false };
		using (var writer = XmlWriter.Create(sb, settings))
			shape.WriteObject(writer, value, style);

		return sb.ToString();
	}

	/// <summary>
	/// Simulates the not-yet-built Task 8 formatter's contract with a generated shape: position an
	/// <see cref="XmlReader"/> on the document's content, push the root element's own observed name
	/// (the caller's responsibility per <see cref="ReaderEmitter"/>'s remarks — <c>Read</c> itself never
	/// pushes its own path segment), call <c>ReadObject</c>, then pop.
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

	/// <summary>Same protocol as <see cref="ReadRoot"/>, but for a standalone fragment (no XML declaration) — the shape of a nested member read in isolation, exactly like <c>WriterEmissionTests</c>' <c>WriteFragment</c> counterpart.</summary>
	static (object? Value, XmlReadContext Context) ReadFragment(IXmlShape shape, string xml, string expectedRootName, WireCaseStyle style)
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
			_assembly.GetType(fullyQualifiedName) ?? throw new InvalidOperationException($"Type '{fullyQualifiedName}' was not found in the compiled fixture assembly.");

		public IXmlShape Shape(string contractShortName)
		{
			var shapeType = ResolveType($"{_rootNamespace}.NorseXmlShapes.{contractShortName}XmlShape");
			return (IXmlShape)Activator.CreateInstance(shapeType)!;
		}

		public object CreateInstance(string fullyQualifiedTypeName, params (string Property, object? Value)[] values)
		{
			var type = ResolveType(fullyQualifiedTypeName);
			var instance = Activator.CreateInstance(type)!;
			foreach (var (property, value) in values)
			{
				var propertyInfo = type.GetProperty(property) ?? throw new InvalidOperationException($"Property '{property}' was not found on '{fullyQualifiedTypeName}'.");
				propertyInfo.SetValue(instance, value);
			}

			return instance;
		}

		public IList CreateList(string itemFullyQualifiedTypeName, params object[] items)
		{
			var itemType = ResolveType(itemFullyQualifiedTypeName);
			var listType = typeof(List<>).MakeGenericType(itemType);
			var list = (IList)Activator.CreateInstance(listType)!;
			foreach (var item in items)
				list.Add(item);

			return list;
		}

		public static object CreateResultSuccess(Type enumType, object enumValue)
		{
			var successType = typeof(Success<>).MakeGenericType(enumType);
			var success = Activator.CreateInstance(successType, enumValue)!;
			var resultType = typeof(Result<>).MakeGenericType(enumType);
			return Activator.CreateInstance(resultType, success)!;
		}
	}
}
