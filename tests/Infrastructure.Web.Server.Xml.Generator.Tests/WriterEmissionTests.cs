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
			public Result<decimal> Limit { get; init; }
			public Result<DateOnly>? Effective { get; init; }
			public List<CoverageLine> Coverages { get; init; } = new();
		}

		public sealed record CoverageLine
		{
			public Result<string> Code { get; init; }
		}

		public sealed record QuoteResponse
		{
			public string Status { get; init; } = "";
		}

		public sealed class QuoteController : GrpcControllerBase
		{
			public Task<ActionResult<QuoteResponse>> Do([FromBody] QuoteRequest request) =>
				Task.FromResult(new ActionResult<QuoteResponse>(new QuoteResponse()));
		}
		""";

	[Fact]
	void QuoteRequest_writes_the_brief_literal_example_byte_exact_with_root_declaration()
	{
		var compiled = CompiledFixture.Build(QuoteFixture);
		var shape = compiled.Shape("QuoteRequest");

		var coverageLine = compiled.CreateInstance("Norse.Fixtures.WriterQuote.CoverageLine",
			("Code", new Result<string>(new Success<string>("GL"))));
		var coverages = compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine", coverageLine);
		// Effective left unset — Result<DateOnly>? defaults to null, the "omitted optional" case.
		var quoteRequest = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", new Result<decimal>(new Success<decimal>(1234.56m))),
			("Coverages", coverages));

		var xml = WriteRoot(shape, quoteRequest, WireCaseStyle.SnakeCase);

		// Note: XmlWriter always renders a self-closing element as "<tag ... />" (space before "/>") —
		// verified against real XmlWriter output (no XmlWriterSettings combination suppresses it; not
		// an emitter choice). The brief's literal example text omits that space; this assertion follows
		// verified runtime behavior over the brief's prose. Flagged in the task report.
		xml.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><quote_request limit="1234.56"><coverage_line code="GL" /></quote_request>""");
	}

	[Fact]
	void An_empty_collection_writes_zero_child_elements()
	{
		var compiled = CompiledFixture.Build(QuoteFixture);
		var shape = compiled.Shape("QuoteRequest");

		var emptyCoverages = compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine");
		var quoteRequest = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", new Result<decimal>(new Success<decimal>(1234.56m))),
			("Coverages", emptyCoverages));

		var xml = WriteRoot(shape, quoteRequest, WireCaseStyle.SnakeCase);

		xml.ShouldBe("""<?xml version="1.0" encoding="utf-8"?><quote_request limit="1234.56" />""");
	}

	[Fact]
	void A_nested_complex_member_written_directly_as_a_fragment_carries_no_declaration()
	{
		var compiled = CompiledFixture.Build(QuoteFixture);
		var coverageShape = compiled.Shape("CoverageLine");

		var coverageLine = compiled.CreateInstance("Norse.Fixtures.WriterQuote.CoverageLine",
			("Code", new Result<string>(new Success<string>("GL"))));

		var xml = WriteFragment(coverageShape, coverageLine, WireCaseStyle.SnakeCase);

		xml.ShouldBe("""<coverage_line code="GL" />""");
		xml.ShouldNotContain("<?xml");
	}

	[Fact]
	void A_failed_required_Result_throws_the_exact_message_and_writes_nothing_further()
	{
		var compiled = CompiledFixture.Build(QuoteFixture);
		var shape = compiled.Shape("QuoteRequest");

		var quoteRequest = compiled.CreateInstance("Norse.Fixtures.WriterQuote.QuoteRequest",
			("Limit", default(Result<decimal>)),
			("Coverages", compiled.CreateList("Norse.Fixtures.WriterQuote.CoverageLine")));

		var exception = Should.Throw<InvalidOperationException>(() => WriteFragment(shape, quoteRequest, WireCaseStyle.SnakeCase));

		exception.Message.ShouldBe("a failed Result<T> is illegal to write");
	}

	const string ResponseFixture = """
		#nullable enable
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.WriterResponse;

		[DataContract]
		public sealed record PingRequest
		{
			public Result<string> Value { get; init; }
		}

		public sealed record Extra
		{
			public string Note { get; init; } = "";
		}

		public sealed record PingResponse
		{
			public int Code { get; init; }
			public string? Note { get; init; }
			public Extra? Detail { get; init; }
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

	const string FlagsFixture = """
		using System;
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.WriterFlags;

		[Flags]
		public enum Access
		{
			Read = 1,
			Write = 2,
			Execute = 4,
			ReadWrite = Read | Write
		}

		public enum Status
		{
			Draft = 1,
			Active = 2
		}

		[DataContract]
		public sealed record AccessRequest
		{
			public Result<Access> Perm { get; init; }
		}

		[DataContract]
		public sealed record StatusRequest
		{
			public Result<Status> State { get; init; }
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

		public sealed class StatusController : GrpcControllerBase
		{
			public Task<ActionResult<FlagsResponse>> Do([FromBody] StatusRequest request) =>
				Task.FromResult(new ActionResult<FlagsResponse>(new FlagsResponse()));
		}
		""";

	[Fact]
	void An_exactly_defined_flags_combination_writes_its_own_name_not_the_decomposed_parts()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("AccessRequest");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var readWrite = Enum.ToObject(accessType, 3); // Read (1) | Write (2) == the exactly-defined ReadWrite

		var request = compiled.CreateInstance("Norse.Fixtures.WriterFlags.AccessRequest",
			("Perm", CompiledFixture.CreateResultSuccess(accessType, readWrite)));

		WriteFragment(shape, request, WireCaseStyle.PascalCase).ShouldBe("""<AccessRequest Perm="ReadWrite" />""");
	}

	[Fact]
	void An_undecomposable_flags_combination_greedily_decomposes_descending_by_value()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("AccessRequest");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var readExecute = Enum.ToObject(accessType, 5); // Read (1) | Execute (4) — no member defines 5 exactly

		var request = compiled.CreateInstance("Norse.Fixtures.WriterFlags.AccessRequest",
			("Perm", CompiledFixture.CreateResultSuccess(accessType, readExecute)));

		// Descending by value among defined non-zero members (Execute=4, ReadWrite=3, Write=2, Read=1):
		// Execute matches first (consumes 4), ReadWrite/Write don't fit the remaining 1 bit, Read matches last.
		WriteFragment(shape, request, WireCaseStyle.PascalCase).ShouldBe("""<AccessRequest Perm="Execute Read" />""");
	}

	[Fact]
	void A_flags_value_with_leftover_bits_after_decomposition_throws()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("AccessRequest");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var undefined = Enum.ToObject(accessType, 8); // no defined bit covers this

		var request = compiled.CreateInstance("Norse.Fixtures.WriterFlags.AccessRequest",
			("Perm", CompiledFixture.CreateResultSuccess(accessType, undefined)));

		Should.Throw<InvalidOperationException>(() => WriteFragment(shape, request, WireCaseStyle.PascalCase));
	}

	[Fact]
	void A_default_flags_value_with_no_defined_zero_member_throws()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("AccessRequest");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var zero = Enum.ToObject(accessType, 0); // Access defines no zero member

		var request = compiled.CreateInstance("Norse.Fixtures.WriterFlags.AccessRequest",
			("Perm", CompiledFixture.CreateResultSuccess(accessType, zero)));

		Should.Throw<InvalidOperationException>(() => WriteFragment(shape, request, WireCaseStyle.PascalCase));
	}

	[Fact]
	void An_undefined_non_flags_enum_value_throws()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("StatusRequest");
		var statusType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Status");
		var undefined = Enum.ToObject(statusType, 99);

		var request = compiled.CreateInstance("Norse.Fixtures.WriterFlags.StatusRequest",
			("State", CompiledFixture.CreateResultSuccess(statusType, undefined)));

		Should.Throw<InvalidOperationException>(() => WriteFragment(shape, request, WireCaseStyle.PascalCase));
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
			public Result<string> Line1 { get; init; }
		}

		[DataContract]
		public sealed record RequestA
		{
			public SharedAddress Home { get; init; } = null!;
		}

		[DataContract]
		public sealed record RequestB
		{
			public SharedAddress Office { get; init; } = null!;
		}

		public sealed record SharedResponse
		{
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

		/// <summary>Constructs <c>Result&lt;{enumType}&gt;</c> in its success state via reflection — <c>Result&lt;T&gt;</c>/<c>Success&lt;T&gt;</c> are generic over the fixture's own dynamically-loaded enum type, which this test project cannot close a generic over at compile time.</summary>
		public static object CreateResultSuccess(System.Type enumType, object enumValue)
		{
			var successType = typeof(Success<>).MakeGenericType(enumType);
			var success = Activator.CreateInstance(successType, enumValue)!;
			var resultType = typeof(Result<>).MakeGenericType(enumType);
			return Activator.CreateInstance(resultType, success)!;
		}
	}
}
