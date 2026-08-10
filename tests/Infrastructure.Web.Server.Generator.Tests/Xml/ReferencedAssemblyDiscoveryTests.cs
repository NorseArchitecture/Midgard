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

	// NORSE035: two independent realms whose facade contracts happen to share an unqualified type name —
	// trivially reachable now that reference-closure discovery merges independent realms. Both "Order"
	// records are legal Futhark shapes on their own; the law strikes because WriterEmitter.ShortName
	// collapses "global::Norse.Fixtures.ShortNameRealmA.Order" and "...ShortNameRealmB.Order" to the same
	// "Order" — the same string both the AddSource hint and the emitted class name derive from.
	const string ShortNameRealmAFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.ShortNameRealmA;

		[DataContract]
		public sealed record Order
		{
			[DataMember]
			public Result<string> Id { get; init; }
		}

		public sealed record RealmAResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class RealmAController : GrpcControllerBase
		{
			public Task<ActionResult<RealmAResponse>> Do([FromBody] Order request) =>
				Task.FromResult(new ActionResult<RealmAResponse>(new RealmAResponse()));
		}
		""";

	const string ShortNameRealmBFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.ShortNameRealmB;

		[DataContract]
		public sealed record Order
		{
			[DataMember]
			public Result<string> Id { get; init; }
		}

		public sealed record RealmBResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class RealmBController : GrpcControllerBase
		{
			public Task<ActionResult<RealmBResponse>> Do([FromBody] Order request) =>
				Task.FromResult(new ActionResult<RealmBResponse>(new RealmBResponse()));
		}
		""";

	// An internal facade controller in a referenced assembly, no InternalsVisibleTo grant to the host --
	// (4) DiscoverReferenced must not accept it by base type alone, or the emitted shape class's own
	// generated source (referencing InternalRequest/InternalResponse by name) fails CS0122 the moment the
	// host tries to compile it.
	const string InternalControllerRealmFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.InternalRealm;

		[DataContract]
		public sealed record InternalRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record InternalResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		internal sealed class InternalController : GrpcControllerBase
		{
			public Task<ActionResult<InternalResponse>> Do([FromBody] InternalRequest request) =>
				Task.FromResult(new ActionResult<InternalResponse>(new InternalResponse()));
		}
		""";

	// Same shape as InternalControllerRealmFixture, but the realm assembly grants InternalsVisibleTo to
	// the host compilation's own assembly name (GeneratorTestHarness.RunWithReferences always compiles the
	// host as "Norse.Hosting.Web.Server") -- IsSymbolAccessibleWithin honors that grant, so the controller
	// becomes legitimately reachable even though DeclaredAccessibility alone still reads Internal.
	const string IvtControllerRealmFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using System.Runtime.CompilerServices;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		[assembly: InternalsVisibleTo("Norse.Hosting.Web.Server")]

		namespace Norse.Fixtures.IvtRealm;

		[DataContract]
		public sealed record IvtRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record IvtResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		internal sealed class IvtController : GrpcControllerBase
		{
			public Task<ActionResult<IvtResponse>> Do([FromBody] IvtRequest request) =>
				Task.FromResult(new ActionResult<IvtResponse>(new IvtResponse()));
		}
		""";

	// (5) ContractDiscovery.AllTypes recurses GetTypeMembers() too, not just namespaces -- so a controller
	// nested inside a public static container type is still FOUND by the metadata walk, not silently
	// invisible to it. That recursion stays (it serves gRPC contract and component discovery); what
	// changed is what ClosureWalker.Analyze does once it sees a nested controller symbol: ruled by Buvy
	// 2026-08-09, facade controllers are namespace-level types, so this fixture now proves NORSE037
	// strikes instead of the closure being walked.
	const string NestedControllerRealmFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.NestedRealm;

		[DataContract]
		public sealed record NestedRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record NestedResponse
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public static class Container
		{
			public sealed class NestedController : GrpcControllerBase
			{
				public Task<ActionResult<NestedResponse>> Do([FromBody] NestedRequest request) =>
					Task.FromResult(new ActionResult<NestedResponse>(new NestedResponse()));
			}
		}
		""";

	// NORSE036: the referenced contract's parameterless constructor is internal, with no InternalsVisibleTo
	// grant to the host — the generated reader would compile "new BadResponse { ... }" in the host
	// assembly, which fails CS0122 the moment that constructor turns out unreachable from there.
	const string InternalCtorRealmFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.InternalCtorRealm;

		[DataContract]
		public sealed record GoodRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record BadResponse
		{
			internal BadResponse()
			{
			}

			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class BadController : GrpcControllerBase
		{
			public Task<ActionResult<BadResponse>> Do([FromBody] GoodRequest request) =>
				Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
		}
		""";

	// NORSE036: the referenced contract's parameterless constructor is host-reachable, but the wire
	// member's own init accessor is internal — same CS0272 failure mode, one level lower (the object
	// initializer's "Member = ..." clause, not the "new BadResponse" call itself).
	const string InternalInitAccessorRealmFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.InternalInitAccessorRealm;

		[DataContract]
		public sealed record GoodRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record BadResponse
		{
			[DataMember]
			public string Status { get; internal init; } = "";
		}

		public sealed class BadController : GrpcControllerBase
		{
			public Task<ActionResult<BadResponse>> Do([FromBody] GoodRequest request) =>
				Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
		}
		""";

	static MetadataReference BuildRealmReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.ReferencedRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference], RealmFixture);

	static MetadataReference BuildInternalCtorRealmReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.InternalCtorRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference],
			InternalCtorRealmFixture);

	static MetadataReference BuildInternalInitAccessorRealmReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.InternalInitAccessorRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference],
			InternalInitAccessorRealmFixture);

	// NORSE036: the ambient compilation's default MetadataImportOptions.Public elides internal AND
	// private referenced-assembly members alike when the host carries no InternalsVisibleTo grant --
	// MetadataImportOptions.Internal alone (an earlier pass of this fix) restores internal visibility but
	// still elides private, so a `private init` accessor would slip through exactly like the internal one
	// used to. Brackets the accessor-elision fix against the accessibility level ONE STEP more restrictive
	// than the internal case already covered above.
	const string PrivateInitAccessorRealmFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.PrivateInitAccessorRealm;

		[DataContract]
		public sealed record GoodRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record BadResponse
		{
			[DataMember]
			public string Status { get; private init; } = "";
		}

		public sealed class BadController : GrpcControllerBase
		{
			public Task<ActionResult<BadResponse>> Do([FromBody] GoodRequest request) =>
				Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
		}
		""";

	static MetadataReference BuildPrivateInitAccessorRealmReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.PrivateInitAccessorRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference],
			PrivateInitAccessorRealmFixture);

	// NORSE036 negative control, MINOR-5: proves MetadataImportOptions.All -- which makes every
	// accessibility level visible to the symbol table, private included -- does not itself grant access.
	// Visibility and accessibility are separate questions; IsSymbolAccessibleWithin still evaluates the
	// latter correctly against the elevated symbols, so an internal construction surface WITH a matching
	// InternalsVisibleTo grant (mirroring IvtControllerRealmFixture's controller-level proof, one level
	// down at the contract's own constructor/accessor) stays clean end to end.
	const string IvtConstructionRealmFixture = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using System.Runtime.CompilerServices;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		[assembly: InternalsVisibleTo("Norse.Hosting.Web.Server")]

		namespace Norse.Fixtures.IvtConstructionRealm;

		[DataContract]
		public sealed record GoodRequest
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record GoodResponse
		{
			internal GoodResponse()
			{
			}

			[DataMember]
			public string Status { get; internal init; } = "";
		}

		public sealed class GoodController : GrpcControllerBase
		{
			public Task<ActionResult<GoodResponse>> Do([FromBody] GoodRequest request) =>
				Task.FromResult(new ActionResult<GoodResponse>(new GoodResponse()));
		}
		""";

	static MetadataReference BuildIvtConstructionRealmReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.IvtConstructionRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference],
			IvtConstructionRealmFixture);

	static MetadataReference BuildInternalControllerRealmReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.InternalRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference],
			InternalControllerRealmFixture);

	static MetadataReference BuildIvtControllerRealmReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.IvtRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference],
			IvtControllerRealmFixture);

	static MetadataReference BuildNestedControllerRealmReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.NestedRealm",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference],
			NestedControllerRealmFixture);

	static MetadataReference BuildShortNameRealmAReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.ShortNameRealmA",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference], ShortNameRealmAFixture);

	static MetadataReference BuildShortNameRealmBReference() =>
		GeneratorTestHarness.EmitToMetadataReference("Norse.Fixtures.ShortNameRealmB",
			[.. GeneratorTestHarness.ExtraReferences, GeneratorTestHarness.StubFacadeReference], ShortNameRealmBFixture);

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

	[Fact]
	void Two_referenced_realms_sharing_a_shape_short_name_report_NORSE035_and_emit_neither_colliding_shape()
	{
		var (diagnostics, outputCompilation) = GeneratorTestHarness.RunWithReferences(
			[BuildShortNameRealmAReference(), BuildShortNameRealmBReference()]);

		var violation = diagnostics.ShouldHaveSingleItem();
		violation.Id.ShouldBe("NORSE035");
		violation.Severity.ShouldBe(DiagnosticSeverity.Error);
		violation.Location.ShouldBe(Location.None);

		var message = violation.GetMessage(CultureInfo.InvariantCulture);
		message.ShouldContain("Order");
		message.ShouldContain("global::Norse.Fixtures.ShortNameRealmA.Order");
		message.ShouldContain("global::Norse.Fixtures.ShortNameRealmB.Order");

		GeneratedFileNames(outputCompilation).ShouldNotContain("OrderXmlShape.g.cs");
	}

	[Fact]
	void Non_colliding_shapes_in_the_same_run_still_emit_despite_a_short_name_collision_elsewhere()
	{
		var (_, outputCompilation) = GeneratorTestHarness.RunWithReferences(
			[BuildShortNameRealmAReference(), BuildShortNameRealmBReference()]);

		var shapeFiles = GeneratedFileNames(outputCompilation)
			.Where(static name => name.EndsWith("XmlShape.g.cs", StringComparison.Ordinal))
			.ToList();
		shapeFiles.ShouldBe(["RealmAResponseXmlShape.g.cs", "RealmBResponseXmlShape.g.cs"], ignoreOrder: true);

		var registration = GeneratedSource(outputCompilation, "NorseXmlShapeRegistration.g.cs");
		registration.ShouldContain("RealmAResponseXmlShape");
		registration.ShouldContain("RealmBResponseXmlShape");
		registration.ShouldNotContain("OrderXmlShape");
	}

	[Fact]
	void An_internal_referenced_controller_with_no_IVT_grant_yields_no_shapes_and_no_diagnostics()
	{
		var (diagnostics, outputCompilation) =
			GeneratorTestHarness.RunWithReferences([BuildInternalControllerRealmReference()]);

		diagnostics.ShouldBeEmpty();

		GeneratedFileNames(outputCompilation).ShouldNotContain("InternalRequestXmlShape.g.cs");
		GeneratedFileNames(outputCompilation).ShouldNotContain("InternalResponseXmlShape.g.cs");

		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(static d => d.Severity == DiagnosticSeverity.Error)
			.ShouldBeEmpty();
	}

	[Fact]
	void An_internal_referenced_controller_whose_assembly_grants_IVT_to_the_host_is_discovered()
	{
		var (diagnostics, outputCompilation) =
			GeneratorTestHarness.RunWithReferences([BuildIvtControllerRealmReference()]);

		diagnostics.ShouldBeEmpty();

		var generated = GeneratedFileNames(outputCompilation);
		generated.ShouldContain("IvtRequestXmlShape.g.cs");
		generated.ShouldContain("IvtResponseXmlShape.g.cs");

		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(static d => d.Severity == DiagnosticSeverity.Error)
			.ShouldBeEmpty();
	}

	[Fact]
	void A_controller_nested_inside_a_public_static_class_in_a_referenced_assembly_strikes_NORSE037_and_emits_no_shapes()
	{
		// Ruled by Buvy 2026-08-09: facade controllers are namespace-level types. A GrpcControllerBase
		// descendant nested inside another type is a build error, struck identically from both discovery
		// paths -- this fixture used to prove the OLD law (nested controllers ARE discovered); it now
		// proves the reference-closure path strikes NORSE037 and emits nothing for it instead.
		var (diagnostics, outputCompilation) =
			GeneratorTestHarness.RunWithReferences([BuildNestedControllerRealmReference()]);

		var violation = diagnostics.ShouldHaveSingleItem();
		violation.Id.ShouldBe("NORSE037");
		violation.Severity.ShouldBe(DiagnosticSeverity.Error);
		violation.Location.IsInSource.ShouldBeFalse();

		var message = violation.GetMessage(CultureInfo.InvariantCulture);
		message.ShouldContain("NestedController");
		message.ShouldContain("Container");

		var generated = GeneratedFileNames(outputCompilation);
		generated.ShouldNotContain("NestedRequestXmlShape.g.cs");
		generated.ShouldNotContain("NestedResponseXmlShape.g.cs");
	}

	[Fact]
	void NORSE036_fires_when_a_referenced_contracts_parameterless_constructor_is_internal()
	{
		var (diagnostics, outputCompilation) =
			GeneratorTestHarness.RunWithReferences([BuildInternalCtorRealmReference()]);

		var violation = diagnostics.ShouldHaveSingleItem();
		violation.Id.ShouldBe("NORSE036");
		violation.Severity.ShouldBe(DiagnosticSeverity.Error);
		var message = violation.GetMessage(CultureInfo.InvariantCulture);
		message.ShouldContain("Norse.Fixtures.InternalCtorRealm.BadResponse");
		// The distinguishing phrase, not just "constructor" -- "has no parameterless constructor at all"
		// (the null-ctor branch) would satisfy a bare "constructor" substring check too, so this fact would
		// keep passing even if elevation regressed back to eliding the internal ctor into invisibility.
		// Asserting the "inaccessible" wording specifically brackets the two message branches apart.
		message.ShouldContain("is not accessible from the host");

		// hasErrors (XmlShapeGenerator) suppresses ALL emission once any error diagnostic is present — no
		// BadResponseXmlShape.g.cs, hence no "new BadResponse { ... }" and no CS0272 to trip over.
		GeneratedFileNames(outputCompilation).ShouldNotContain("BadResponseXmlShape.g.cs");
		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.ShouldNotContain(d => d.Id == "CS0272" || d.Id == "CS0122");
	}

	[Fact]
	void NORSE036_fires_when_a_referenced_contracts_wire_member_has_an_internal_init_accessor()
	{
		var (diagnostics, outputCompilation) =
			GeneratorTestHarness.RunWithReferences([BuildInternalInitAccessorRealmReference()]);

		var violation = diagnostics.ShouldHaveSingleItem();
		violation.Id.ShouldBe("NORSE036");
		violation.Severity.ShouldBe(DiagnosticSeverity.Error);
		var message = violation.GetMessage(CultureInfo.InvariantCulture);
		message.ShouldContain("Status");
		message.ShouldContain("Norse.Fixtures.InternalInitAccessorRealm.BadResponse");
		message.ShouldContain("init");

		GeneratedFileNames(outputCompilation).ShouldNotContain("BadResponseXmlShape.g.cs");
		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.ShouldNotContain(d => d.Id == "CS0272" || d.Id == "CS0122");
	}

	[Fact]
	void NORSE036_fires_when_a_referenced_contracts_wire_member_has_a_private_init_accessor()
	{
		var (diagnostics, outputCompilation) =
			GeneratorTestHarness.RunWithReferences([BuildPrivateInitAccessorRealmReference()]);

		var violation = diagnostics.ShouldHaveSingleItem();
		violation.Id.ShouldBe("NORSE036");
		violation.Severity.ShouldBe(DiagnosticSeverity.Error);
		var message = violation.GetMessage(CultureInfo.InvariantCulture);
		message.ShouldContain("Status");
		message.ShouldContain("Norse.Fixtures.PrivateInitAccessorRealm.BadResponse");
		message.ShouldContain("init");

		GeneratedFileNames(outputCompilation).ShouldNotContain("BadResponseXmlShape.g.cs");
		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.ShouldNotContain(d => d.Id == "CS0272" || d.Id == "CS0122");
	}

	[Fact]
	void NORSE036_does_not_fire_on_a_referenced_contracts_internal_construction_surface_when_IVT_is_granted()
	{
		var (diagnostics, outputCompilation) =
			GeneratorTestHarness.RunWithReferences([BuildIvtConstructionRealmReference()]);

		diagnostics.ShouldBeEmpty();

		GeneratedFileNames(outputCompilation).ShouldContain("GoodResponseXmlShape.g.cs");
		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(static d => d.Severity == DiagnosticSeverity.Error)
			.ShouldBeEmpty();
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
