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
///     Tests <see cref="ResultSchemaTransformer" />, <see cref="XmlMetadataTransformer" />, and
///     <see cref="UnionLeakGuardTransformer" /> against the real <c>Microsoft.AspNetCore.OpenApi</c> native
///     pipeline (never Swashbuckle — confirmed by using its actual types throughout, not a stand-in). Every
///     assertion reads the byte-real generated document, fetched from a live <see cref="TestServer" /> host
///     exactly the way a partner's tooling would, not a hand-inspected in-memory model.
/// </summary>
public sealed class TransformerTests
{
	static readonly IServiceProvider _emptyServiceProvider = new ServiceCollection().BuildServiceProvider();

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
		required.ShouldContain("kind");
		required.ShouldNotContain("expirationDate");
	}

	[Fact]
	async Task Result_wrapped_request_member_is_writeOnly()
	{
		var document = await BuildDocumentAsync();

		document["components"]!["schemas"]!["QuoteRequest"]!["properties"]!["effectiveDate"]!["writeOnly"]!
			.GetValue<bool>().ShouldBeTrue();
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
		effective["name"]!.GetValue<string>()
			.ShouldBe("effective_date"); // SnakeCase, the host style this test wires below.
	}

	[Fact]
	async Task The_contract_types_own_schema_carries_a_case_styled_xml_element_name()
	{
		var document = await BuildDocumentAsync();

		document["components"]!["schemas"]!["QuoteRequest"]!["xml"]!["name"]!.GetValue<string>()
			.ShouldBe("quote_request");
	}

	[Fact]
	async Task Collection_item_elements_are_named_from_the_item_types_own_case_styled_schema_name()
	{
		var document = await BuildDocumentAsync();

		// Item element names come from the item type's own [DataContract] schema (spec §6.3) — proven
		// here against the real generated document, not merely eyeballed against a throwaway probe.
		document["components"]!["schemas"]!["CoverageLine"]!["xml"]!["name"]!.GetValue<string>()
			.ShouldBe("coverage_line");

		// No literal "wrapped" keyword exists in this OpenApi package version's xml vocabulary — see
		// XmlMetadataTransformer's remarks. The array property itself carries no stray xml stamp; the
		// vocabulary's own unwrapped-by-default behavior already matches Futhark's law.
		document["components"]!["schemas"]!["QuoteRequest"]!["properties"]!["lines"]!.AsObject().ContainsKey("xml")
			.ShouldBeFalse();
	}

	[Fact]
	async Task Result_wrapped_enum_renders_as_string_with_case_styled_member_names_not_the_leaky_union_shape()
	{
		var document = await BuildDocumentAsync();

		// The closed BCL table cannot key an open-ended enum type — enums are §7's twentieth row,
		// resolved through ScalarTaxonomy.TryBuildEnumSchema instead, mirroring how the generator's own
		// ClosureWalker.Classify handles enums generically (type.IsEnum, not a table of concrete types).
		var kind = document["components"]!["schemas"]!["QuoteRequest"]!["properties"]!["kind"]!;
		kind["type"]!.GetValue<string>().ShouldBe("string");
		kind.AsObject().ContainsKey("anyOf").ShouldBeFalse(); // never the default reflected union shape.

		var members = kind["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
		members.ShouldBe(["general_liability", "property_damage"]);

		kind["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
		kind["xml"]!["nodeType"]!.GetValue<string>()
			.ShouldBe("attribute"); // the existing scalar-property stamping already covers enums once IsClosedScalar recognizes them — no separate branch needed.
	}

	[Fact]
	async Task The_wired_pipeline_never_leaks_Result_or_Outcome_by_name()
	{
		var document = await BuildDocumentAsync();

		var schemaNames = document["components"]!["schemas"]!.AsObject().Select(kv => kv.Key);
		schemaNames.ShouldNotContain(name =>
			name.StartsWith("Result", StringComparison.Ordinal) ||
			name.StartsWith("Outcome", StringComparison.Ordinal));
	}

	[Fact]
	async Task Plain_enum_member_renders_as_governed_string_list_under_a_camel_case_host()
	{
		var document = await BuildDocumentAsync(caseStyle: XmlCaseStyle.CamelCase);

		// A raw (non-Result) enum member is left as the framework's own $ref to the shared component
		// schema for the enum type — ResultSchemaTransformer never touches it (that inline-override
		// mechanism is reserved for the Result<TEnum> branch, where the union leak forces a full
		// replacement); EnumSchemaTransformer governs the referenced component directly instead.
		document["components"]!["schemas"]!["QuoteReport"]!["properties"]!["status"]!["$ref"]!.GetValue<string>()
			.ShouldBe("#/components/schemas/TableStatus");

		var status = document["components"]!["schemas"]!["TableStatus"]!;
		status["type"]!.GetValue<string>().ShouldBe("string");

		var members = status["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
		members.ShouldBe(["active", "inactive"]);

		// A raw (non-Result) enum member is always response-side by the shape law (NORSE022/23 ban raw
		// scalars from request closures), and this governed component is never referenced from a request
		// direction — the component schema itself carries readOnly, restoring the spec §12 commitment.
		status["readOnly"]!.GetValue<bool>().ShouldBeTrue();
	}

	[Fact]
	async Task Flags_enum_member_renders_as_type_array_with_governed_string_items_from_the_same_table()
	{
		var document = await BuildDocumentAsync();

		// Flags enums still route through the framework's own per-CLR-type component dedup, exactly like
		// TableStatus above — the property itself carries only the $ref.
		document["components"]!["schemas"]!["QuoteReport"]!["properties"]!["coverageOptions"]!["$ref"]!
			.GetValue<string>().ShouldBe("#/components/schemas/CoverageOptions");

		var options = document["components"]!["schemas"]!["CoverageOptions"]!;
		options["type"]!.GetValue<string>().ShouldBe("array");
		options.AsObject().ContainsKey("enum").ShouldBeFalse(); // the picklist moves down to items — the outer array schema carries none of its own.
		options.AsObject().ContainsKey("format").ShouldBeFalse();

		var items = options["items"]!;
		items["type"]!.GetValue<string>().ShouldBe("string");
		var members = items["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
		members.ShouldBe(["fire", "flood"]); // the same governed table a plain enum would project, unfiltered.

		// Same response-only readOnly policy as the plain-enum component path (NORSE022/23) — a flags
		// member is never request-side either.
		options["readOnly"]!.GetValue<bool>().ShouldBeTrue();
	}

	[Fact]
	async Task Result_wrapped_flags_enum_member_renders_as_type_array_with_governed_string_items()
	{
		var document = await BuildDocumentAsync();

		// The Result<TEnum> branch builds its schema inline (never a $ref — see the class doc's remark on
		// why the union leak forces a full replacement there), so the array/items shape is asserted
		// directly on the property, mirroring how Result_wrapped_TableStatus_member_... reads statusResult.
		var options = document["components"]!["schemas"]!["QuoteRequest"]!["properties"]!["options"]!;
		options["type"]!.GetValue<string>().ShouldBe("array");
		options.AsObject().ContainsKey("enum").ShouldBeFalse();

		var items = options["items"]!;
		items["type"]!.GetValue<string>().ShouldBe("string");
		var members = items["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
		members.ShouldBe(["fire", "flood"]);

		// Request-side Result<TEnum> members carry writeOnly, never readOnly — the same distinction the
		// plain Result<TableStatus> fact above already covers for the non-flags row.
		options["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
		options.AsObject().ContainsKey("readOnly").ShouldBeFalse();
	}

	[Fact]
	async Task Result_wrapped_TableStatus_member_renders_the_identical_governed_string_list_under_a_camel_case_host()
	{
		var document = await BuildDocumentAsync(caseStyle: XmlCaseStyle.CamelCase);

		var statusResult = document["components"]!["schemas"]!["QuoteRequest"]!["properties"]!["statusResult"]!;
		statusResult["type"]!.GetValue<string>().ShouldBe("string");

		var members = statusResult["enum"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
		members.ShouldBe(["active", "inactive"]);

		// Request-side Result<TEnum> members carry writeOnly, never readOnly — the component-schema
		// ReadOnly stamp EnumSchemaTransformer applies to the raw $ref path must not leak here; this is
		// an inline schema ResultSchemaTransformer builds fresh via the shared ApplyGovernedList helper.
		statusResult["writeOnly"]!.GetValue<bool>().ShouldBeTrue();
		statusResult.AsObject().ContainsKey("readOnly").ShouldBeFalse();
	}

	[Fact]
	async Task An_enum_with_no_registered_table_throws_the_named_gap_from_EnumSchemaTransformer()
	{
		// The same impossible-by-construction tripwire EnumLexicalJsonConverterFactory already holds for
		// the JSON channel (EnumLexicalJsonConverterTests.Read_plain_enum_unregistered_type_throws_the_named_gap),
		// proven here for the OpenAPI channel against a real generated document, not a hand-built context.
		// Unlike UnionLeakGuardTransformer's document-transformer throw (caught by the framework and
		// surfaced as a 500 response — see BuildRawDocumentAsync's re-throw), a schema-transformer
		// exception during MapOpenApi's synchronous document build propagates straight through the
		// in-process TestServer call, uncaught — the raw NotSupportedException, not a wrapped 500.
		var exception = await Should.ThrowAsync<NotSupportedException>(BuildDocumentWithUngovernedEnumAsync);

		exception.Message.ShouldContain("no generated name table for enum 'UngovernedKind'");
	}

	static async Task BuildDocumentWithUngovernedEnumAsync()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddControllers()
			.ConfigureApplicationPartManager(manager =>
			{
				for (var i = manager.FeatureProviders.Count - 1; i >= 0; i--)
					if (manager.FeatureProviders[i] is ControllerFeatureProvider)
						manager.FeatureProviders.RemoveAt(i);

				manager.FeatureProviders.Add(new OnlyUngovernedControllerFeatureProvider());
			});
		builder.Services.AddSingleton(new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		builder.Services.AddSingleton(new EnumNameRegistry()); // deliberately empty — UngovernedKind carries no table.
		builder.Services.AddOpenApi(options =>
		{
			options.AddSchemaTransformer<ResultSchemaTransformer>();
			options.AddSchemaTransformer<EnumSchemaTransformer>();
			options.AddSchemaTransformer<XmlMetadataTransformer>();
			options.AddDocumentTransformer<UnionLeakGuardTransformer>();
		});

		await using var app = builder.Build();
		app.MapOpenApi();
		app.MapControllers();

		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative),
			TestContext.Current.CancellationToken);
		var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		await app.StopAsync(TestContext.Current.CancellationToken);

		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException(json);
	}

	[Fact]
	async Task Removing_ResultSchemaTransformer_from_the_registration_makes_the_leak_guard_fail_the_build()
	{
		// The "wired not just designed" proof (spec §10.4): the guard is not a fixture-only unit test —
		// it genuinely trips on a real, live-generated document when the unwrap transformer that is
		// supposed to run first is missing from registration.
		var exception =
			await Should.ThrowAsync<InvalidOperationException>(() =>
				BuildRawDocumentAsync(registerResultTransformer: false));

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

		var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
			new UnionLeakGuardTransformer().TransformAsync(document,
				new OpenApiDocumentTransformerContext
				{
					DocumentName = "v1",
					DescriptionGroups = [],
					ApplicationServices = _emptyServiceProvider
				}, TestContext.Current.CancellationToken));

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

		await Should.NotThrowAsync(() => new UnionLeakGuardTransformer().TransformAsync(document,
			new OpenApiDocumentTransformerContext
			{
				DocumentName = "v1",
				DescriptionGroups = [],
				ApplicationServices = _emptyServiceProvider
			}, TestContext.Current.CancellationToken));
	}

	static async Task<JsonNode> BuildDocumentAsync(XmlCaseStyle caseStyle = XmlCaseStyle.SnakeCase)
	{
		var json = await BuildRawDocumentAsync(registerResultTransformer: true, caseStyle);
		return JsonNode.Parse(json)!;
	}

	/// <summary>
	///     Columns follow <see cref="XmlCaseStyle" />'s declared order (Camel/Pascal/Snake/Upper/Lower) — the
	///     same hand-built idiom <c>EnumLexicalJsonConverterTests</c> uses. Every fixture enum this test
	///     file's contracts reference (<see cref="CoverageKind" />, <see cref="TableStatus" />,
	///     <see cref="CoverageOptions" />) gets a table here — <see cref="ResultSchemaTransformer" /> and
	///     <see cref="EnumSchemaTransformer" /> both throw on an unregistered enum, so every enum the fixture
	///     types reference must carry one.
	/// </summary>
	static EnumNameRegistry BuildEnumRegistry()
	{
		EnumNameRegistry registry = new();
		registry.Add(new EnumNameTable(
			typeof(CoverageKind),
			nameof(CoverageKind),
			[
				["generalLiability", "GeneralLiability", "general_liability", "GENERALLIABILITY", "generalliability"],
				["propertyDamage", "PropertyDamage", "property_damage", "PROPERTYDAMAGE", "propertydamage"]
			],
			[0, 1]));
		registry.Add(new EnumNameTable(
			typeof(TableStatus),
			nameof(TableStatus),
			[
				["active", "Active", "active", "ACTIVE", "active"],
				["inactive", "Inactive", "inactive", "INACTIVE", "inactive"]
			],
			[0, 1]));
		registry.Add(new EnumNameTable(
			typeof(CoverageOptions),
			nameof(CoverageOptions),
			[
				["fire", "Fire", "fire", "FIRE", "fire"],
				["flood", "Flood", "flood", "FLOOD", "flood"]
			],
			[1, 2]));
		return registry;
	}

	static async Task<string> BuildRawDocumentAsync(bool registerResultTransformer,
		XmlCaseStyle caseStyle = XmlCaseStyle.SnakeCase)
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
		builder.Services.AddSingleton(new NorseXmlOptions { CaseStyle = caseStyle });
		builder.Services.AddSingleton(BuildEnumRegistry());
		builder.Services.AddOpenApi(options =>
		{
			if (registerResultTransformer)
				options.AddSchemaTransformer<ResultSchemaTransformer>();
			options.AddSchemaTransformer<EnumSchemaTransformer>();
			options.AddSchemaTransformer<XmlMetadataTransformer>();
			options.AddDocumentTransformer<UnionLeakGuardTransformer>();
		});

		await using var app = builder.Build();
		app.MapOpenApi();
		app.MapControllers();

		await app.StartAsync(TestContext.Current.CancellationToken);
		using var client = app.GetTestClient();
		var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative),
			TestContext.Current.CancellationToken);
		var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		await app.StopAsync(TestContext.Current.CancellationToken);

		if (!response.IsSuccessStatusCode)
			throw
				new InvalidOperationException(
					json); // the document generation itself threw (e.g. the leak guard) — MapOpenApi surfaces it as a 500 body, not a thrown exception at this call site, so re-throw for the "verify fail" tests below.

		return json;
	}
}

