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
		MetadataReference.CreateFromFile(typeof(Norse.Infrastructure.Web.Server.Xml.IXmlShape).Assembly.Location),
		.. ReferenceAssemblies.Bcl,
		.. ReferenceAssemblies.AspNetCore
	];

	/// <summary>
	/// Preview lang version — required from Task 6 onward, not by Task 5's own fixtures (diagnostics-
	/// only; never called a <c>Result&lt;T&gt;</c> member). <c>Result&lt;T&gt;</c>/<c>Success&lt;T&gt;</c>
	/// carry the <c>[Union]</c> C# 15 preview attribute, and generator-emitted writer code (Task 6) calls
	/// <c>Result&lt;T&gt;.TryGetValue(...)</c> directly — without this, the emitted source (parsed with
	/// whatever parse options this fixture compilation carries) fails CS8652 ("the feature 'unions' is
	/// currently in Preview") the moment it's added to the compilation, cascading into unrelated CS1061s.
	/// </summary>
	public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

	/// <summary>Builds the fixture compilation, stub base class included, unrun.</summary>
	public static Compilation CreateCompilation(params string[] sources) =>
		CSharpCompilation.Create(
			"Norse.Hosting.Web.Server",
			[.. new[] { StubGrpcControllerBase }.Concat(sources).Select(s => CSharpSyntaxTree.ParseText(s, ParseOptions))],
			ExtraReferences,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

	/// <summary>Runs the real generator once against fixture source, the stub base class included.</summary>
	public static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) Run(params string[] sources)
	{
		_ = CSharpGeneratorDriver.Create([new XmlShapeGenerator().AsSourceGenerator()], parseOptions: ParseOptions)
			.RunGeneratorsAndUpdateCompilation(CreateCompilation(sources), out var outputCompilation, out var diagnostics);
		return (diagnostics, outputCompilation);
	}

	public static ImmutableArray<Diagnostic> GenerateDiagnostics(params string[] sources) =>
		Run(sources).Diagnostics;
}
