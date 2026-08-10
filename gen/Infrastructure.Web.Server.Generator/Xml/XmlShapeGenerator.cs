using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;
using Norse.Infrastructure.Web.Grpc.Generator.Shared;

namespace Norse.Infrastructure.Web.Server.Generator.Xml;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
///     Discovers facade controllers (<c>GrpcControllerBase</c> descendants, spec §4) in the host
///     compilation and — per the spec's 2026-08-09 amendment — in the host's reference closure, enforces
///     Futhark's XML shape law (NORSE022-028) over their request/response closures at build time, and —
///     new as of Task 6 — emits the canonical writer (<see cref="WriterEmitter" />) for every distinct
///     reachable contract shape. Reader emission (a later task) will extend the same emitted classes;
///     this generator does not yet make them fully functional, only fully compiling.
/// </summary>
/// <remarks>
///     <b>Incremental pipeline shape is load-bearing (spec §2, plan Task 5):</b> there is no attribute to
///     hang <c>ForAttributeWithMetadataName</c> on — the base class is the key — so a naive
///     syntax-provider-plus-semantic-walk would re-run the full closure walk on every keystroke anywhere
///     in the host. Instead: the syntax predicate is a cheap, semantics-free structural check (a class
///     declaration with a non-empty base list); the transform confirms the <c>GrpcControllerBase</c>
///     metadata name and returns <see langword="null" /> immediately for anything else, before the
///     (comparatively expensive) closure walk ever runs; and <see cref="ControllerShapeResult" /> —
///     carried through <see cref="ShapeModel" /> and <see cref="DiagnosticInfo" /> — is fully equatable and
///     symbol-free, so an edit to an unrelated file leaves the untouched controller's syntax tree
///     byte-identical across driver runs and the step reports <see cref="IncrementalStepRunReason.Cached" />
///     without the transform running again at all.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class XmlShapeGenerator : IIncrementalGenerator
{
	const string GrpcControllerBaseMetadataName = "Norse.Abstractions.Web.Server.Facade.GrpcControllerBase";

	/// <summary>
	///     The tracking name asserted against in the incrementality test —
	///     <c>GeneratorDriverRunResult.Results[0].TrackedSteps["ControllerShapes"]</c>.
	/// </summary>
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

		// The reference-closure branch (spec amendment 2026-08-09): facade controllers compiled into a
		// referenced realm assembly are discovered by metadata symbol walk, mirroring the sibling
		// GrpcServerRegistrationGenerator's CompilationProvider.Select(Discover) shape. This node
		// re-runs on every compilation change — the accepted cost of the precedent — but its output is
		// fully equatable and symbol-free (the same ControllerShapeResult the syntax branch produces),
		// so an unchanged reference closure leaves the merge node below cached. The syntax branch above
		// keeps sole ownership of the host's own source; this walk excludes the compilation's own
		// assembly by construction (ReferencedAssemblySymbols never contains it).
		var referencedControllerShapes = context.CompilationProvider
			.Select(static (compilation, cancellationToken) => DiscoverReferenced(compilation, cancellationToken));

		// The host's root namespace, projected down to a single equatable string — a lighter touch
		// than the sibling generator's full CompilationProvider.Select(Discover), and deliberately so:
		// the incrementality guarantee this generator exists to prove (see the class remarks and
		// IncrementalCachingTests) belongs to the ControllerShapes step above; this independent
		// pipeline node only ever recomputes a cheap string, never re-runs the closure walk.
		var rootNamespace =
			context.CompilationProvider.Select(static (compilation, _) =>
				compilation.AssemblyName ?? "Norse.Generated");

		context.RegisterSourceOutput(controllerShapes.Collect().Combine(referencedControllerShapes).Combine(rootNamespace),
			static (productionContext, pair) =>
			{
				var ((sourceResults, referencedResults), hostRootNamespace) = pair;

				// Both discovery branches merge here, before the distinct-by-TypeName grouping below —
				// a contract type reachable from a source controller AND a referenced-assembly
				// controller is still one shape, one emitted class.
				ControllerShapeResult[] results = [.. sourceResults, .. referencedResults];

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
					.OrderBy(static s => s.TypeName, StringComparer.Ordinal)
					.ToList();

				// NORSE035: two DISTINCT contract types can still collide once reduced to WriterEmitter's
				// unqualified ShortName — trivially reachable now that reference-closure discovery merges
				// independent realms (two realms' own "Order" contracts, say). Left unchecked, that collision
				// either double-adds the same AddSource hint (a generator exception, CS8785) or emits two
				// classes under the same name (CS0101) — an honest diagnostic beats either crash. Location.None:
				// this is a closure-level fact spanning two shapes, not a single source site.
				var shortNameGroups = distinctShapes
					.GroupBy(static s => WriterEmitter.ShortName(s.TypeName), StringComparer.Ordinal)
					.ToList();
				var collidingShortNames = shortNameGroups.Where(static g => g.Count() > 1).ToList();
				if (collidingShortNames.Count > 0)
				{
					var collidingTypeNames = new HashSet<string>(StringComparer.Ordinal);
					foreach (var group in collidingShortNames)
					{
						foreach (var shape in group)
							collidingTypeNames.Add(shape.TypeName);

						var typeNames = string.Join(", ",
							group.Select(static s => s.TypeName).OrderBy(static n => n, StringComparer.Ordinal));
						productionContext.ReportDiagnostic(Diagnostic.Create(
							Diagnostics.DuplicateShapeShortName, Location.None, group.Key, typeNames));
					}

					// Excluded from shape-class emission AND both registration emitters below — the
					// non-colliding shapes in the same run are otherwise unaffected and still emit.
					distinctShapes = [.. distinctShapes.Where(s => !collidingTypeNames.Contains(s.TypeName))];
				}

				foreach (var shape in distinctShapes)
				{
					var shortName = WriterEmitter.ShortName(shape.TypeName);
					productionContext.AddSource($"{shortName}XmlShape.g.cs",
						SourceText.From(WriterEmitter.Emit(hostRootNamespace, shape), Utf8NoBom.Encoding));
				}

				// Task 8: one registration summary per host compilation, listing every shape just emitted
				// above — always produced, even with zero shapes, since the host calls
				// AddNorseXml(style, NorseXmlShapeRegistration.Build()) unconditionally.
				productionContext.AddSource("NorseXmlShapeRegistration.g.cs",
					SourceText.From(RegistrationEmitter.Emit(hostRootNamespace, distinctShapes), Utf8NoBom.Encoding));

				// Task 8: the sibling enum name/value registration -- one EnumNameTable per distinct enum
				// type reachable from any emitted shape's members, always produced (even with zero enums),
				// since every generated shape's Write/Read may reference NorseEnumNameRegistration.*Table
				// fields for its own enum-typed members.
				productionContext.AddSource("NorseEnumNameRegistration.g.cs",
					SourceText.From(RegistrationEmitter.EmitEnumRegistration(hostRootNamespace, distinctShapes),
						Utf8NoBom.Encoding));
			});
	}

	static ControllerShapeResult? Transform(GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node, cancellationToken) is not
			INamedTypeSymbol classSymbol)
			return null;

		var compilation = ctx.SemanticModel.Compilation;
		var controllerBase = compilation.GetTypeByMetadataName(GrpcControllerBaseMetadataName);
		if (controllerBase is null || !DerivesFrom(classSymbol, controllerBase))
			return null;

		return ClosureWalker.Analyze(classSymbol, compilation);
	}

	/// <summary>
	///     Walks every referenced assembly's global namespace (<c>ContractDiscovery.AllTypes</c>'s
	///     recursive shape) for <c>GrpcControllerBase</c> descendants and runs each through the same
	///     <see cref="ClosureWalker" /> the syntax branch uses — metadata-sourced symbols walk through it
	///     identically, and any diagnostic they trip reports at <see cref="Location.None" /> (via
	///     <see cref="LocationInfo.None" />, the symbol having no source location). Cheap bail-outs: no
	///     <c>GrpcControllerBase</c> resolvable means no facade controller can exist anywhere in the
	///     closure, and BCL/framework assemblies are skipped by name prefix before their namespaces are
	///     ever walked. <c>Compilation.IsSymbolAccessibleWithin</c> (2026-08-09 codex review hardening,
	///     finding 4) excludes a controller the host cannot legally name — an internal controller the
	///     referenced assembly hasn't granted <c>InternalsVisibleTo</c> to would otherwise emit a shape
	///     class whose generated source fails CS0122 the moment the host tries to compile it; the check
	///     honors that grant correctly, unlike a naive <c>DeclaredAccessibility</c> read.
	/// </summary>
	static EquatableArray<ControllerShapeResult> DiscoverReferenced(
		Compilation compilation, CancellationToken cancellationToken)
	{
		var controllerBase = compilation.GetTypeByMetadataName(GrpcControllerBaseMetadataName);
		if (controllerBase is null)
			return EquatableArray<ControllerShapeResult>.Empty;

		List<ControllerShapeResult> results = [];
		foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
		{
			if (IsNeverAFacadeAssembly(assembly.Name))
				continue;

			foreach (var type in ContractDiscovery.AllTypes(assembly.GlobalNamespace))
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (type.TypeKind == TypeKind.Class && DerivesFrom(type, controllerBase) &&
					compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
					results.Add(ClosureWalker.Analyze(type, compilation));
			}
		}

		return EquatableArray<ControllerShapeResult>.Create(results);
	}

	static bool IsNeverAFacadeAssembly(string assemblyName) =>
		assemblyName.StartsWith("System.", StringComparison.Ordinal) ||
		assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal) ||
		assemblyName.StartsWith("netstandard", StringComparison.Ordinal) ||
		assemblyName.StartsWith("mscorlib", StringComparison.Ordinal);

	static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
	{
		for (var current = type.BaseType; current is not null; current = current.BaseType)
			if (SymbolEqualityComparer.Default.Equals(current, baseType))
				return true;

		return false;
	}
}
