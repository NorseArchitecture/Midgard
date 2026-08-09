using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Generator.Tests.Xml;

/// <summary>
///     Proves the reference-closure widening (spec amendment 2026-08-09): <c>GrpcControllerBase</c>
///     descendants compiled into a referenced realm assembly are discovered by metadata, their closures
///     walked symbolically, and their shapes emitted into the host compilation — including a host with no
///     source controllers at all, and a host whose own source controller shares a contract type with the
///     referenced controller (each shape emitted exactly once).
/// </summary>
public sealed class ReferencedAssemblyDiscoveryTests
{
	const string RealmFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.ReferencedRealm;

		[DataContract]
		public sealed record RealmRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record RealmResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class RealmController : GrpcControllerBase
		{
			public Task<ActionResult<RealmResponse>> Do([FromBody] RealmRequest request) =>
				Task.FromResult(new ActionResult<RealmResponse>(new RealmResponse()));
		}
		""";

	// The host's own source controller deliberately takes the REFERENCED assembly's RealmRequest as its
	// body type — the same contract type then arrives from both discovery branches (syntax and metadata),
	// so the exactly-once assertion below exercises the merge point, not just two disjoint closures.
	const string HostControllerFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Fixtures.ReferencedRealm;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.ReferencedHost;

		public sealed record HostResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class HostController : GrpcControllerBase
		{
			public Task<ActionResult<HostResponse>> Do([FromBody] RealmRequest request) =>
				Task.FromResult(new ActionResult<HostResponse>(new HostResponse()));
		}
		""";

	// A referenced realm whose facade closure violates shape law: Map is a dictionary member (NORSE027,
	// "dictionaries have no Futhark shape"). The realm assembly itself compiles clean — the generator
	// never ran over it — so the violation can only surface when the HOST's generator walks the
	// referenced closure through metadata symbols.
	const string LawlessRealmFixture = """
		using System.Collections.Generic;
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.LawlessRealm;

		[DataContract]
		public sealed record LawlessRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
			[DataMember]
			public Dictionary<string, string> Map { get; init; } = new();
		}

		public sealed record LawlessResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class LawlessController : GrpcControllerBase
		{
			public Task<ActionResult<LawlessResponse>> Do([FromBody] LawlessRequest request) =>
				Task.FromResult(new ActionResult<LawlessResponse>(new LawlessResponse()));
		}
		""";

	static MetadataReference BuildRealmReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.ReferencedRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference], RealmFixture);

	[Fact]
	void A_referenced_assembly_controller_emits_its_shapes_and_registration_into_a_controllerless_host()
	{
		var (diagnostics, outputCompilation) =
			GeneratorTestHarness.RunWithReferences([BuildRealmReference()]);

		diagnostics.ShouldBeEmpty();

		var generated = GeneratedFileNames(outputCompilation);
		generated.ShouldContain("RealmRequestXmlShape.g.cs");
		generated.ShouldContain("RealmResponseXmlShape.g.cs");

		// The registration summary lists the referenced-assembly shapes — the host's unconditional
		// AddNorseXml(style, NorseXmlShapeRegistration.Build()) call resolves them for real.
		GeneratedSource(outputCompilation, "NorseXmlShapeRegistration.g.cs")
			.ShouldContain("RealmRequestXmlShape");

		// "Still emit into the host compilation": the emitted shape classes compile clean against the
		// referenced contract types.
		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(static d => d.Severity == DiagnosticSeverity.Error)
			.ShouldBeEmpty();
	}

	[Fact]
	void A_source_controller_and_a_referenced_assembly_controller_emit_each_shape_exactly_once()
	{
		var (diagnostics, outputCompilation) =
			GeneratorTestHarness.RunWithReferences([BuildRealmReference()], HostControllerFixture);

		diagnostics.ShouldBeEmpty();

		var shapeFiles = GeneratedFileNames(outputCompilation)
			.Where(static name => name.EndsWith("XmlShape.g.cs", StringComparison.Ordinal))
			.ToList();
		shapeFiles.ShouldBe(
			["HostResponseXmlShape.g.cs", "RealmRequestXmlShape.g.cs", "RealmResponseXmlShape.g.cs"],
			ignoreOrder: true);

		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(static d => d.Severity == DiagnosticSeverity.Error)
			.ShouldBeEmpty();
	}

	[Fact]
	void A_referenced_assembly_closure_violating_shape_law_reports_the_diagnostic_with_no_source_location()
	{
		// The metadata-sourced walk runs the same ClosureWalker the syntax branch uses, so a violation in
		// a referenced closure still strikes NORSE022-028 — and, the symbol having no source location,
		// reports through ShapeModel's LocationInfo.FromSymbol fallback (LocationInfo.None, the zero-width
		// location at the empty path) rather than crashing on a null file path.
		var lawlessReference = GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.LawlessRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference], LawlessRealmFixture);

		var (diagnostics, _) = GeneratorTestHarness.RunWithReferences([lawlessReference]);

		var violation = diagnostics.ShouldHaveSingleItem();
		violation.Id.ShouldBe("NORSE027");
		violation.Location.IsInSource.ShouldBeFalse();
		violation.Location.GetLineSpan().Path.ShouldBeEmpty();
		violation.GetMessage(CultureInfo.InvariantCulture).ShouldContain(
			"Member 'Map' on 'global::Norse.Fixtures.LawlessRealm.LawlessRequest' is a dictionary");
	}

	static List<string> GeneratedFileNames(Compilation outputCompilation) =>
	[
		.. outputCompilation.SyntaxTrees
			.Where(static tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
			.Select(static tree => Path.GetFileName(tree.FilePath))
	];

	static string GeneratedSource(Compilation outputCompilation, string fileName) =>
		outputCompilation.SyntaxTrees
			.Single(tree => Path.GetFileName(tree.FilePath) == fileName)
			.ToString();
}
