using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;

namespace Norse.Infrastructure.Web.Server.Xml.Generator;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
/// Discovers facade controllers (<c>GrpcControllerBase</c> descendants, spec §4) in the host
/// compilation, enforces Futhark's XML shape law (NORSE022-028) over their request/response closures
/// at build time, and — new as of Task 6 — emits the canonical writer (<see cref="WriterEmitter"/>)
/// for every distinct reachable contract shape. Reader emission (a later task) will extend the same
/// emitted classes; this generator does not yet make them fully functional, only fully compiling.
/// </summary>
/// <remarks>
/// <b>Incremental pipeline shape is load-bearing (spec §2, plan Task 5):</b> there is no attribute to
/// hang <c>ForAttributeWithMetadataName</c> on — the base class is the key — so a naive
/// syntax-provider-plus-semantic-walk would re-run the full closure walk on every keystroke anywhere
/// in the host. Instead: the syntax predicate is a cheap, semantics-free structural check (a class
/// declaration with a non-empty base list); the transform confirms the <c>GrpcControllerBase</c>
/// metadata name and returns <see langword="null"/> immediately for anything else, before the
/// (comparatively expensive) closure walk ever runs; and <see cref="ControllerShapeResult"/> —
/// carried through <see cref="ShapeModel"/> and <see cref="DiagnosticInfo"/> — is fully equatable and
/// symbol-free, so an edit to an unrelated file leaves the untouched controller's syntax tree
/// byte-identical across driver runs and the step reports <see cref="IncrementalStepRunReason.Cached"/>
/// without the transform running again at all.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class XmlShapeGenerator : IIncrementalGenerator
{
	const string GrpcControllerBaseMetadataName = "Norse.Abstractions.Web.Server.Facade.GrpcControllerBase";

	/// <summary>The tracking name asserted against in the incrementality test — <c>GeneratorDriverRunResult.Results[0].TrackedSteps["ControllerShapes"]</c>.</summary>
	internal const string ControllerShapesTrackingName = "ControllerShapes";

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var controllerShapes = context.SyntaxProvider.CreateSyntaxProvider(
				predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
				transform: static (ctx, cancellationToken) => Transform(ctx, cancellationToken))
			.Where(static result => result is not null)
			.Select(static (result, _) => result!.Value)
			.WithTrackingName(ControllerShapesTrackingName);

		// The host's root namespace, projected down to a single equatable string — a lighter touch
		// than the sibling generator's full CompilationProvider.Select(Discover), and deliberately so:
		// the incrementality guarantee this generator exists to prove (see the class remarks and
		// IncrementalCachingTests) belongs to the ControllerShapes step above; this second, independent
		// pipeline node only ever recomputes a cheap string, never re-runs the closure walk.
		var rootNamespace = context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? "Norse.Generated");

		context.RegisterSourceOutput(controllerShapes.Collect().Combine(rootNamespace), static (productionContext, pair) =>
		{
			var (results, hostRootNamespace) = pair;

			var hasErrors = false;
			foreach (var result in results)
				foreach (var diagnostic in result.Diagnostics)
				{
					productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
					hasErrors = true;
				}

			// Every NORSE022-028 diagnostic is an error — "you cannot compile an exposure Futhark
			// cannot round-trip" (spec §2.2). A shape built alongside a reported violation can carry
			// null ScalarTypeName/ComplexTypeName on the offending member (ClosureWalker.ClassifyMember),
			// which WriterEmitter has no defined behavior for — skip emission entirely rather than risk
			// the generator crashing (CS8785) over a shape the build was already going to reject.
			if (hasErrors)
				return;

			// One shape class per distinct contract type, globally — not per (controller, shape) pair.
			// The same complex type is legitimately reachable from more than one controller's closure;
			// emitting it once per controller would double-declare the same class name and fail the
			// host build with CS0101. Content is expected to be identical for the same TypeName (it's
			// the same CLR type observed twice), so last-write-wins (.Last()) is safe.
			var distinctShapes = results
				.SelectMany(static r => r.Shapes)
				.GroupBy(static s => s.TypeName, StringComparer.Ordinal)
				.Select(static g => g.Last())
				.OrderBy(static s => s.TypeName, StringComparer.Ordinal);

			foreach (var shape in distinctShapes)
			{
				var shortName = WriterEmitter.ShortName(shape.TypeName);
				productionContext.AddSource($"{shortName}XmlShape.g.cs", SourceText.From(WriterEmitter.Emit(hostRootNamespace, shape), Utf8NoBom.Encoding));
			}
		});
	}

	static ControllerShapeResult? Transform(GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node, cancellationToken) is not INamedTypeSymbol classSymbol)
			return null;

		var compilation = ctx.SemanticModel.Compilation;
		var controllerBase = compilation.GetTypeByMetadataName(GrpcControllerBaseMetadataName);
		if (controllerBase is null || !DerivesFrom(classSymbol, controllerBase))
			return null;

		return ClosureWalker.Analyze(classSymbol, compilation);
	}

	static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
	{
		for (var current = type.BaseType; current is not null; current = current.BaseType)
			if (SymbolEqualityComparer.Default.Equals(current, baseType))
				return true;

		return false;
	}
}
