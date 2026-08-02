using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.Web.Server.Tests.Xml.TripwireFixtures;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

/// <summary>
/// Tests <see cref="MvcBuilderExtensions.AddNorseXml"/>'s composition seam: the ordinary DI wiring
/// (registry singleton, <see cref="NorseXmlOptions"/>, both formatter types) via a plain
/// <see cref="ServiceCollection"/> — mirroring the sibling <c>Json/MvcBuilderExtensionsTests.cs</c>
/// idiom — plus the library-controller tripwire (spec §3, ratified 2026-08-02), which needs a real
/// <see cref="WebApplication"/> host to prove it fails at genuine startup, not merely from a validator
/// method returning false.
/// </summary>
/// <remarks>
/// <see cref="BuildHost"/> registers the whole test assembly as one application part, so every
/// tripwire-scanned fixture controller (<see cref="TripwireController"/> and
/// <see cref="ImplicitBodyController"/>) is discovered in every host-based test below, regardless of
/// which one a given test is actually about. Each test therefore builds a registry that fully
/// satisfies every OTHER scanned controller's types and leaves only the one type under test missing —
/// otherwise the thrown exception's controller/type names would depend on
/// <c>ApplicationPartManager</c>'s unspecified enumeration order instead of the test's own intent.
/// </remarks>
public sealed class AddNorseXmlTests
{
	[Fact]
	void AddNorseXml_registers_the_registry_singleton_options_and_both_formatter_types()
	{
		ServiceCollection services = new();
		var builder = services.AddControllers();
		var registry = new XmlShapeRegistry();

		builder.AddNorseXml(XmlCaseStyle.SnakeCase, registry);

		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<XmlShapeRegistry>().ShouldBeSameAs(registry);
		provider.GetRequiredService<NorseXmlOptions>().CaseStyle.ShouldBe(XmlCaseStyle.SnakeCase);

		var mvcOptions = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
		mvcOptions.InputFormatters.ShouldContain(f => f is XmlContractInputFormatter);
		mvcOptions.OutputFormatters.ShouldContain(f => f is XmlContractOutputFormatter);
	}

	[Fact]
	void AddNorseXml_registers_the_problem_xml_formatter_and_the_ModelState_problem_factory()
	{
		ServiceCollection services = new();
		var builder = services.AddControllers();
		var registry = new XmlShapeRegistry();

		builder.AddNorseXml(XmlCaseStyle.SnakeCase, registry);

		using var provider = services.BuildServiceProvider();
		var mvcOptions = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
		mvcOptions.OutputFormatters.ShouldContain(f => f is ProblemXmlOutputFormatter);

		var apiBehaviorOptions = provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value;
		apiBehaviorOptions.InvalidModelStateResponseFactory.ShouldBe(InvalidModelStateProblemFactory.Create);
	}

	[Fact]
	void AddNorseXml_suppresses_the_implicit_NRT_required_attribute()
	{
		// Required-ness on Futhark contracts is carried by Result<T> presence semantics plus
		// FluentValidation's ResultRules — never by MVC's DataAnnotations layer. Without this switch,
		// [ApiController]'s implicit [Required] on the non-nullable [FromBody] parameter double-fires
		// whenever the XML input formatter returns Failure (the parameter binds null), layering a
		// "The request field is required" ModelState entry on top of the formatter's real accumulated
		// failures — the Task 13 payload-asymmetry finding.
		ServiceCollection services = new();
		var builder = services.AddControllers();

		builder.AddNorseXml(XmlCaseStyle.CamelCase, new XmlShapeRegistry());

		using var provider = services.BuildServiceProvider();
		provider.GetRequiredService<IOptions<MvcOptions>>().Value
			.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes.ShouldBeTrue();
	}

	[Fact]
	void AddNorseXml_throws_on_a_null_registry()
	{
		ServiceCollection services = new();
		var builder = services.AddControllers();

		Should.Throw<ArgumentNullException>(() => builder.AddNorseXml(XmlCaseStyle.CamelCase, null!));
	}

