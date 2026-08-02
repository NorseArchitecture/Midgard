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
			public Result<string> Value { get; init; }
		}

		public sealed record Extra
		{
			public string Note { get; init; } = "";
		}

		public sealed record Tag
		{
			public string Name { get; init; } = "";
		}

		public sealed record PingResponse
		{
			public int Code { get; init; }
			public string? Note { get; init; }
			public Extra? Detail { get; init; }
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
			public Access Perm { get; init; }
			public Status State { get; init; }
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

	// AccessRequest.Perm/StatusRequest.State are Result<T>-wrapped (request-side shape law) and so can
	// never write, whatever value they carry — the enum-specific formatting/decomposition logic below
	// is therefore only reachable through a raw (non-Result) enum member, which shape law requires to
	// live on the response side instead: FlagsResponse.Perm/State.

	[Fact]
	void Writing_a_Result_wrapped_enum_member_throws_the_same_way_any_other_scalar_does()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("AccessRequest");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var readWrite = Enum.ToObject(accessType, 3); // Read (1) | Write (2) == the exactly-defined ReadWrite — even a perfectly valid, defined value still throws.

		var request = compiled.CreateInstance("Norse.Fixtures.WriterFlags.AccessRequest",
			("Perm", CompiledFixture.CreateResultSuccess(accessType, readWrite)));

		var exception = Should.Throw<InvalidOperationException>(() => WriteFragment(shape, request, WireCaseStyle.PascalCase));

		exception.Message.ShouldBe(DeserializationOnlyMessage);
	}

	[Fact]
	void An_exactly_defined_flags_combination_writes_its_own_name_not_the_decomposed_parts()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("FlagsResponse");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var statusType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Status");
		var readWrite = Enum.ToObject(accessType, 3); // Read (1) | Write (2) == the exactly-defined ReadWrite
		var draft = Enum.ToObject(statusType, 1); // State must carry a defined value too, or it throws first.

		var response = compiled.CreateInstance("Norse.Fixtures.WriterFlags.FlagsResponse", ("Perm", readWrite), ("State", draft));

		WriteFragment(shape, response, WireCaseStyle.PascalCase).ShouldBe("""<FlagsResponse Ok="" Perm="ReadWrite" State="Draft" />""");
	}

	[Fact]
	void An_undecomposable_flags_combination_greedily_decomposes_descending_by_value()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("FlagsResponse");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var statusType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Status");
		var readExecute = Enum.ToObject(accessType, 5); // Read (1) | Execute (4) — no member defines 5 exactly
		var draft = Enum.ToObject(statusType, 1); // State must carry a defined value too, or it throws first.

		var response = compiled.CreateInstance("Norse.Fixtures.WriterFlags.FlagsResponse", ("Perm", readExecute), ("State", draft));

		// Descending by value among defined non-zero members (Execute=4, ReadWrite=3, Write=2, Read=1):
		// Execute matches first (consumes 4), ReadWrite/Write don't fit the remaining 1 bit, Read matches last.
		WriteFragment(shape, response, WireCaseStyle.PascalCase).ShouldBe("""<FlagsResponse Ok="" Perm="Execute Read" State="Draft" />""");
	}

	[Fact]
	void A_flags_value_with_leftover_bits_after_decomposition_throws()
	{
		// Perm writes before State in declaration order, so an undefined Perm throws before State's own
		// (also-unset, also-undefined) default value is ever reached — no need to give State a valid
		// value here the way the two successful-write tests above do.
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("FlagsResponse");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var undefined = Enum.ToObject(accessType, 8); // no defined bit covers this

		var response = compiled.CreateInstance("Norse.Fixtures.WriterFlags.FlagsResponse", ("Perm", undefined));

		Should.Throw<InvalidOperationException>(() => WriteFragment(shape, response, WireCaseStyle.PascalCase));
	}

	[Fact]
	void A_default_flags_value_with_no_defined_zero_member_throws()
	{
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("FlagsResponse");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var zero = Enum.ToObject(accessType, 0); // Access defines no zero member

		var response = compiled.CreateInstance("Norse.Fixtures.WriterFlags.FlagsResponse", ("Perm", zero));

		Should.Throw<InvalidOperationException>(() => WriteFragment(shape, response, WireCaseStyle.PascalCase));
	}

	[Fact]
	void An_undefined_non_flags_enum_value_throws()
	{
		// Perm must carry a defined value here, or it throws first (declaration order) for the wrong
		// reason — this test is specifically about State's own undefined-value throw.
		var compiled = CompiledFixture.Build(FlagsFixture);
		var shape = compiled.Shape("FlagsResponse");
		var accessType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Access");
		var statusType = compiled.ResolveType("Norse.Fixtures.WriterFlags.Status");
		var read = Enum.ToObject(accessType, 1);
		var undefined = Enum.ToObject(statusType, 99);

		var response = compiled.CreateInstance("Norse.Fixtures.WriterFlags.FlagsResponse", ("Perm", read), ("State", undefined));

		Should.Throw<InvalidOperationException>(() => WriteFragment(shape, response, WireCaseStyle.PascalCase));
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
