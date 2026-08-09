using System.Collections;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Norse.Primitives;
// No `using Norse.Infrastructure.Web.Server.Xml;` needed for IXmlShape — this test file's own
// namespace nests under Norse.Infrastructure.Web.Server.Xml.Generator, which nests under
// Norse.Infrastructure.Web.Server.Xml itself, so IXmlShape resolves via plain enclosing-namespace
// walk. XmlCaseStyle needs the rename below: Xml.Generator (the *nearer* ancestor, via NameCasing.cs)
// declares its own compiler-process-local XmlCaseStyle mirror, so the walk finds that one first,
// before ever reaching Xml's real runtime enum — a same-named alias wouldn't out-rank it either
// (usings before a file-scoped namespace bind at the outermost compilation-unit level, the very last
// thing checked). A differently-named alias sidesteps the shadow entirely.
using WireCaseStyle = Norse.Infrastructure.Web.Server.Xml.XmlCaseStyle;

namespace Norse.Infrastructure.Web.Server.Xml.Generator.Tests;

/// <summary>
///     Compiles a fixture contract set through the real generator, loads the emitted assembly, and
///     instantiates the generated <c>{Contract}XmlShape</c> classes to assert canonical writer output
///     byte-exact (design spec §6) — the brief's literal <c>QuoteRequest</c> example, flags-canonical
///     forms, and the failed-<c>Result&lt;T&gt;</c> throw, among others.
/// </summary>
public sealed class WriterEmissionTests
{
	const string QuoteFixture = """
		#nullable enable
		using System;
		using System.Collections.Generic;
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.WriterQuote;

		public enum TableStatus
		{
			Active = 1,
			Inactive = 2
		}

		[DataContract]
		public sealed record QuoteRequest
		{
			[DataMember]
			public Result<decimal> Limit { get; init; }
			[DataMember]
			public Result<TableStatus> Status { get; init; }
			[DataMember]
			public Result<DateOnly>? Effective { get; init; }
			[DataMember]
			public List<CoverageLine> Coverages { get; init; } = new();
		}

		public sealed record CoverageLine
		{
			[DataMember]
			public Result<string> Code { get; init; }
		}

		public sealed record QuoteResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class QuoteController : GrpcControllerBase
		{
			public Task<ActionResult<QuoteResponse>> Do([FromBody] QuoteRequest request) =>
				Task.FromResult(new ActionResult<QuoteResponse>(new QuoteResponse()));
		}
		""";

	const string TableStatusFullName = "Norse.Fixtures.WriterQuote.TableStatus";

	// Task 8 restores unwrap-on-success, the same pinned wording ResultSerializers.IllegalWriteMessage
	// and the JSON converters use platform-wide — only a failed or default Result<T> is illegal to write.
	const string IllegalWriteMessage = "a failed or default Result<T> is illegal to write";

	const string TruncationFixture = """
		#nullable enable
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.WriterTruncation;

		[DataContract]
		public sealed record TruncationRequest
		{
			[DataMember]
			public Result<string> First { get; init; }
			[DataMember]
			public Result<int> Second { get; init; }
			[DataMember]
			public TruncationNested Nested { get; init; } = null!;
		}

		public sealed record TruncationNested
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record TruncationResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class TruncationController : GrpcControllerBase
		{
			public Task<ActionResult<TruncationResponse>> Do([FromBody] TruncationRequest request) =>
				Task.FromResult(new ActionResult<TruncationResponse>(new TruncationResponse()));
		}
		""";

	const string ResponseFixture = """
		#nullable enable
		using System.Collections.Generic;
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.WriterResponse;

		[DataContract]
		public sealed record PingRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record Extra
		{
			[DataMember]
			public string Note { get; init; } = "";
		}

		public sealed record Tag
		{
			[DataMember]
			public string Name { get; init; } = "";
		}

		public sealed record PingResponse
		{
			[DataMember]
			public int Code { get; init; }
			[DataMember]
			public string? Note { get; init; }
			[DataMember]
			public Extra? Detail { get; init; }
			[DataMember]
			public List<Tag> Tags { get; init; } = new();
		}

		public sealed class PingController : GrpcControllerBase
		{
			public Task<ActionResult<PingResponse>> Do([FromBody] PingRequest request) =>
				Task.FromResult(new ActionResult<PingResponse>(new PingResponse()));
		}
		""";

