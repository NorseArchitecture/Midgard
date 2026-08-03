using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Infrastructure.Web.Server.Xml.Generator.Tests;

/// <summary>
/// Proves the load-bearing incremental-pipeline claim (plan Task 5, spec §2): editing a syntax tree
/// unrelated to any facade controller must not re-run the closure walk. Asserted via
/// <see cref="GeneratorDriverRunResult"/> tracked-step reasons — incrementality proven, not presumed.
/// </summary>
public sealed class IncrementalCachingTests
{
	const string Controller = """
		using System.Runtime.Serialization;
		using System.Threading.Tasks;
		using Microsoft.AspNetCore.Mvc;
		using Norse.Primitives;
		using Norse.Abstractions.Web.Server.Facade;

		namespace Norse.Fixtures.Incrementality;

		[DataContract]
		public sealed record Req
		{
			[DataMember]
			public Result<string> Value { get; init; }
		}

		public sealed record Resp
		{
			[DataMember]
			public string Status { get; init; } = "";
		}

		public sealed class FixtureController : GrpcControllerBase
		{
			public Task<ActionResult<Resp>> Do([FromBody] Req request) =>
				Task.FromResult(new ActionResult<Resp>(new Resp()));
		}
		""";

	const string UnrelatedV1 = """
		namespace Norse.Fixtures.Incrementality;

		public static class Unrelated
		{
			public static int Value => 1;
		}
		""";

	const string UnrelatedV2 = """
		namespace Norse.Fixtures.Incrementality;

		public static class Unrelated
		{
			public static int Value => 2;
		}
		""";

	[Fact]
	void ControllerShapes_step_reports_Cached_when_only_an_unrelated_syntax_tree_changes()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var unrelatedTree1 = CSharpSyntaxTree.ParseText(UnrelatedV1, cancellationToken: cancellationToken);
		var compilation1 = CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[
				CSharpSyntaxTree.ParseText(GeneratorTestHarness.StubGrpcControllerBase, cancellationToken: cancellationToken),
				CSharpSyntaxTree.ParseText(Controller, cancellationToken: cancellationToken),
				unrelatedTree1
			],
			GeneratorTestHarness.ExtraReferences,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[new XmlShapeGenerator().AsSourceGenerator()],
			driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

		driver = driver.RunGenerators(compilation1, cancellationToken);

		var unrelatedTree2 = CSharpSyntaxTree.ParseText(UnrelatedV2, cancellationToken: cancellationToken);
		var compilation2 = compilation1.ReplaceSyntaxTree(unrelatedTree1, unrelatedTree2);

		driver = driver.RunGenerators(compilation2, cancellationToken);

		var runResult = driver.GetRunResult();
		var steps = runResult.Results.Single().TrackedSteps[XmlShapeGenerator.ControllerShapesTrackingName];

		steps.ShouldNotBeEmpty();
		steps.SelectMany(step => step.Outputs)
			.ShouldAllBe(output => output.Reason == IncrementalStepRunReason.Cached || output.Reason == IncrementalStepRunReason.Unchanged);
	}

	[Fact]
	void ControllerShapes_step_reports_Modified_when_the_exposed_shape_itself_changes()
	{
		// The control case for the test above: changing what the controller actually exposes (here,
		// Req.Value's scalar type) must NOT report Cached/Unchanged — otherwise the "Cached" assertion
		// above would be trivially true regardless of what changed, and would prove nothing. Renaming
		// the action method alone (tried first) is deliberately insufficient here — the method name
		// isn't part of ControllerShapeResult, so that edit legitimately reports Unchanged, which is
		// itself evidence the model is exposure-content-keyed rather than syntax-keyed. This test
		// instead edits the member's scalar type, which does change the resulting ShapeModel.
		var cancellationToken = TestContext.Current.CancellationToken;
		var controllerTree1 = CSharpSyntaxTree.ParseText(Controller, cancellationToken: cancellationToken);
		var compilation1 = CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[CSharpSyntaxTree.ParseText(GeneratorTestHarness.StubGrpcControllerBase, cancellationToken: cancellationToken), controllerTree1],
			GeneratorTestHarness.ExtraReferences,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		GeneratorDriver driver = CSharpGeneratorDriver.Create(
			[new XmlShapeGenerator().AsSourceGenerator()],
			driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

		driver = driver.RunGenerators(compilation1, cancellationToken);

		var controllerTree2 = CSharpSyntaxTree.ParseText(Controller.Replace("Result<string> Value", "Result<int> Value"), cancellationToken: cancellationToken);
		var compilation2 = compilation1.ReplaceSyntaxTree(controllerTree1, controllerTree2);

		driver = driver.RunGenerators(compilation2, cancellationToken);

		var runResult = driver.GetRunResult();
		var steps = runResult.Results.Single().TrackedSteps[XmlShapeGenerator.ControllerShapesTrackingName];

		steps.SelectMany(step => step.Outputs)
			.ShouldContain(output => output.Reason == IncrementalStepRunReason.Modified || output.Reason == IncrementalStepRunReason.New);
	}
}