/// <summary>
///     Restricts controller discovery to <see cref="QuotesController" /> alone — see the remark at its registration
///     call site.
/// </summary>
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
	public Result<CoverageKind> Kind { get; init; }
	public Result<TableStatus> StatusResult { get; init; }
	public Result<CoverageOptions> Options { get; init; }
	public List<CoverageLine> Lines { get; init; } = [];
}

enum CoverageKind
{
	GeneralLiability,
	PropertyDamage
}

/// <summary>
///     The plain-member fixture enum for Task 9's governed <c>enum:</c> list coverage — mirrors the
///     <c>EnumLexicalJsonConverterTests</c> fixture by name and shape.
/// </summary>
enum TableStatus
{
	Active,
	Inactive
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
	public TableStatus Status { get; init; }
	public CoverageOptions CoverageOptions { get; init; }
}

/// <summary>
///     The <c>[Flags]</c> fixture enum for Task 4's array-schema coverage — two single-bit members are
///     enough to prove the shape change without needing a composite (multi-bit) member.
/// </summary>
[Flags]
enum CoverageOptions
{
	Fire = 1,
	Flood = 2
}

/// <summary>
///     Restricts controller discovery to <see cref="UngovernedController" /> alone — the tripwire fixture for the
///     registry-miss throw test.
/// </summary>
sealed class OnlyUngovernedControllerFeatureProvider : ControllerFeatureProvider
{
	protected override bool IsController(TypeInfo typeInfo) =>
		typeInfo == typeof(UngovernedController).GetTypeInfo();
}

[ApiController]
[Route("ungoverned")]
sealed class UngovernedController : ControllerBase
{
#pragma warning disable CA1822 // ASP.NET Core actions must be instance methods — see the identical suppression in TripwireFixtures.cs.
	[HttpGet]
	public ActionResult<UngovernedReport> Get() => new UngovernedReport();
#pragma warning restore CA1822
}

/// <summary>
///     Carries a raw enum with deliberately no table in the registry the host wires — the registry-miss tripwire
///     fixture.
/// </summary>
enum UngovernedKind
{
	First,
	Second
}

[DataContract]
sealed class UngovernedReport
{
	public UngovernedKind Kind { get; init; }
}
