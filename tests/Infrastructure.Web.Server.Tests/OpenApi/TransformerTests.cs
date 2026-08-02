using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Norse.Infrastructure.Web.Server.OpenApi;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Tests.OpenApi;

/// <summary>
/// Tests <see cref="ResultSchemaTransformer"/>, <see cref="XmlMetadataTransformer"/>, and
/// <see cref="UnionLeakGuardTransformer"/> against the real <c>Microsoft.AspNetCore.OpenApi</c> native
/// pipeline (never Swashbuckle — confirmed by using its actual types throughout, not a stand-in). Every
/// assertion reads the byte-real generated document, fetched from a live <see cref="TestServer"/> host
/// exactly the way a partner's tooling would, not a hand-inspected in-memory model.
/// </summary>
public sealed class TransformerTests
{
	[Fact]
	async Task Result_wrapped_scalar_renders_as_the_unwrapped_type_and_format()
	{
		var document = await BuildDocumentAsync();

		var effective = document["components"]!["schemas"]!["QuoteRequest"]!["properties"]!["effectiveDate"]!;
		effective["type"]!.GetValue<string>().ShouldBe("string");
		effective["format"]!.GetValue<string>().ShouldBe("date");
	}

	[Fact]
	async Task Nullable_Result_member_is_absent_from_required_while_the_non_nullable_one_stays()
	{
		var document = await BuildDocumentAsync();

		var required = document["components"]!["schemas"]!["QuoteRequest"]!["required"]!.AsArray()
			.Select(node => node!.GetValue<string>())
			.ToArray();

		required.ShouldContain("effectiveDate");
		required.ShouldContain("lineCount");
		required.ShouldNotContain("expirationDate");
	}

	[Fact]
	async Task Result_wrapped_request_member_is_writeOnly()
	{
		var document = await BuildDocumentAsync();

		document["components"]!["schemas"]!["QuoteRequest"]!["properties"]!["effectiveDate"]!["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
	}

	[Fact]
	async Task Raw_scalar_response_member_is_readOnly_via_the_same_closed_table()
	{
		var document = await BuildDocumentAsync();

		var status = document["components"]!["schemas"]!["QuoteReport"]!["properties"]!["policyStatus"]!;
		status["type"]!.GetValue<string>().ShouldBe("string");
		status["readOnly"]!.GetValue<bool>().ShouldBeTrue();
	}

	[Fact]
	async Task Every_scalar_property_carries_xml_attribute_metadata_case_styled_to_the_host_style()
	{
		var document = await BuildDocumentAsync();

		// The resolved Microsoft.OpenApi 3.6.0 (OpenAPI 3.2) vocabulary replaced the classic
		// "attribute: true" boolean with a NodeType enum — "nodeType": "attribute" is the honest
		// equivalent this package version actually renders (see XmlMetadataTransformer's remarks).
		var effective = document["components"]!["schemas"]!["QuoteRequest"]!["properties"]!["effectiveDate"]!["xml"]!;
		effective["nodeType"]!.GetValue<string>().ShouldBe("attribute");
		effective["name"]!.GetValue<string>().ShouldBe("effective_date"); // SnakeCase, the host style this test wires below.
	}

	[Fact]
	async Task The_contract_types_own_schema_carries_a_case_styled_xml_element_name()
	{
		var document = await BuildDocumentAsync();

		document["components"]!["schemas"]!["QuoteRequest"]!["xml"]!["name"]!.GetValue<string>().ShouldBe("quote_request");
	}

	[Fact]
	async Task Collection_item_elements_are_named_from_the_item_types_own_case_styled_schema_name()
	{
		var document = await BuildDocumentAsync();

		// Item element names come from the item type's own [DataContract] schema (spec §6.3) — proven
		// here against the real generated document, not merely eyeballed against a throwaway probe.
		document["components"]!["schemas"]!["CoverageLine"]!["xml"]!["name"]!.GetValue<string>().ShouldBe("coverage_line");

		// No literal "wrapped" keyword exists in this OpenApi package version's xml vocabulary — see
		// XmlMetadataTransformer's remarks. The array property itself carries no stray xml stamp; the
		// vocabulary's own unwrapped-by-default behavior already matches Futhark's law.
		document["components"]!["schemas"]!["QuoteRequest"]!["properties"]!["lines"]!.AsObject().ContainsKey("xml").ShouldBeFalse();
	}

	[Fact]
	async Task The_wired_pipeline_never_leaks_Result_or_Outcome_by_name()
	{
		var document = await BuildDocumentAsync();

		var schemaNames = document["components"]!["schemas"]!.AsObject().Select(kv => kv.Key);
		schemaNames.ShouldNotContain(name => name.StartsWith("Result", StringComparison.Ordinal) || name.StartsWith("Outcome", StringComparison.Ordinal));
	}

	[Fact]
	async Task Removing_ResultSchemaTransformer_from_the_registration_makes_the_leak_guard_fail_the_build()
	{
		// The "wired not just designed" proof (spec §10.4): the guard is not a fixture-only unit test —
		// it genuinely trips on a real, live-generated document when the unwrap transformer that is
		// supposed to run first is missing from registration.
		var exception = await Should.ThrowAsync<InvalidOperationException>(() => BuildRawDocumentAsync(registerResultTransformer: false));

		exception.Message.ShouldContain("Result<T>/Outcome<T>");
	}

	[Fact]
	async Task The_leak_guard_throws_when_a_raw_Outcome_reference_is_smuggled_into_a_signature_less_document()
	{
		OpenApiDocument document = new()
		{
			Components = new OpenApiComponents
			{
				Schemas = new Dictionary<string, IOpenApiSchema>
				{
					["OutcomeOfQuoteReport"] = new OpenApiSchema { Type = JsonSchemaType.Object }
				}
			}
		};

		var exception = await Should.ThrowAsync<InvalidOperationException>(
			() => new UnionLeakGuardTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext { DocumentName = "v1", DescriptionGroups = [], ApplicationServices = _emptyServiceProvider }, TestContext.Current.CancellationToken));

		exception.Message.ShouldContain("OutcomeOfQuoteReport");
	}