	const string SharedTypeFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.WriterSharedType;

		public sealed record SharedAddress
		{
			[DataMember]
			public Result<string> Line1 { get; init; }
		}

		[DataContract]
		public sealed record RequestA
		{
			[DataMember]
			public SharedAddress Home { get; init; } = null!;
		}

		[DataContract]
		public sealed record RequestB
		{
			[DataMember]
			public SharedAddress Office { get; init; } = null!;
		}

		public sealed record SharedResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class ControllerA : GrpcControllerBase
		{
			public Task<ActionResult<SharedResponse>> Do([FromBody] RequestA request) =>
				Task.FromResult(new ActionResult<SharedResponse>(new SharedResponse()));
		}

		public sealed class ControllerB : GrpcControllerBase
		{
			public Task<ActionResult<SharedResponse>> Do([FromBody] RequestB request) =>
				Task.FromResult(new ActionResult<SharedResponse>(new SharedResponse()));
		}
		""";

	const string DataMemberFilterFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.WriterDataMemberFilter;

		[DataContract]
		public sealed record FilterRequest
		{
			[DataMember]
			public Result<string> Name { get; init; }
			public string Shadow { get; init; } = "";
		}

		public sealed record FilterResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class FilterController : GrpcControllerBase
		{
			public Task<ActionResult<FilterResponse>> Do([FromBody] FilterRequest request) =>
				Task.FromResult(new ActionResult<FilterResponse>(new FilterResponse()));
		}
		""";

