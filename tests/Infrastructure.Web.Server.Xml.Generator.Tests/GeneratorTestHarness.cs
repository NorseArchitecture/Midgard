using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Infrastructure.Web.Server.Xml.Generator.Tests;

/// <summary>
/// Shared compilation-harness helpers for <see cref="XmlShapeGenerator"/> tests — mirrors the sibling
/// <c>Infrastructure.Web.Server.Generator.Tests</c> harness idiom (compile fixture source through the
/// real generator via <see cref="CSharpGeneratorDriver"/>, inspect diagnostics/output).
/// <c>GrpcControllerBase</c> does not exist on the platform until Task 10 (a different repo, Asgard) —
/// <see cref="StubGrpcControllerBase"/> supplies the exact metadata name
/// (<c>Norse.Abstractions.Web.Server.Facade.GrpcControllerBase</c>) the generator keys on, deriving
/// from the real ASP.NET Core <c>ControllerBase</c> so fixture controllers behave exactly like the
/// eventual real base class.
/// </summary>
static class GeneratorTestHarness
{
	public const string StubGrpcControllerBase = """
		namespace Norse.Abstractions.Web.Server.Facade;

		public abstract class GrpcControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase
		{
		}
		""";

	public static readonly MetadataReference[] ExtraReferences =
	[
		MetadataReference.CreateFromFile(typeof(Norse.Primitives.Result<>).Assembly.Location),
		.. ReferenceAssemblies.Bcl,
		.. ReferenceAssemblies.AspNetCore
	];

	/// <summary>Builds the fixture compilation, stub base class included, unrun.</summary>
	public static Compilation CreateCompilation(params string[] sources) =>
		CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[.. new[] { StubGrpcControllerBase }.Concat(sources).Select(s => CSharpSyntaxTree.ParseText(s))],
			ExtraReferences,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

	/// <summary>Runs the real generator once against fixture source, the stub base class included.</summary>
	public static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) Run(params string[] sources)
	{
		_ = CSharpGeneratorDriver.Create([new XmlShapeGenerator().AsSourceGenerator()])
			.RunGeneratorsAndUpdateCompilation(CreateCompilation(sources), out var outputCompilation, out var diagnostics);
		return (diagnostics, outputCompilation);
	}

	public static ImmutableArray<Diagnostic> GenerateDiagnostics(params string[] sources) =>
		Run(sources).Diagnostics;
}
