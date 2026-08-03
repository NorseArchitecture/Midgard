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
/// Compiles a fixture contract set through the real generator, loads the emitted assembly, and
/// instantiates the generated <c>{Contract}XmlShape</c> classes to assert canonical writer output
/// byte-exact (design spec §6) — the brief's literal <c>QuoteRequest</c> example, flags-canonical
/// forms, and the failed-<c>Result&lt;T&gt;</c> throw, among others.
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

		[DataContract]
		public sealed record QuoteRequest
		{
			[DataMember]
			public Result<decimal> Limit { get; init; }
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

	// Result<T> is a deserialization-only type — Write always throws, for every state, success
	// included. Matches the JSON leg's ResultJsonConverter<T> and the gRPC leg's ResultSerializer<T>
	// wording exactly: one platform law, one message, regardless of channel.
	const string DeserializationOnlyMessage = "Result<T> is a deserialization-only type and must never be written";

	[Theory]
	[MemberData(nameof(RequiredResultStates))]
	void Writing_any_state_of_a_required_Result_wrapped_member_throws_and_writes_nothing_further(string label, Result<decimal> limit)
	{
		// QuoteRequest's only scalar member (Limit) is Result<decimal>-wrapped and required — every
		// possible state throws before a single byte is written, so the brief's literal "writes the
		// clean value" example no longer has a legal outbound form. Coverages left empty: whatever
		// state Coverages' own CoverageLine.Code carried would be moot — Limit throws first (attributes
		// write before children in declaration order).
		var compiled = CompiledFixture.Build(QuoteFixture);
		var shape = compiled.Shape("QuoteRequest");

		var quoteRequest = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", limit),
			("Coverages", compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine")));

		var exception = Should.Throw<InvalidOperationException>(() => WriteFragment(shape, quoteRequest, WireCaseStyle.SnakeCase));

		exception.Message.ShouldBe(DeserializationOnlyMessage, label);
	}

	public static TheoryData<string, Result<decimal>> RequiredResultStates() => new()
	{
		{ "success", new Success<decimal>(1234.56m) },
		{ "failure", new Failure(ParseFailure.Malformed, "nope", nameof(Decimal)) },
		{ "default", default },
	};

	[Fact]
	void A_nested_Result_wrapped_member_throws_the_same_way_a_root_level_one_does()
	{
		// CoverageLine's only member (Code) is Result<string>-wrapped — proves the law applies uniformly
		// at any nesting depth, not just at the root the theory above already covers.
		var compiled = CompiledFixture.Build(QuoteFixture);
		var coverageShape = compiled.Shape("CoverageLine");

		var coverageLine = compiled.CreateInstance("Norse.Fixtures.WriterQuote.CoverageLine",
			("Code", new Result<string>(new Success<string>("GL"))));

		var exception = Should.Throw<InvalidOperationException>(() => WriteFragment(coverageShape, coverageLine, WireCaseStyle.SnakeCase));

		exception.Message.ShouldBe(DeserializationOnlyMessage);
	}

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

	// Regression for the CS0162 bug (design spec review, Task 13): TruncationRequestXmlShape.g.cs's
	// Write method has TWO required Result<T>-wrapped scalars (First, Second) in declaration order,
	// plus a required complex member (Nested) after them. Before the fix, WriterEmitter kept emitting
	// Second's throw, Nested's write, and the closing WriteEndElement — all genuinely unreachable the
	// moment First's unconditional throw fires, and a strict compilation (GeneratorTestHarness now
	// mirrors real-project TreatWarningsAsErrors) correctly refused to build it. CompiledFixture.Build's
	// own `emitResult.Success.ShouldBeTrue()` — running through the harness's strict
	// CSharpCompilationOptions — IS this test's regression assertion: this fixture would not have
	// compiled before the fix. What follows additionally proves only the FIRST required member's throw
	// is ever reached, whatever state the later members carry.
	[Fact]
	void A_request_with_two_required_Result_members_and_a_trailing_complex_member_compiles_clean_and_only_the_first_member_throws()
	{
		var compiled = CompiledFixture.Build(TruncationFixture);
		var shape = compiled.Shape("TruncationRequest");

		var request = compiled.CreateInstance("Norse.Fixtures.WriterTruncation.TruncationRequest",
			("First", new Result<string>(new Success<string>("only-member-that-matters"))),
			("Second", new Result<int>(new Success<int>(42))),
			("Nested", compiled.CreateInstance("Norse.Fixtures.WriterTruncation.TruncationNested",
				("Value", new Result<string>(new Success<string>("also-never-reached"))))));

		var exception = Should.Throw<InvalidOperationException>(() => WriteFragment(shape, request, WireCaseStyle.SnakeCase));

		exception.Message.ShouldBe(DeserializationOnlyMessage);
	}

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

	[Fact]
	void A_raw_required_response_scalar_writes_via_invariant_ToString_and_a_null_optional_omits()
	{
		var compiled = CompiledFixture.Build(ResponseFixture);
		var shape = compiled.Shape("PingResponse");

		var withoutNote = compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200));
		WriteRoot(shape, withoutNote, WireCaseStyle.SnakeCase)
			.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><ping_response code="200" />""");

		var withNote = compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200), ("Note", "ok"));
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
		var withDetail = compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200), ("Detail", extra));
		WriteRoot(shape, withDetail, WireCaseStyle.SnakeCase)
			.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><ping_response code="200"><extra note="hi" /></ping_response>""");
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
		var withTags = compiled.CreateInstance("Norse.Fixtures.WriterResponse.PingResponse", ("Code", 200), ("Tags", tags));

		// Fragment form here also keeps "a fragment carries no XML declaration" covered — the only prior
		// test of that fact (QuoteFixture's nested CoverageLine) is gone now that Result<T> can never
		// write, and this member (Tag.Name) is raw, so it can actually reach WriteFragment successfully.
		WriteFragment(shape, withTags, WireCaseStyle.SnakeCase)
			.ShouldBe("""<ping_response code="200"><tag name="a" /><tag name="b" /></ping_response>""");
	}

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

	[Fact]
	void A_complex_type_reachable_from_two_different_controllers_emits_exactly_one_shape_class()
	{
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new XmlShapeGenerator().AsSourceGenerator()], parseOptions: GeneratorTestHarness.ParseOptions);
		driver = driver.RunGeneratorsAndUpdateCompilation(GeneratorTestHarness.CreateCompilation(SharedTypeFixture), out var outputCompilation, out var diagnostics, TestContext.Current.CancellationToken);

		diagnostics.ShouldBeEmpty();

		var generatedSources = driver.GetRunResult().Results.Single().GeneratedSources;
		generatedSources.Count(s => s.HintName == "SharedAddressXmlShape.g.cs").ShouldBe(1);
		generatedSources.Count(s => s.HintName == "RequestAXmlShape.g.cs").ShouldBe(1);
		generatedSources.Count(s => s.HintName == "RequestBXmlShape.g.cs").ShouldBe(1);

		using MemoryStream stream = new();
		var emitResult = outputCompilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
		emitResult.Success.ShouldBeTrue(string.Join("\n", emitResult.Diagnostics));
	}

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
	void An_undecorated_property_never_enters_the_closure_and_leaves_no_trace_in_generated_output()
	{
		// Shadow carries no [DataMember] — the opt-in membership law (design spec §4b) means it does
		// not exist to Futhark at all: no closure entry, no shape member, no diagnostic, and no trace
		// of it anywhere in the generated shape's own source, in any casing the writer could have
		// chosen for it had it been a real member.
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new XmlShapeGenerator().AsSourceGenerator()], parseOptions: GeneratorTestHarness.ParseOptions);
		driver = driver.RunGeneratorsAndUpdateCompilation(GeneratorTestHarness.CreateCompilation(DataMemberFilterFixture), out _, out var diagnostics, TestContext.Current.CancellationToken);

		diagnostics.ShouldBeEmpty();

		var generatedSources = driver.GetRunResult().Results.Single().GeneratedSources;
		var requestShapeSource = generatedSources.Single(s => s.HintName == "FilterRequestXmlShape.g.cs").SourceText.ToString();

		foreach (var casing in new[] { "shadow", "Shadow", "SHADOW" })
			requestShapeSource.ShouldNotContain(casing);
	}

	static string WriteRoot(IXmlShape shape, object value, WireCaseStyle style)
	{
		using MemoryStream stream = new();
		var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), OmitXmlDeclaration = false, Indent = false };
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
	/// Compiles a fixture through the real <see cref="XmlShapeGenerator"/>, emits the result to an
	/// in-memory assembly, and loads it — the "instantiate the generated shape via the compilation"
	/// bar from the brief. Shape classes and fixture contract types alike are only known by name at
	/// this level (they don't exist at this test project's own compile time); <see cref="IXmlShape"/>,
	/// <c>Result&lt;T&gt;</c>, and <c>Success&lt;T&gt;</c> ARE compile-time known here, because the
	/// loaded fixture assembly references the exact same physical <c>Infrastructure.Web.Server.dll</c>/
	/// <c>Norse.Primitives.dll</c> this test project itself references — same assembly identity, same
	/// runtime <see cref="Type"/>, no reflection needed to cross that boundary.
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
	}
}
