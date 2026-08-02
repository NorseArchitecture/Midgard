using System.Text;
using Norse.Infrastructure.Web.Server.Tests.Xml.FormatterFixtures;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Tests.Xml;

/// <summary>
/// Tests <see cref="XmlContractInputFormatter"/>'s plumbing — happy-path read, the
/// <see cref="XmlReadContext.HasFailures"/> gate, accumulable failures surfacing into
/// <c>ModelState</c>, and the unregistered-type refusal — against a hand-rolled
/// <see cref="WidgetXmlShape"/>, never generated code (the brief's explicit instruction). The security
/// corpus (session-fatal payloads, the spy-verified "shape never invoked" proof) lives in
/// <c>SecurityCorpusTests.cs</c>, not here.
/// </summary>
public sealed class InputFormatterTests
{
	[Fact]
	async Task Happy_path_read_returns_success_with_no_ModelState_errors()
	{
		var registry = new XmlShapeRegistry();
		registry.Add(new WidgetXmlShape());
		var formatter = new XmlContractInputFormatter(registry, new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		var context = FormatterTestSupport.BuildReadContext("""<widget name="lantern" />""", typeof(Widget));

		var result = await formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

		result.HasError.ShouldBeFalse();
		result.IsModelSet.ShouldBeTrue();
		((Widget)result.Model!).Name.ShouldBe("lantern");
		context.ModelState.ErrorCount.ShouldBe(0);
	}

	[Fact]
	async Task Accumulable_failures_surface_one_ModelState_entry_per_XmlReadFailure()
	{
		var registry = new XmlShapeRegistry();
		registry.Add(new WidgetXmlShape());
		var formatter = new XmlContractInputFormatter(registry, new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		var context = FormatterTestSupport.BuildReadContext("""<widget />""", typeof(Widget));

		var result = await formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

		result.HasError.ShouldBeTrue();
		context.ModelState.ErrorCount.ShouldBe(1);
		var entry = context.ModelState["widget/@name"].ShouldNotBeNull();
		entry.Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe("required value missing");
	}

	[Fact]
	async Task A_shape_that_always_constructs_despite_a_failure_never_lets_the_object_through()
	{
		// Mirrors Task 7's generator law directly: generated Read always constructs and returns an
		// object even when required members are missing. This formatter is the first real caller of
		// that contract and MUST check HasFailures before trusting the returned object — proven here
		// with a shape that deliberately constructs a sentinel value specifically so a regression here
		// would be caught red-handed (an unexpected Success carrying "SHOULD-NEVER-LEAK"), not silently
		// passed.
		var registry = new XmlShapeRegistry();
		registry.Add(new AlwaysFailsButConstructsWidgetShape());
		var formatter = new XmlContractInputFormatter(registry, new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		var context = FormatterTestSupport.BuildReadContext("""<widget name="anything" />""", typeof(Widget));

		var result = await formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

		result.HasError.ShouldBeTrue();
		result.IsModelSet.ShouldBeFalse();
		context.ModelState.ErrorCount.ShouldBe(1);
	}

	[Fact]
	async Task An_unregistered_type_is_refused_loudly()
	{
		var registry = new XmlShapeRegistry(); // Widget's shape deliberately never registered.
		var formatter = new XmlContractInputFormatter(registry, new NorseXmlOptions { CaseStyle = XmlCaseStyle.CamelCase });
		var context = FormatterTestSupport.BuildReadContext("""<widget name="x" />""", typeof(Widget));

		var exception = await Should.ThrowAsync<InvalidOperationException>(() => formatter.ReadRequestBodyAsync(context, Encoding.UTF8));
		exception.Message.ShouldContain("Widget");
	}

	[Fact]
	void Constructor_throws_on_a_null_registry_or_options()
	{
		Should.Throw<ArgumentNullException>(() => new XmlContractInputFormatter(null!, new NorseXmlOptions()));
		Should.Throw<ArgumentNullException>(() => new XmlContractInputFormatter(new XmlShapeRegistry(), null!));
	}
}
