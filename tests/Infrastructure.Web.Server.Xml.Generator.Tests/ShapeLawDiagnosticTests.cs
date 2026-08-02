using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Xml.Generator.Tests;

/// <summary>
/// One test per Futhark shape-law diagnostic (NORSE022-028) — a controller exposing a contract that
/// violates exactly one law, asserting the diagnostic ID and that the reported location's source
/// substring is the offending symbol's own name (the "squiggle lands on the offending symbol" bar).
/// Plus the exposure-scoping negative (spec §15): the same kind of violation, unexposed, compiles clean.
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
				public decimal Limit { get; init; }
			}

			public sealed record GoodResponse
			{
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
				public Result<decimal> Limit { get; init; }
			}

			public sealed record BadResponse
			{
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
				public string Name { get; init; } = "";
			}

			[DataContract]
			public sealed record BadRequest
			{
				public SharedThing Item { get; init; } = null!;
			}

			public sealed record BadResponse
			{
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
				public Result<string> Value { get; init; }
			}

			public record BadThing
			{
				public string Name { get; init; } = "";
			}

			public sealed record GoodResponse
			{
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
				public Result<string> Value { get; init; }
			}

			public sealed record PostalAddress
			{
				public string Line1 { get; init; } = "";
			}

			public sealed record BadResponse
			{
				public PostalAddress Home { get; init; } = null!;
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
				public Result<string> Value { get; init; }
			}

			public sealed record BadResponse
			{
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
				public Result<string> Value { get; init; }
			}

			public sealed record GoodResponse
			{
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
				public decimal Limit { get; init; }
			}

			public sealed class HarmlessController : GrpcControllerBase
			{
			}
			""";

		var diagnostics = GeneratorTestHarness.GenerateDiagnostics(Fixture);

		diagnostics.ShouldBeEmpty();
	}

	/// <summary>The exact source substring at <paramref name="diagnostic"/>'s reported span, within <paramref name="fixture"/> — proves the squiggle lands on the offending symbol, not merely that the right ID fired.</summary>
	static string SourceAt(string fixture, Diagnostic diagnostic)
	{
		var span = diagnostic.Location.SourceSpan;
		return fixture.Substring(span.Start, span.Length);
	}
}