	[Fact]
	void A_success_state_for_every_required_Result_wrapped_member_unwraps_and_writes_the_clean_values()
	{
		// The brief's literal byte-exact example: a decimal Result member and an enum Result member,
		// both Success, both unwrap and write — no trailing children (Coverages left empty, Effective
		// left absent so its optional attribute omits).
		var compiled = CompiledFixture.Build(QuoteFixture);
		var shape = compiled.Shape("QuoteRequest");

		var quoteRequest = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", new Result<decimal>(new Success<decimal>(1234.56m))),
			("Status", compiled.CreateEnumSuccess(TableStatusFullName, "Inactive")),
			("Coverages", compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine")));

		WriteFragment(shape, quoteRequest, WireCaseStyle.SnakeCase)
			.ShouldBe("""<quote_request limit="1234.56" status="inactive" />""");
	}

	[Fact]
	void An_absent_optional_Result_wrapped_member_omits_its_attribute_and_a_present_one_writes()
	{
		var compiled = CompiledFixture.Build(QuoteFixture);
		var shape = compiled.Shape("QuoteRequest");

		var withoutEffective = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", new Result<decimal>(new Success<decimal>(1m))),
			("Status", compiled.CreateEnumSuccess(TableStatusFullName, "Active")),
			("Coverages", compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine")));
		WriteFragment(shape, withoutEffective, WireCaseStyle.SnakeCase)
			.ShouldBe("""<quote_request limit="1" status="active" />""");

		var withEffective = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", new Result<decimal>(new Success<decimal>(1m))),
			("Status", compiled.CreateEnumSuccess(TableStatusFullName, "Active")),
			("Effective", (Result<DateOnly>?)new Result<DateOnly>(new Success<DateOnly>(new DateOnly(2020, 1, 15)))),
			("Coverages", compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine")));
		WriteFragment(shape, withEffective, WireCaseStyle.SnakeCase)
			.ShouldBe("""<quote_request limit="1" status="active" effective="2020-01-15" />""");
	}

	[Theory]
	[MemberData(nameof(RequiredDecimalFailureStates))]
	void A_failed_or_default_required_Result_wrapped_scalar_member_throws_the_pinned_message(string label,
		Result<decimal> limit)
	{
		// Limit is declared before Status — whichever member's own state is illegal throws at exactly
		// that member's own conditional check; Status (a valid Success here) is never reached because
		// Limit's throw fires first, but that's an ordering fact, not a truncation mechanism: unlike
		// the deleted "unconditional throw" design, every member's check is independently conditional.
		var compiled = CompiledFixture.Build(QuoteFixture);
		var shape = compiled.Shape("QuoteRequest");

		var quoteRequest = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", limit),
			("Status", compiled.CreateEnumSuccess(TableStatusFullName, "Active")),
			("Coverages", compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine")));

		var exception =
			Should.Throw<InvalidOperationException>(() => WriteFragment(shape, quoteRequest, WireCaseStyle.SnakeCase));

		exception.Message.ShouldBe(IllegalWriteMessage, label);
	}

	public static TheoryData<string, Result<decimal>> RequiredDecimalFailureStates() => new()
	{
		{ "failure", new Failure(ParseFailure.Malformed, "nope", nameof(Decimal)) }, { "default", default }
	};

	[Theory]
	[InlineData("failure")]
	[InlineData("default")]
	void A_failed_or_default_required_Result_wrapped_enum_member_throws_the_pinned_message(string label)
	{
		// The mirror of the decimal-member theory above, but for the enum-typed required member —
		// proves the unwrap-on-success law applies uniformly across scalar kinds, not just plain
		// scalars. Limit is a valid Success here so control actually reaches Status's own check.
		var compiled = CompiledFixture.Build(QuoteFixture);
		var shape = compiled.Shape("QuoteRequest");

		var status = label == "failure" ?
			compiled.CreateEnumFailure(TableStatusFullName) :
			compiled.CreateEnumDefault(TableStatusFullName);

		var quoteRequest = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", new Result<decimal>(new Success<decimal>(1m))),
			("Status", status),
			("Coverages", compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine")));

		var exception =
			Should.Throw<InvalidOperationException>(() => WriteFragment(shape, quoteRequest, WireCaseStyle.SnakeCase));

		exception.Message.ShouldBe(IllegalWriteMessage, label);
	}

	[Fact]
	void A_success_state_carrying_an_undefined_enum_value_throws_the_undefined_value_message_not_the_illegal_write_one()
	{
		// (TableStatus)99 is a Success — TryGetValue(out Success<TableStatus>) succeeds and the write
		// proceeds to EnumLexical.Format, which is where THIS throw actually originates (Task 5's
		// runtime, not the generated unwrap check) — a materially different failure mode from an
		// illegal Result<T> state, and it must not be confused with one.
		var compiled = CompiledFixture.Build(QuoteFixture);
		var shape = compiled.Shape("QuoteRequest");
		var tableStatusType = compiled.ResolveType(TableStatusFullName);

		var quoteRequest = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", new Result<decimal>(new Success<decimal>(1m))),
			("Status", compiled.CreateEnumSuccess(TableStatusFullName, 99)),
			("Coverages", compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine")));

		var exception =
			Should.Throw<InvalidOperationException>(() => WriteFragment(shape, quoteRequest, WireCaseStyle.SnakeCase));

		exception.Message.ShouldBe($"'99' is an undefined value of '{tableStatusType}' and is illegal to write.");
	}

	[Fact]
	void A_nested_Result_wrapped_member_unwraps_on_success_and_throws_the_same_pinned_message_on_failure()
	{
		// CoverageLine's only member (Code) is Result<string>-wrapped — proves the law applies uniformly
		// at any nesting depth, not just at the root the tests above already cover.
		var compiled = CompiledFixture.Build(QuoteFixture);
		var coverageShape = compiled.Shape("CoverageLine");

		var success = compiled.CreateInstance("Norse.Fixtures.WriterQuote.CoverageLine",
			("Code", new Result<string>(new Success<string>("GL"))));
		WriteFragment(coverageShape, success, WireCaseStyle.SnakeCase)
			.ShouldBe("""<coverage_line code="GL" />""");

		var failed = compiled.CreateInstance("Norse.Fixtures.WriterQuote.CoverageLine",
			("Code", new Result<string>(new Failure(ParseFailure.Malformed, "nope", nameof(String)))));
		var exception =
			Should.Throw<InvalidOperationException>(() =>
				WriteFragment(coverageShape, failed, WireCaseStyle.SnakeCase));
		exception.Message.ShouldBe(IllegalWriteMessage);
	}

	// Task 8 deletes the truncate-on-unconditional-throw machinery this fixture originally regression-
	// tested (design spec review, Task 13's CS0162 fix): every required Result<T>-wrapped member's throw
	// is now conditional (behind its own TryGetValue check), so nothing textually after it is ever
	// unreachable, and there is no CS0162 risk left to guard against. What survives is the positive case
	// that fix was protecting in the first place — TWO required Result<T> scalars (First, Second) plus a
	// trailing required complex member (Nested) all compile and, when every one of them is a genuine
	// Success, the write runs all the way through: both attributes unwrap, the nested element writes,
	// and the element closes. A second fact proves independence, not truncation: whichever member
	// actually carries an illegal state is the one whose own check throws, regardless of position.
	[Fact]
	void A_request_with_two_required_Result_members_and_a_trailing_complex_member_writes_every_member_when_all_succeed()
	{
		var compiled = CompiledFixture.Build(TruncationFixture);
		var shape = compiled.Shape("TruncationRequest");

		var request = compiled.CreateInstance("Norse.Fixtures.WriterTruncation.TruncationRequest",
			("First", new Result<string>(new Success<string>("first-value"))),
			("Second", new Result<int>(new Success<int>(42))),
			("Nested", compiled.CreateInstance("Norse.Fixtures.WriterTruncation.TruncationNested",
				("Value", new Result<string>(new Success<string>("nested-value"))))));

		WriteFragment(shape, request, WireCaseStyle.SnakeCase)
			.ShouldBe(
				"""<truncation_request first="first-value" second="42"><truncation_nested value="nested-value" /></truncation_request>""");
	}

	[Fact]
	void When_only_the_second_required_Result_member_is_illegal_only_its_own_check_throws()
	{
		var compiled = CompiledFixture.Build(TruncationFixture);
		var shape = compiled.Shape("TruncationRequest");

		var request = compiled.CreateInstance("Norse.Fixtures.WriterTruncation.TruncationRequest",
			("First", new Result<string>(new Success<string>("first-value"))),
			("Second", new Result<int>(new Failure(ParseFailure.Malformed, "nope", nameof(Int32)))),
			("Nested", compiled.CreateInstance("Norse.Fixtures.WriterTruncation.TruncationNested",
				("Value", new Result<string>(new Success<string>("also-never-reached"))))));

		var exception =
			Should.Throw<InvalidOperationException>(() => WriteFragment(shape, request, WireCaseStyle.SnakeCase));

		exception.Message.ShouldBe(IllegalWriteMessage);
	}

	[Fact]
	void A_raw_required_response_scalar_writes_via_invariant_ToString_and_a_null_optional_omits()
	{
		var compiled = CompiledFixture.Build(ResponseFixture);
		var shape = compiled.Shape("PingResponse");

		var withoutNote = compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200));
		WriteRoot(shape, withoutNote, WireCaseStyle.SnakeCase)
			.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><ping_response code="200" />""");

		var withNote =
			compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200), ("Note", "ok"));
		WriteRoot(shape, withNote, WireCaseStyle.SnakeCase)
			.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><ping_response code="200" note="ok" />""");
	}

	[Fact]
	void A_nullable_complex_member_omits_its_element_when_null_and_writes_it_when_present()
	{
		var compiled = CompiledFixture.Build(ResponseFixture);
		var shape = compiled.Shape("PingResponse");

		var withoutDetail = compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200));
		WriteRoot(shape, withoutDetail, WireCaseStyle.SnakeCase)
			.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><ping_response code="200" />""");

		var extra = compiled.CreateInstance("Norse.Fixtures.WriterResponse.Extra", ("Note", "hi"));
		var withDetail = compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200),
			("Detail", extra));
		WriteRoot(shape, withDetail, WireCaseStyle.SnakeCase)
			.ShouldBe(
				"""<?xml version="1.0" encoding="utf-8"?><ping_response code="200"><extra note="hi" /></ping_response>""");
	}

	[Fact]
	void A_raw_collection_member_writes_zero_children_when_empty_and_one_per_item_when_populated()
	{
		// PingResponse's scalar/nested-complex members are all Result-free (response-side law), so this
		// is the surviving coverage of the general collection-writing mechanic — empty foreach writes
		// nothing, a populated list writes one child element per item, in order — now that QuoteRequest's
		// own Coverages list can never write a single byte (its item type, CoverageLine, is Result-wrapped
		// and always throws).
		var compiled = CompiledFixture.Build(ResponseFixture);
		var shape = compiled.Shape("PingResponse");

		var withoutTags = compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200));
		WriteRoot(shape, withoutTags, WireCaseStyle.SnakeCase)
			.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><ping_response code="200" />""");

		var tagA = compiled.CreateInstance("Norse.Fixtures.WriterResponse.Tag", ("Name", "a"));
		var tagB = compiled.CreateInstance("Norse.Fixtures.WriterResponse.Tag", ("Name", "b"));
		var tags = compiled.CreateList("Norse.Fixtures.WriterResponse.Tag", tagA, tagB);
		var withTags =
			compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200), ("Tags", tags));

		// Fragment form here also keeps "a fragment carries no XML declaration" covered — the only prior
		// test of that fact (QuoteFixture's nested CoverageLine) is gone now that Result<T> can never
		// write, and this member (Tag.Name) is raw, so it can actually reach WriteFragment successfully.
		WriteFragment(shape, withTags, WireCaseStyle.SnakeCase)
			.ShouldBe("""<ping_response code="200"><tag name="a" /><tag name="b" /></ping_response>""");
	}

	[Fact]
	void A_complex_type_reachable_from_two_different_controllers_emits_exactly_one_shape_class()
	{
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new XmlShapeGenerator().AsSourceGenerator()],
			parseOptions: GeneratorTestHarness.ParseOptions);
		driver = driver.RunGeneratorsAndUpdateCompilation(GeneratorTestHarness.CreateCompilation(SharedTypeFixture),
			out var outputCompilation, out var diagnostics, TestContext.Current.CancellationToken);

		diagnostics.ShouldBeEmpty();

		var generatedSources = driver.GetRunResult().Results.Single().GeneratedSources;
		generatedSources.Count(s => s.HintName == "SharedAddressXmlShape.g.cs").ShouldBe(1);
		generatedSources.Count(s => s.HintName == "RequestAXmlShape.g.cs").ShouldBe(1);
		generatedSources.Count(s => s.HintName == "RequestBXmlShape.g.cs").ShouldBe(1);

		using MemoryStream stream = new();
		var emitResult = outputCompilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
		emitResult.Success.ShouldBeTrue(string.Join("\n", emitResult.Diagnostics));
	}

	[Fact]
	void An_undecorated_property_never_enters_the_closure_and_leaves_no_trace_in_generated_output()
	{
		// Shadow carries no [DataMember] — the opt-in membership law (design spec §4b) means it does
		// not exist to Futhark at all: no closure entry, no shape member, no diagnostic, and no trace
		// of it anywhere in the generated shape's own source, in any casing the writer could have
		// chosen for it had it been a real member.
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new XmlShapeGenerator().AsSourceGenerator()],
			parseOptions: GeneratorTestHarness.ParseOptions);
		driver = driver.RunGeneratorsAndUpdateCompilation(
			GeneratorTestHarness.CreateCompilation(DataMemberFilterFixture), out _, out var diagnostics,
			TestContext.Current.CancellationToken);

		diagnostics.ShouldBeEmpty();

		var generatedSources = driver.GetRunResult().Results.Single().GeneratedSources;
		var requestShapeSource = generatedSources.Single(s => s.HintName == "FilterRequestXmlShape.g.cs").SourceText
			.ToString();

		foreach (var casing in new[] { "shadow", "Shadow", "SHADOW" })
			requestShapeSource.ShouldNotContain(casing);
	}

	static string WriteRoot(IXmlShape shape, object value, WireCaseStyle style)
	{
		using MemoryStream stream = new();
		var settings = new XmlWriterSettings
		{
			Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			OmitXmlDeclaration = false,
			Indent = false
		};
		using (var writer = XmlWriter.Create(stream, settings))
			shape.WriteObject(writer, value, style);

		return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(stream.ToArray());
	}

	static string WriteFragment(IXmlShape shape, object value, WireCaseStyle style)
	{
		StringBuilder sb = new();
		var settings = new XmlWriterSettings { ConformanceLevel = ConformanceLevel.Fragment, Indent = false };
		using (var writer = XmlWriter.Create(sb, settings))
			shape.WriteObject(writer, value, style);

		return sb.ToString();
	}

	/// <summary>
	///     Compiles a fixture through the real <see cref="XmlShapeGenerator" />, emits the result to an
	///     in-memory assembly, and loads it — the "instantiate the generated shape via the compilation"
	///     bar from the brief. Shape classes and fixture contract types alike are only known by name at
	///     this level (they don't exist at this test project's own compile time); <see cref="IXmlShape" />,
	///     <c>Result&lt;T&gt;</c>, and <c>Success&lt;T&gt;</c> ARE compile-time known here, because the
	///     loaded fixture assembly references the exact same physical <c>Infrastructure.Web.Server.dll</c>/
	///     <c>Norse.Primitives.dll</c> this test project itself references — same assembly identity, same
	///     runtime <see cref="Type" />, no reflection needed to cross that boundary.
	/// </summary>
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

		public object CreateInstance(string fullyQualifiedTypeName, params (string Property, object? Value)[] values)
		{
			var type = ResolveType(fullyQualifiedTypeName);
			var instance = Activator.CreateInstance(type)!;
			foreach (var (property, value) in values)
			{
				var propertyInfo = type.GetProperty(property) ??
					throw new InvalidOperationException(
						$"Property '{property}' was not found on '{fullyQualifiedTypeName}'.");
				propertyInfo.SetValue(instance, value);
			}

			return instance;
		}

		/// <summary>
		///     Builds a <c>Result&lt;TEnum&gt;</c> success case for a fixture-local enum only known by name at
		///     this test project's own compile time — <c>Result&lt;T&gt;.op_Implicit</c> is the only public
		///     surface that constructs a success case without a compile-time <c>T</c>, so this reflects that
		///     operator rather than the <c>Success&lt;T&gt;</c> constructor directly.
		/// </summary>
		public object CreateEnumSuccess(string enumFullyQualifiedName, string memberName) =>
			InvokeImplicitResult(ResolveType(enumFullyQualifiedName),
				Enum.Parse(ResolveType(enumFullyQualifiedName), memberName));

		/// <summary>
		///     Same as the named-member overload, for an underlying integral value that may not name any defined member (e.g.
		///     an undefined-value write test).
		/// </summary>
		public object CreateEnumSuccess(string enumFullyQualifiedName, int underlyingValue) =>
			InvokeImplicitResult(ResolveType(enumFullyQualifiedName),
				Enum.ToObject(ResolveType(enumFullyQualifiedName), underlyingValue));

		/// <summary>
		///     Builds a <c>Result&lt;TEnum&gt;</c> failure case for a fixture-local enum via <c>Result&lt;T&gt;</c>'s
		///     <c>Failure</c>-typed constructor.
		/// </summary>
		public object CreateEnumFailure(string enumFullyQualifiedName)
		{
			var enumType = ResolveType(enumFullyQualifiedName);
			var resultType = typeof(Result<>).MakeGenericType(enumType);
			var failure = new Failure(ParseFailure.Malformed, "nope", enumType.Name);
			return Activator.CreateInstance(resultType, failure)!;
		}

		/// <summary>
		///     Builds <c>default(Result&lt;TEnum&gt;)</c> for a fixture-local enum — the union's own defaulted (neither-case)
		///     state.
		/// </summary>
		public object CreateEnumDefault(string enumFullyQualifiedName)
		{
			var resultType = typeof(Result<>).MakeGenericType(ResolveType(enumFullyQualifiedName));
			return Activator.CreateInstance(resultType)!;
		}

		static object InvokeImplicitResult(Type enumType, object enumValue)
		{
			var resultType = typeof(Result<>).MakeGenericType(enumType);
			var implicitOperator = resultType.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, null,
					[enumType], null)
				?? throw new InvalidOperationException(
					$"'{resultType}' has no implicit conversion operator from '{enumType}'.");
			return implicitOperator.Invoke(null, [enumValue])!;
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
	}
}