	[Fact]
	async Task The_leak_guard_does_not_throw_on_a_document_with_no_reserved_names()
	{
		OpenApiDocument document = new()
		{
			Components = new OpenApiComponents
			{
				Schemas = new Dictionary<string, IOpenApiSchema>
				{
					["QuoteReport"] = new OpenApiSchema { Type = JsonSchemaType.Object }
				}
			}
		};

		await Should.NotThrowAsync(
			() => new UnionLeakGuardTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext { DocumentName = "v1", DescriptionGroups = [], ApplicationServices = _emptyServiceProvider }, TestContext.Current.CancellationToken));
	}

	static readonly IServiceProvider _emptyServiceProvider = new ServiceCollection().BuildServiceProvider();

	static async Task<JsonNode> BuildDocumentAsync()
	{
		var json = await BuildRawDocumentAsync(registerResultTransformer: true);
		return JsonNode.Parse(json)!;
	}

	static async Task<string> BuildRawDocumentAsync(bool registerResultTransformer)
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddControllers()
			// The test assembly's default entry-assembly controller scan would also pick up sibling
			// test-fixture controllers from Xml/TripwireFixtures.cs (TripwireController et al., which
			// carry no attribute route by design — they exist for AddNorseXmlTests.cs, a different
			// host wiring entirely). Scoping discovery to QuotesController alone keeps this document
			// generation deterministic and independent of what other test fixtures exist in the project.
			.ConfigureApplicationPartManager(manager =>
			{
				for (var i = manager.FeatureProviders.Count - 1; i >= 0; i--)
					if (manager.FeatureProviders[i] is ControllerFeatureProvider)
						manager.FeatureProviders.RemoveAt(i);

				manager.FeatureProviders.Add(new OnlyQuotesControllerFeatureProvider());
			});
		builder.Services.AddSingleton(new NorseXmlOptions { CaseStyle = XmlCaseStyle.SnakeCase });
		builder.Services.AddOpenApi(options =>
		{
			if (registerResultTransformer)
				options.AddSchemaTransformer<ResultSchemaTransformer>();
			options.AddSchemaTransformer<XmlMetadataTransformer>();
			options.AddDocumentTransformer<UnionLeakGuardTransformer>();
		});

		await using var app = builder.Build();
		app.MapOpenApi();
		app.MapControllers();

		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
		var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		await app.StopAsync(TestContext.Current.CancellationToken);

		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException(json); // the document generation itself threw (e.g. the leak guard) — MapOpenApi surfaces it as a 500 body, not a thrown exception at this call site, so re-throw for the "verify fail" tests below.

		return json;
	}
}

/// <summary>Restricts controller discovery to <see cref="QuotesController"/> alone — see the remark at its registration call site.</summary>
sealed class OnlyQuotesControllerFeatureProvider : ControllerFeatureProvider
{
	protected override bool IsController(TypeInfo typeInfo) =>
		typeInfo == typeof(QuotesController).GetTypeInfo();
}

[ApiController]
[Route("quotes")]
sealed class QuotesController : ControllerBase
{
#pragma warning disable CA1822 // ASP.NET Core actions must be instance methods — see the identical suppression in TripwireFixtures.cs.
	[HttpPost]
	public ActionResult<QuoteReport> Post([FromBody] QuoteRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		return new QuoteReport();
	}
#pragma warning restore CA1822
}

[DataContract]
sealed class QuoteRequest
{
	public Result<DateOnly> EffectiveDate { get; init; }
	public Result<DateOnly>? ExpirationDate { get; init; }
	public Result<int> LineCount { get; init; }
	public List<CoverageLine> Lines { get; init; } = [];
}

[DataContract]
sealed class CoverageLine
{
	public Result<string> Code { get; init; }
}

[DataContract]
sealed class QuoteReport
{
	public string PolicyStatus { get; init; } = "";
}