	// XmlContractInputFormatter/XmlContractOutputFormatter's real ReadRequestBodyAsync/WriteResponseBodyAsync
	// behavior (Task 9) is covered by InputFormatterTests.cs, OutputFormatterTests.cs, and
	// SecurityCorpusTests.cs — the shell-era "throws NotSupportedException unconditionally" tests that
	// used to live here were replaced wholesale, not merely edited, since that behavior no longer exists.

	[Fact]
	async Task Tripwire_fails_startup_with_the_exact_named_error_when_a_facade_controller_exposes_an_unregistered_type()
	{
		// Every other scanned controller's types are satisfied — only TripwireController's own
		// [FromBody] TripwireRequest is missing — so the thrown message is deterministically about it.
		var registry = new XmlShapeRegistry();
		registry.Add(new FakeXmlShape<TripwireResponse>());
		registry.Add(new FakeXmlShape<ImplicitBodyPayload>());

		await using var app = BuildHost(registry);

		var exception = await Should.ThrowAsync<InvalidOperationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

		exception.Message.ShouldBe(
			"facade controllers are host-compilation source — 'TripwireController' exposes 'TripwireRequest' with no generated shape; controllers shipped in referenced assemblies generate nothing");
	}

	[Fact]
	async Task Tripwire_fails_startup_with_the_exact_named_error_for_the_implicit_single_parameter_body_binding_convention()
	{
		// ImplicitBodyController.Do(ImplicitBodyPayload payload) carries no [FromBody] — it relies on
		// MVC's implicit single-non-scalar-parameter convention, the same one ClosureWalker.Analyze
		// treats as body-bound (spec §4.1) when generating shapes. Every other scanned controller's
		// types are satisfied here, so only this one is deterministically missing.
		var registry = new XmlShapeRegistry();
		registry.Add(new FakeXmlShape<TripwireRequest>());
		registry.Add(new FakeXmlShape<TripwireResponse>());

		await using var app = BuildHost(registry);

		var exception = await Should.ThrowAsync<InvalidOperationException>(() => app.StartAsync(TestContext.Current.CancellationToken));

		exception.Message.ShouldBe(
			"facade controllers are host-compilation source — 'ImplicitBodyController' exposes 'ImplicitBodyPayload' with no generated shape; controllers shipped in referenced assemblies generate nothing");
	}

	[Fact]
	async Task Tripwire_starts_up_cleanly_when_every_exposed_type_has_a_registered_shape()
	{
		var registry = new XmlShapeRegistry();
		registry.Add(new FakeXmlShape<TripwireRequest>());
		registry.Add(new FakeXmlShape<TripwireResponse>());
		registry.Add(new FakeXmlShape<ImplicitBodyPayload>());

		await using var app = BuildHost(registry);

		await Should.NotThrowAsync(() => app.StartAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	async Task Tripwire_ignores_a_controller_that_does_not_derive_from_GrpcControllerBase()
	{
		// PlainMvcController (also discovered — same application part) exposes UnregisteredPayload
		// with no registered shape. Every GrpcControllerBase-descended controller's types are
		// registered here; if the tripwire scanned every discovered controller instead of only
		// GrpcControllerBase descendants, PlainMvcController's unregistered payload would still fail
		// startup.
		var registry = new XmlShapeRegistry();
		registry.Add(new FakeXmlShape<TripwireRequest>());
		registry.Add(new FakeXmlShape<TripwireResponse>());
		registry.Add(new FakeXmlShape<ImplicitBodyPayload>());

		await using var app = BuildHost(registry);

		await Should.NotThrowAsync(() => app.StartAsync(TestContext.Current.CancellationToken));
	}

	static WebApplication BuildHost(XmlShapeRegistry registry)
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseTestServer();
		builder.Services.AddControllers()
			.AddApplicationPart(typeof(TripwireController).Assembly)
			.AddNorseXml(XmlCaseStyle.CamelCase, registry);
		return builder.Build();
	}
}
