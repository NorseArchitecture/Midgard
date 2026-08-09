using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Infrastructure.Web.Server.Xml.Generator.Tests;

/// <summary>
///     One test per Futhark shape-law diagnostic (NORSE022-028) — a controller exposing a contract that
///     violates exactly one law, asserting the diagnostic ID and that the reported location's source
///     substring is the offending symbol's own name (the "squiggle lands on the offending symbol" bar).
///     Plus the exposure-scoping negative (spec §15): the same kind of violation, unexposed, compiles clean.
/// </summary>
public sealed class ShapeLawDiagnosticTests
{
	[Fact]
	void NORSE022_fires_on_a_raw_scalar_in_the_request_closure()
	{
		const string Fixture = """
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N022;

			[DataContract]
			public sealed record BadRequest
			{
				[DataMember]
				public decimal Limit { get; init; }
			}

			public sealed record GoodResponse
			{
				[DataMember]
				public decimal Total { get; init; }
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<GoodResponse>> Do([FromBody] BadRequest request) =>
					Task.FromResult(new ActionResult<GoodResponse>(new GoodResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE022");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("Limit");
	}

	[Fact]
	void NORSE023_fires_on_Result_reachable_in_the_response_closure()
	{
		const string Fixture = """
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Primitives;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N023;

			[DataContract]
			public sealed record GoodRequest
			{
				[DataMember]
				public Result<decimal> Limit { get; init; }
			}

			public sealed record BadResponse
			{
				[DataMember]
				public Result<decimal> Total { get; init; }
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<BadResponse>> Do([FromBody] GoodRequest request) =>
					Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE023");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("Total");
	}

	[Fact]
	void NORSE024_fires_when_a_type_is_reachable_from_both_closures()
	{
		const string Fixture = """
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N024;

			public sealed record SharedThing
			{
				[DataMember]
				public string Name { get; init; } = "";
			}

			[DataContract]
			public sealed record BadRequest
			{
				[DataMember]
				public SharedThing Item { get; init; } = null!;
			}

			public sealed record BadResponse
			{
				[DataMember]
				public SharedThing Item { get; init; } = null!;
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<BadResponse>> Do([FromBody] BadRequest request) =>
					Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE024");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("SharedThing");
	}

	[Fact]
	void NORSE025_fires_on_a_non_sealed_reachable_complex_type()
	{
		const string Fixture = """
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Primitives;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N025;

			[DataContract]
			public sealed record GoodRequest
			{
				[DataMember]
				public Result<string> Value { get; init; }
			}

			public record BadThing
			{
				[DataMember]
				public string Name { get; init; } = "";
			}

			public sealed record GoodResponse
			{
				[DataMember]
				public BadThing Item { get; init; } = null!;
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<GoodResponse>> Do([FromBody] GoodRequest request) =>
					Task.FromResult(new ActionResult<GoodResponse>(new GoodResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE025");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("BadThing");
	}

	[Fact]
	void NORSE025_reports_cleanly_instead_of_crashing_on_a_referenced_assembly_type_with_no_source_location()
	{
		// ExternalThing lives in a genuinely separate compilation, referenced only as metadata — its
		// ISymbol has no location with IsInSource, exactly the shape a shared-library DTO or any
		// non-fixture-local type takes. It's deliberately non-sealed, so reaching it legitimately trips
		// NORSE025. Reporting a diagnostic on such a symbol must not throw: LocationInfo.FromSymbol
		// falls back to LocationInfo.None (empty-but-non-null FilePath), not default(LocationInfo)
		// (null FilePath, which crashes Location.Create with ArgumentNullException).
		var externalReference = CompileToMetadataReference("""
			using System.Runtime.Serialization;

			namespace Norse.Fixtures.External;

			public class ExternalThing
			{
				[DataMember]
				public string Name { get; set; } = "";
			}
			""");

		const string Fixture = """
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Primitives;
			using Norse.Abstractions.Web.Server.Facade;
			using Norse.Fixtures.External;

			namespace Norse.Fixtures.N025Referenced;

			[DataContract]
			public sealed record GoodRequest
			{
				[DataMember]
				public Result<string> Value { get; init; }
			}

			public sealed record BadResponse
			{
				[DataMember]
				public ExternalThing Item { get; init; } = null!;
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<BadResponse>> Do([FromBody] GoodRequest request) =>
					Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
			}
			""";

		var compilation = GeneratorTestHarness.CreateCompilation(Fixture).AddReferences(externalReference);
		_ = CSharpGeneratorDriver.Create(new XmlShapeGenerator().AsSourceGenerator())
			.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics,
				TestContext.Current.CancellationToken);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE025");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		diagnostic.Location.SourceTree.ShouldBeNull();
		diagnostic.Location.SourceSpan.ShouldBe(default);
	}

	/// <summary>
	///     Compiles <paramref name="source" /> into an in-memory assembly and returns it as a metadata-only reference — a
	///     type from it resolves with no <c>IsInSource</c> location, exactly like a real shared-library dependency.
	/// </summary>
	static MetadataReference CompileToMetadataReference(string source)
	{
		var compilation = CSharpCompilation.Create(
			"Norse.Fixtures.ExternalLibrary",
			[CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
			GeneratorTestHarness.ExtraReferences,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		using MemoryStream stream = new();
		var result = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
		result.Success.ShouldBeTrue(string.Join("\n", result.Diagnostics));
		stream.Position = 0;
		return MetadataReference.CreateFromStream(stream);
	}

	[Fact]
	void NORSE026_fires_when_two_members_share_one_complex_type()
	{
		const string Fixture = """
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Primitives;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N026;

			[DataContract]
			public sealed record GoodRequest
			{
				[DataMember]
				public Result<string> Value { get; init; }
			}

			public sealed record PostalAddress
			{
				[DataMember]
				public string Line1 { get; init; } = "";
			}

			public sealed record BadResponse
			{
				[DataMember]
				public PostalAddress Home { get; init; } = null!;
				[DataMember]
				public PostalAddress Mailing { get; init; } = null!;
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<BadResponse>> Do([FromBody] GoodRequest request) =>
					Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE026");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("Mailing");
	}

	[Fact]
	void NORSE026_fires_once_on_a_post_case_transform_name_collision_even_when_every_style_collides()
	{
		// UserId/UserID decompose to the same word list ("User"+"Id" vs "User"+"ID", differing only in
		// the trailing letter's case) — they collide in all five casing styles simultaneously
		// (camelCase "userId", PascalCase "UserId", snake_case "user_id", UPPERCASE "USERID",
		// lowercase "userid" all match pairwise). This is the law's "or post-case-transform name
		// collision in any style" trigger — untested until now. It also proves the collision is
		// reported exactly once per offending member, not once per colliding style: five style hits
		// from one naming mistake must not become five diagnostics.
		const string Fixture = """
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Primitives;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N026NameCollision;

			[DataContract]
			public sealed record GoodRequest
			{
				[DataMember]
				public Result<string> Value { get; init; }
			}

			public sealed record BadResponse
			{
				[DataMember]
				public string UserId { get; init; } = "";
				[DataMember]
				public string UserID { get; init; } = "";
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<BadResponse>> Do([FromBody] GoodRequest request) =>
					Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE026");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("UserID");
	}

	[Fact]
	void NORSE027_fires_on_a_scalar_collection()
	{
		const string Fixture = """
			using System.Collections.Generic;
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Primitives;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N027;

			[DataContract]
			public sealed record GoodRequest
			{
				[DataMember]
				public Result<string> Value { get; init; }
			}

			public sealed record BadResponse
			{
				[DataMember]
				public List<string> Tags { get; init; } = new();
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<BadResponse>> Do([FromBody] GoodRequest request) =>
					Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE027");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("Tags");
	}

	[Fact]
	void NORSE028_fires_when_a_body_bound_type_carries_no_DataContract()
	{
		const string Fixture = """
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Primitives;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N028;

			public sealed record NoContractRequest
			{
				[DataMember]
				public Result<string> Value { get; init; }
			}

			public sealed record GoodResponse
			{
				[DataMember]
				public string Status { get; init; } = "";
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<GoodResponse>> Do([FromBody] NoContractRequest request) =>
					Task.FromResult(new ActionResult<GoodResponse>(new GoodResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE028");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("request");
	}

	[Fact]
	void NORSE029_fires_on_a_Result_wrapped_flags_enum_in_the_request_closure()
	{
		const string Fixture = """
			using System;
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Primitives;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N029Request;

			[Flags]
			public enum AccessRights
			{
				None = 0,
				Read = 1
			}

			[DataContract]
			public sealed record BadRequest
			{
				[DataMember]
				public Result<AccessRights> Perm { get; init; }
			}

			public sealed record GoodResponse
			{
				[DataMember]
				public string Status { get; init; } = "";
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<GoodResponse>> Do([FromBody] BadRequest request) =>
					Task.FromResult(new ActionResult<GoodResponse>(new GoodResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE029");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("Perm");
	}

	[Fact]
	void NORSE029_fires_on_a_plain_flags_enum_in_the_response_closure()
	{
		const string Fixture = """
			using System;
			using System.Runtime.Serialization;
			using System.Threading.Tasks;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Primitives;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N029Response;

			[Flags]
			public enum AccessRights
			{
				None = 0,
				Read = 1
			}

			[DataContract]
			public sealed record GoodRequest
			{
				[DataMember]
				public Result<string> Value { get; init; }
			}

			public sealed record BadResponse
			{
				[DataMember]
				public AccessRights Perm { get; init; }
			}

			public sealed class BadController : GrpcControllerBase
			{
				public Task<ActionResult<BadResponse>> Do([FromBody] GoodRequest request) =>
					Task.FromResult(new ActionResult<BadResponse>(new BadResponse()));
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		var diagnostic = diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE029");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		SourceAt(Fixture, diagnostic).ShouldBe("Perm");
	}

	[Fact]
	void NORSE029_does_not_fire_when_the_flags_contract_is_unexposed()
	{
		const string Fixture = """
			using System;
			using System.Runtime.Serialization;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.N029ExposureScoping;

			[Flags]
			public enum AccessRights
			{
				None = 0,
				Read = 1
			}

			[DataContract]
			public sealed record UnexposedFlagsRequest
			{
				[DataMember]
				public AccessRights Perm { get; init; }
			}

			public sealed class HarmlessController : GrpcControllerBase
			{
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		diagnostics.ShouldBeEmpty();
	}

	[Fact]
	void A_violating_contract_untouched_by_any_controller_action_compiles_clean()
	{
		const string Fixture = """
			using System.Runtime.Serialization;
			using Microsoft.AspNetCore.Mvc;
			using Norse.Abstractions.Web.Server.Facade;

			namespace Norse.Fixtures.ExposureScoping;

			[DataContract]
			public sealed record UnexposedBadRequest
			{
				[DataMember]
				public decimal Limit { get; init; }
			}

			public sealed class HarmlessController : GrpcControllerBase
			{
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		diagnostics.ShouldBeEmpty();
	}

	/// <summary>
	///     The exact source substring at <paramref name="diagnostic" />'s reported span, within
	///     <paramref name="fixture" /> — proves the squiggle lands on the offending symbol, not merely that the right ID
	///     fired.
	/// </summary>
	static string SourceAt(string fixture, Diagnostic diagnostic)
	{
		var span = diagnostic.Location.SourceSpan;
		return fixture.Substring(span.Start, span.Length);
	}
}
