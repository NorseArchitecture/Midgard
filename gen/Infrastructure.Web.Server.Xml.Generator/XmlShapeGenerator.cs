using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Norse.Infrastructure.Web.Server.Xml.Generator;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
/// Discovers facade controllers (<c>GrpcControllerBase</c> descendants, spec §4) in the host
/// compilation and enforces Futhark's XML shape law (NORSE022-028) over their request/response
/// closures at build time. Diagnostics only in this slice — shape emission is a later increment,
/// gated on the same discovery this generator already performs.
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

		context.RegisterSourceOutput(controllerShapes.Collect(), static (productionContext, results) =>
		{
			foreach (var result in results)
				foreach (var diagnostic in result.Diagnostics)
					productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
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
