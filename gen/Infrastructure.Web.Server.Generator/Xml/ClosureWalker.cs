using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Server.Generator.Xml;

/// <summary>
///     Walks one confirmed <c>GrpcControllerBase</c> descendant's action methods (spec §4.1): body-bound
///     parameter types seed the request closure, <c>ActionResult&lt;T&gt;</c>/<c>Task&lt;ActionResult&lt;T&gt;&gt;</c>/
///     <c>ValueTask&lt;ActionResult&lt;T&gt;&gt;</c> payload types seed the response closure. Every complex
///     type reachable from either seed set gets a <see cref="ShapeModel" />; every shape-law violation
///     along the way (NORSE022-028, plus the closure guards NORSE036 and NORSE037) becomes a
///     <see cref="DiagnosticInfo" />. Pure symbol-to-value-model
///     projection — nothing this type touches survives into the returned <see cref="ControllerShapeResult" />.
/// </summary>
static class ClosureWalker
{
	static readonly SymbolDisplayFormat _displayFormat = SymbolDisplayFormat.FullyQualifiedFormat;

	/// <summary>
	///     One elevated companion per ambient <see cref="Compilation" />, memoized in a
	///     <see cref="ConditionalWeakTable{TKey,TValue}" /> keyed on the compilation instance itself --
	///     entries fall out of scope with the compilation that produced them, so a long-running generator
	///     host (an IDE, not just a one-shot build) never accumulates unbounded entries.
	///     <c>Compilation.WithOptions</c> forces Roslyn to rebuild its reference manager
	///     (<c>MetadataImportOptions</c> participates in <c>CanReuseCompilationReferenceManager</c>) --
	///     expensive enough that doing it once per <see cref="Analyze" /> call (once per discovered
	///     controller, syntax or referenced) rather than once per ambient compilation would be a real cost
	///     on a host carrying many controllers. The table, not a per-call rebuild, is what makes that
	///     "once per compilation" true.
	/// </summary>
	static readonly ConditionalWeakTable<Compilation, Compilation> _elevatedCompilations = new();

	public static ControllerShapeResult Analyze(INamedTypeSymbol controller, Compilation compilation)
	{
		// NORSE037, ruled by Buvy 2026-08-09: facade controllers are namespace-level types. A
		// GrpcControllerBase descendant nested inside another type strikes here, at the single choke
		// point both discovery paths (syntax and referenced-assembly) flow through -- before any closure
		// walk begins, no shapes, no further analysis of the nested controller's actions. Loud diagnostic,
		// never silent exclusion (no-silent-fallbacks law): the shared ContractDiscovery.AllTypes nested-
		// type recursion still FINDS a nested controller (it serves gRPC contract and component discovery
		// and stays as-is) -- the law lives here, in the XML generator's controller handling, not in that
		// shared walker.
		if (controller.ContainingType is not null)
		{
			var diagnostic = DiagnosticInfo.Create(Diagnostics.NestedFacadeController, controller,
				controller.ToDisplayString(_displayFormat), controller.ContainingType.ToDisplayString(_displayFormat));
			return new ControllerShapeResult(EquatableArray<ShapeModel>.Empty,
				EquatableArray<DiagnosticInfo>.Create([diagnostic]));
		}

		var ctx = new TaxonomyContext(
			compilation.GetTypeByMetadataName("Norse.Primitives.Result`1"),
			compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1"),
			compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2"),
			compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2"),
			compilation.GetTypeByMetadataName("System.FlagsAttribute"),
			compilation.GetTypeByMetadataName("System.Runtime.Serialization.DataMemberAttribute"));

		var dataContractAttribute =
			compilation.GetTypeByMetadataName("System.Runtime.Serialization.DataContractAttribute");
		var fromBodyAttribute = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromBodyAttribute");
		var actionResultType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ActionResult`1");
		var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
		var valueTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
		INamedTypeSymbol?[] explicitBindingAttributes =
		[
			compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromRouteAttribute"),
			compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromQueryAttribute"),
			compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromHeaderAttribute"),
			compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromServicesAttribute")
		];

		List<DiagnosticInfo> diagnostics = [];
		List<(IParameterSymbol Parameter, INamedTypeSymbol Type)> requestRoots = [];
		List<INamedTypeSymbol> responseRoots = [];

		foreach (var method in controller.GetMembers().OfType<IMethodSymbol>())
		{
			if (method is not
				{ MethodKind: MethodKind.Ordinary, IsStatic: false, DeclaredAccessibility: Accessibility.Public })
				continue;

			foreach (var parameter in method.Parameters)
			{
				if (parameter.Type is not INamedTypeSymbol parameterType)
					continue;

				var hasExplicitOtherSource = explicitBindingAttributes.Any(a => HasAttribute(parameter, a));
				if (HasAttribute(parameter, fromBodyAttribute))
					requestRoots.Add((parameter, parameterType));
				else if (!hasExplicitOtherSource && method.Parameters.Length == 1 && !IsSupportedScalar(parameterType))
					requestRoots.Add((parameter, parameterType));
			}

			if (TryGetActionResultPayload(method.ReturnType, actionResultType, taskType, valueTaskType) is
				INamedTypeSymbol payload)
				responseRoots.Add(payload);
		}

		if (requestRoots.Count == 0 && responseRoots.Count == 0)
			return new ControllerShapeResult(EquatableArray<ShapeModel>.Empty, EquatableArray<DiagnosticInfo>.Empty);

		foreach (var (parameter, type) in requestRoots)
			if (dataContractAttribute is not null && !HasAttribute(type, dataContractAttribute))
				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.BodyTypeNotDataContract, parameter, parameter.Name,
					type.ToDisplayString(_displayFormat)));

		var requestReachable = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		var responseReachable = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		foreach (var (_, type) in requestRoots)
			Walk(type, requestReachable, ctx);
		foreach (var type in responseRoots)
			Walk(type, responseReachable, ctx);

		var crossDirection = new HashSet<INamedTypeSymbol>(requestReachable, SymbolEqualityComparer.Default);
		crossDirection.IntersectWith(responseReachable);

		foreach (var shared in crossDirection.OrderBy(t => t.ToDisplayString(_displayFormat), StringComparer.Ordinal))
			diagnostics.Add(DiagnosticInfo.Create(Diagnostics.SharedAcrossDirections, shared,
				shared.ToDisplayString(_displayFormat)));

		var allReachable = new HashSet<INamedTypeSymbol>(requestReachable, SymbolEqualityComparer.Default);
		allReachable.UnionWith(responseReachable);

		var shapes = BuildShapes(allReachable, requestReachable, crossDirection, ctx, compilation, diagnostics);

		return new ControllerShapeResult(EquatableArray<ShapeModel>.Create(shapes),
			EquatableArray<DiagnosticInfo>.Create(diagnostics));
	}

	static List<ShapeModel> BuildShapes(
		HashSet<INamedTypeSymbol> allReachable,
		HashSet<INamedTypeSymbol> requestReachable,
		HashSet<INamedTypeSymbol> crossDirection,
		TaxonomyContext ctx,
		Compilation compilation,
		List<DiagnosticInfo> diagnostics)
	{
		List<ShapeModel> shapes = [];

		// NORSE036 support (metadata-import correction): the ambient compilation defaults to
		// MetadataImportOptions.Public, under which Roslyn elides a referenced assembly's non-public
		// members from the symbol table entirely -- not merely marks them inaccessible -- whenever the
		// host carries no matching InternalsVisibleTo grant. `type.InstanceConstructors`/
		// `property.SetMethod` would silently come back empty/null for exactly the violation this
		// diagnostic exists to catch, indistinguishable from "legitimately has none". MetadataImportOptions
		// has three levels, not two: Public elides both internal and private; Internal restores internal
		// but still elides private (a `private set` referenced accessor slipped straight through an
		// earlier pass of this fix that only elevated to Internal); only All restores every accessibility.
		// One companion compilation per ambient compilation -- memoized below, not rebuilt per Analyze
		// call -- restores that visibility for the two checks below without disturbing the ambient
		// compilation the rest of this walk (and NORSE022-028) relies on. Source-declared types skip the
		// detour entirely -- source symbols are never filtered by import options, only PE metadata is.
		var elevatedCompilation = ElevateMetadataImport(compilation);

		foreach (var type in allReachable.OrderBy(t => t.ToDisplayString(_displayFormat), StringComparer.Ordinal))
		{
			if (!type.IsSealed || type.IsGenericType ||
				(type.BaseType is not null && type.BaseType.SpecialType != SpecialType.System_Object))
				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidContractShape, type,
					type.ToDisplayString(_displayFormat)));

			var probeType = ResolveForAccessibility(type, elevatedCompilation);
			var probeCompilation = ReferenceEquals(probeType, type) ? compilation : elevatedCompilation;

			CheckConstructorAccessibility(type, probeType, probeCompilation, diagnostics);

			var isCross = crossDirection.Contains(type);
			var isRequestSide = !isCross && requestReachable.Contains(type);

			List<(IPropertySymbol Property, MemberModel Model)> built = [];
			foreach (var property in GetInstanceProperties(type, ctx))
			{
				CheckAccessorAccessibility(property, type, probeType, probeCompilation, diagnostics);
				built.Add((property, ClassifyMember(property, type, isCross, isRequestSide, ctx, diagnostics)));
			}

			ReportUniquenessViolations(type, built, diagnostics);

			shapes.Add(new ShapeModel(
				type.ToDisplayString(_displayFormat),
				NameCasing.ApplyAll(type.Name),
				EquatableArray<MemberModel>.Create(built.Select(b => b.Model))));
		}

		return shapes;
	}

	static Compilation ElevateMetadataImport(Compilation compilation) =>
		_elevatedCompilations.GetValue(compilation, static c =>
			c.Options.MetadataImportOptions == MetadataImportOptions.All ?
				c :
				c.WithOptions(c.Options.WithMetadataImportOptions(MetadataImportOptions.All)));

	/// <summary>
	///     Re-resolves <paramref name="type" /> through <paramref name="elevatedCompilation" /> when it's a
	///     referenced-assembly (PE) symbol, so the accessibility checks below see the same type under
	///     <see cref="MetadataImportOptions.All" />. Source-declared types (an in-source
	///     <see cref="Location" /> present) are returned unchanged -- import options never filter source
	///     symbols, and re-resolving them would be pure overhead. Two silent fallbacks live here
	///     deliberately, not as an oversight: a <see langword="null" /> <see cref="ISymbol.ContainingAssembly" />
	///     (an error-type symbol -- the unresolved-<c>[FromBody]</c> path can produce one) and a failed
	///     assembly/metadata-name resolution both fall back to the original <paramref name="type" />, which
	///     degrades this call site back to the pre-elevation (pre-fix) blind behavior rather than crashing
	///     the generator (CS8785) mid-walk over an already-degenerate symbol. That degraded behavior is the
	///     documented cost of the fallback, not a silently-eaten one.
	/// </summary>
	static INamedTypeSymbol ResolveForAccessibility(INamedTypeSymbol type, Compilation elevatedCompilation)
	{
		if (type.Locations.Any(static l => l.IsInSource))
			return type;

		if (type.ContainingAssembly is not { } owningAssembly)
			return type;

		var assembly = elevatedCompilation.SourceModule.ReferencedAssemblySymbols
			.FirstOrDefault(a => a.Identity.Equals(owningAssembly.Identity));

		return assembly?.GetTypeByMetadataName(MetadataQualifiedName(type)) ?? type;
	}

	/// <summary>Builds the dotted-namespace, "+"-nested metadata name <see cref="IAssemblySymbol.GetTypeByMetadataName" /> expects.</summary>
	static string MetadataQualifiedName(INamedTypeSymbol type)
	{
		List<string> parts = [];
		for (var current = type; current is not null; current = current.ContainingType)
			parts.Insert(0, current.MetadataName);

		var name = string.Join("+", parts);
		return type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{name}" : name;
	}

	/// <summary>
	///     NORSE036, half one: the generated reader compiles <c>new {Contract} { Member = ... }</c> in the
	///     HOST assembly, so <paramref name="type" />'s parameterless constructor must be reachable from
	///     there — <c>Compilation.IsSymbolAccessibleWithin</c> honors an <c>InternalsVisibleTo</c> grant
	///     correctly, unlike a naive <c>DeclaredAccessibility</c> read, and same-assembly (syntax-path)
	///     contracts trivially pass because internal is always accessible within its own assembly; this
	///     check is never special-cased away for them. <paramref name="probeType" />/
	///     <paramref name="probeCompilation" /> are <paramref name="type" />/the ambient compilation for a
	///     source-declared type, or their <see cref="MetadataImportOptions.All" />-elevated equivalents for
	///     a referenced-assembly one (see <see cref="ResolveForAccessibility" />) — the lookup and the
	///     accessibility check both run against whichever pair actually has the constructor symbol in
	///     view. When the local <c>ctor</c> lookup comes back <see langword="null" /> even after elevation,
	///     the type genuinely declares no parameterless constructor at all (a positional record's primary
	///     constructor is the common real-world source) — a distinct message branch from "has one, but it's
	///     inaccessible", asserted separately below.
	/// </summary>
	static void CheckConstructorAccessibility(INamedTypeSymbol type, INamedTypeSymbol probeType,
		Compilation probeCompilation, List<DiagnosticInfo> diagnostics)
	{
		var ctor = probeType.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length == 0);
		if (ctor is not null && probeCompilation.IsSymbolAccessibleWithin(ctor, probeCompilation.Assembly))
			return;

		var typeName = type.ToDisplayString(_displayFormat);
		var message = ctor is null ?
			$"'{typeName}' has no parameterless constructor at all — the generated reader compiles 'new {typeName} {{ ... }}' in the host assembly, so the contract's construction surface must be reachable from it" :
			$"The parameterless constructor of '{typeName}' is not accessible from the host — the generated reader compiles 'new {typeName} {{ ... }}' in the host assembly, so the contract's construction surface must be reachable from it";

		diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ContractConstructionInaccessible,
			ctor is not null ? (ISymbol)ctor : type, message));
	}

	/// <summary>
	///     NORSE036, half two: the same reader initializes every wire member by name
	///     (<c>Member = ...</c>), so each member's <c>set</c>/<c>init</c> accessor must be reachable from
	///     the host exactly like the constructor above. A member with no setter at all (get-only) is a
	///     different, pre-existing concern this check does not widen into — <see cref="IPropertySymbol.SetMethod" />
	///     is <see langword="null" /> for those, and <see langword="null" /> is skipped here. After the
	///     <see cref="MetadataImportOptions.All" /> elevation (every accessibility level, not just
	///     internal), that null is finally trustworthy — it no longer conflates "genuinely no setter" with
	///     "setter elided by import options" at ANY accessibility, public through private.
	///     <paramref name="probeOwner" />/<paramref name="probeCompilation" /> mirror
	///     <see cref="CheckConstructorAccessibility" />'s pair; the property itself is re-looked-up by name
	///     on <paramref name="probeOwner" /> rather than reused from <paramref name="owner" />, since a
	///     property symbol carries its declaring type's import-options view with it. That lookup is
	///     narrowed to the public property specifically — under <c>All</c> import, a same-named private
	///     member (shadowing, or an explicit interface implementation's backing property) could otherwise
	///     surface and get probed instead of the actual wire member; <see cref="GetInstanceProperties" />
	///     only ever admits public properties into the closure in the first place, so the probe must match
	///     that same admission rule. A failed lookup (no matching public property found under elevation, an
	///     edge case reachable only via a malformed/adversarial metadata shape) falls back to
	///     <see langword="null" /> deliberately — <c>setter is null</c> below then skips the check exactly
	///     as it would pre-elevation, rather than crashing the generator over a symbol this walk can't
	///     make sense of.
	/// </summary>
	static void CheckAccessorAccessibility(IPropertySymbol property, INamedTypeSymbol owner,
		INamedTypeSymbol probeOwner, Compilation probeCompilation, List<DiagnosticInfo> diagnostics)
	{
		var probeProperty = ReferenceEquals(probeOwner, owner) ?
			property :
			probeOwner.GetMembers(property.Name).OfType<IPropertySymbol>()
				.FirstOrDefault(static p => p.DeclaredAccessibility == Accessibility.Public);

		var setter = probeProperty?.SetMethod;
		if (setter is null || probeCompilation.IsSymbolAccessibleWithin(setter, probeCompilation.Assembly))
			return;

		var accessorKind = setter.IsInitOnly ?
			"init" :
			"set";
		var ownerName = owner.ToDisplayString(_displayFormat);
		var message =
			$"Member '{property.Name}' on '{ownerName}' has a '{accessorKind}' accessor that is not accessible from the host — the generated reader compiles 'new {ownerName} {{ {property.Name} = ... }}' in the host assembly, so every wire member's set/init accessor must be reachable from it";

		diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ContractConstructionInaccessible, setter, message));
	}

	static MemberModel ClassifyMember(IPropertySymbol property, INamedTypeSymbol owner, bool isCross,
		bool isRequestSide, TaxonomyContext ctx, List<DiagnosticInfo> diagnostics)
	{
		var classification = Classify(property.Type, ctx);

		if (classification.Problem != TaxonomyProblem.None)
		{
			diagnostics.Add(DiagnosticInfo.Create(Diagnostics.TaxonomyViolation, property,
				TaxonomyMessage(classification.Problem, property, owner)));
			return new MemberModel(property.Name, classification.Kind, NameCasing.ApplyAll(property.Name),
				classification.IsResultWrapped, classification.IsNullable, null, null, false, null,
				EquatableArray<EnumValueModel>.Empty);
		}

		if (classification.Kind == MemberKind.Scalar && !isCross)
		{
			if (isRequestSide && !classification.IsResultWrapped)
				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.RawScalarInRequestClosure, property, property.Name,
					owner.ToDisplayString(_displayFormat)));
			else if (!isRequestSide && classification.IsResultWrapped)
				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ResultInResponseClosure, property, property.Name,
					owner.ToDisplayString(_displayFormat)));
		}

		// Flags are legal in either closure, carried bare on the contract (design spec
		// 2026-08-02-futhark-enum-wire-law-design.md, Amendment 2026-08-09) — recorded as a member
		// trait the emitters translate into the repeated governed-name element shape, never a strike.
		// The enum table builds identically for flags and plain enums: one table, one algorithm (§2.3).
		var isEnum = classification.ScalarType is { TypeKind: TypeKind.Enum };
		var isFlags = isEnum && ctx.FlagsAttribute is not null &&
			((INamedTypeSymbol)classification.ScalarType!).GetAttributes().Any(a =>
				SymbolEqualityComparer.Default.Equals(a.AttributeClass, ctx.FlagsAttribute));
		var enumValues = isEnum ?
			BuildEnumTable((INamedTypeSymbol)classification.ScalarType!) :
			EquatableArray<EnumValueModel>.Empty;
		// FullyQualifiedFormat renders special types as their bare keywords ("int", "uint", ...) — the
		// exact strings WriterEmitter's zero-extension dispatch matches on.
		var enumUnderlyingTypeName = isEnum ?
			((INamedTypeSymbol)classification.ScalarType!).EnumUnderlyingType!.ToDisplayString(_displayFormat) :
			null;

		return new MemberModel(
			property.Name,
			classification.Kind,
			NameCasing.ApplyAll(property.Name),
			classification.IsResultWrapped,
			classification.IsNullable,
			classification.ScalarType?.ToDisplayString(_displayFormat),
			classification.ComplexType?.ToDisplayString(_displayFormat),
			isFlags,
			enumUnderlyingTypeName,
			enumValues);
	}

	static void ReportUniquenessViolations(INamedTypeSymbol owner,
		List<(IPropertySymbol Property, MemberModel Model)> built, List<DiagnosticInfo> diagnostics)
	{
		foreach (var group in built.Where(b => b.Model.Kind != MemberKind.Scalar && b.Model.ComplexTypeName is not null)
			.GroupBy(b => b.Model.ComplexTypeName, StringComparer.Ordinal))
			if (group.Count() > 1)
				foreach (var (duplicateProperty, duplicateModel) in group.Skip(1))
					diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MemberUniquenessViolation, duplicateProperty,
						$"'{owner.ToDisplayString(_displayFormat)}' carries more than one member of complex type '{duplicateModel.ComplexTypeName}' ('{duplicateProperty.Name}' collides with an earlier member) — one member per complex type per contract, any arity"));

		// One diagnostic per offending member, not one per colliding style: two names built from the
		// same word list (e.g. "UserId"/"UserID") collide in every one of the five styles at once —
		// reporting per-style would fire the same law five times over for a single naming mistake.
		for (var i = 1; i < built.Count; i++)
		{
			var (currentProperty, currentModel) = built[i];
			for (var earlier = 0; earlier < i; earlier++)
			{
				var (earlierProperty, earlierModel) = built[earlier];
				var collidingStyle = FirstCollidingStyle(currentModel.WireNames, earlierModel.WireNames);
				if (collidingStyle is null)
					continue;

				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MemberUniquenessViolation, currentProperty,
					$"'{owner.ToDisplayString(_displayFormat)}' has a wire-name collision between '{earlierProperty.Name}' and '{currentProperty.Name}' once case-transformed to {collidingStyle} ('{currentModel.WireNames[(int)collidingStyle.Value]}') — two members produce the same wire name"));
				break;
			}
		}

		// A flags member renders repeated elements named from the property itself (2026-08-09
		// amendment); every complex/collection member renders its element(s) named from its own type
		// (ShortName(ComplexTypeName), the exact NameCasing/ShortName transform WriterEmitter and
		// ReaderEmitter's field declarations use). Both live in the same per-contract XML element
		// namespace, so a flags property whose wire name matches a sibling complex/collection member's
		// type-derived element name produces two same-named dispatch arms with no diagnostic — cross-
		// check the two name sources here rather than folding them into either pairwise loop above,
		// which only ever compare within one name source (property-derived, or ComplexTypeName-only).
		var typeDerivedMembers = built.Where(b => b.Model.Kind != MemberKind.Scalar && b.Model.ComplexTypeName is not null)
			.ToList();
		foreach (var (flagsProperty, flagsModel) in built.Where(b => b.Model.IsFlagsEnum))
		{
			foreach (var (complexProperty, complexModel) in typeDerivedMembers)
			{
				var elementNames = NameCasing.ApplyAll(WriterEmitter.ShortName(complexModel.ComplexTypeName!));
				var collidingStyle = FirstCollidingStyle(flagsModel.WireNames, elementNames);
				if (collidingStyle is null)
					continue;

				diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MemberUniquenessViolation, flagsProperty,
					$"'{owner.ToDisplayString(_displayFormat)}' has a wire-name collision between flags member '{flagsProperty.Name}' and the type-derived element name of '{complexProperty.Name}' once case-transformed to {collidingStyle} ('{elementNames[(int)collidingStyle.Value]}') — a flags member's repeated element name must not collide with a complex/collection member's element name"));
				break;
			}
		}
	}

	static XmlCaseStyle? FirstCollidingStyle(EquatableArray<string> left, EquatableArray<string> right)
	{
		for (var style = 0; style < 5; style++)
			if (StringComparer.Ordinal.Equals(left[style], right[style]))
				return (XmlCaseStyle)style;

		return null;
	}

	static void Walk(INamedTypeSymbol root, HashSet<INamedTypeSymbol> reachable, TaxonomyContext ctx)
	{
		Queue<INamedTypeSymbol> queue = [];
		if (reachable.Add(root))
			queue.Enqueue(root);

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			foreach (var property in GetInstanceProperties(current, ctx))
			{
				var next = Classify(property.Type, ctx).ComplexType;
				if (next is not null && reachable.Add(next))
					queue.Enqueue(next);
			}
		}
	}

	// [DataMember] is an opt-in membership law (design spec §4b, plan Task 7): a property that never
	// carries the attribute does not exist to Futhark at all — no closure entry, no shape, no
	// diagnostic — mirroring the same law Midgard's JSON leg enforces via OptInContractModifier.
	static IEnumerable<IPropertySymbol> GetInstanceProperties(INamedTypeSymbol type, TaxonomyContext ctx) =>
		type.GetMembers().OfType<IPropertySymbol>().Where(p =>
			p is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public } &&
			HasAttribute(p, ctx.DataMemberAttribute));

	static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attribute) =>
		attribute is not null && symbol.GetAttributes()
			.Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));

	static INamedTypeSymbol? TryGetActionResultPayload(ITypeSymbol returnType, INamedTypeSymbol? actionResultType,
		INamedTypeSymbol? taskType, INamedTypeSymbol? valueTaskType)
	{
		if (returnType is not INamedTypeSymbol { IsGenericType: true } named)
			return null;

		if (actionResultType is not null &&
			SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, actionResultType))
			return named.TypeArguments[0] as INamedTypeSymbol;

		var isAsyncWrapper =
			(taskType is not null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, taskType)) ||
			(valueTaskType is not null &&
				SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, valueTaskType));
		if (!isAsyncWrapper)
			return null;

		if (named.TypeArguments[0] is not INamedTypeSymbol { IsGenericType: true } inner || actionResultType is null ||
			!SymbolEqualityComparer.Default.Equals(inner.OriginalDefinition, actionResultType))
			return null;

		return inner.TypeArguments[0] as INamedTypeSymbol;
	}

	static MemberClassification Classify(ITypeSymbol propertyType, TaxonomyContext ctx)
	{
		var (underlying, isNullable, isResultWrapped) = Unwrap(propertyType, ctx.ResultType);

		if (underlying.SpecialType == SpecialType.System_String)
			return new MemberClassification(MemberKind.Scalar, isResultWrapped, isNullable, underlying, null,
				TaxonomyProblem.None);

		if (IsDictionary(underlying, ctx))
			return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, null,
				TaxonomyProblem.Dictionary);

		if (TryGetEnumerableItemType(underlying, ctx.EnumerableOpen, out var itemType))
		{
			if (itemType.SpecialType != SpecialType.System_String && (IsDictionary(itemType, ctx) ||
				TryGetEnumerableItemType(itemType, ctx.EnumerableOpen, out _)))
				return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, null,
					TaxonomyProblem.NestedCollection);

			if (IsSupportedScalar(itemType))
				return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, null,
					TaxonomyProblem.ScalarCollection);

			if (itemType is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } complexItem)
				return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, complexItem,
					TaxonomyProblem.None);

			return new MemberClassification(MemberKind.Collection, isResultWrapped, isNullable, null, null,
				TaxonomyProblem.NestedCollection);
		}

		if (IsSupportedScalar(underlying))
			return new MemberClassification(MemberKind.Scalar, isResultWrapped, isNullable, underlying, null,
				TaxonomyProblem.None);

		if (underlying is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } complex)
			return new MemberClassification(MemberKind.Complex, isResultWrapped, isNullable, null, complex,
				TaxonomyProblem.None);

		return new MemberClassification(MemberKind.Scalar, isResultWrapped, isNullable, underlying, null,
			TaxonomyProblem.UnsupportedScalar);
	}

	static (ITypeSymbol Underlying, bool IsNullable, bool IsResultWrapped) Unwrap(ITypeSymbol type,
		INamedTypeSymbol? resultType)
	{
		if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
		{
			var inner = nullable.TypeArguments[0];
			if (resultType is not null && inner is INamedTypeSymbol { IsGenericType: true } innerNamed &&
				SymbolEqualityComparer.Default.Equals(innerNamed.OriginalDefinition, resultType))
				return (innerNamed.TypeArguments[0], true, true);

			return (inner, true, false);
		}

		if (resultType is not null && type is INamedTypeSymbol { IsGenericType: true } named &&
			SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, resultType))
			return (named.TypeArguments[0], false, true);

		return (type, type is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.Annotated }, false);
	}

	static bool IsDictionary(ITypeSymbol type, TaxonomyContext ctx)
	{
		bool Matches(INamedTypeSymbol candidate) =>
			(ctx.DictionaryOpen is not null &&
				SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, ctx.DictionaryOpen)) ||
			(ctx.ReadOnlyDictionaryOpen is not null &&
				SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, ctx.ReadOnlyDictionaryOpen));

		if (type is INamedTypeSymbol { IsGenericType: true } self && Matches(self))
			return true;

		return type.AllInterfaces.Any(i => i.IsGenericType && Matches(i));
	}

	static bool TryGetEnumerableItemType(ITypeSymbol type, INamedTypeSymbol? enumerableOpen, out ITypeSymbol itemType)
	{
		if (enumerableOpen is not null)
		{
			if (type is INamedTypeSymbol { IsGenericType: true } self &&
				SymbolEqualityComparer.Default.Equals(self.OriginalDefinition, enumerableOpen))
			{
				itemType = self.TypeArguments[0];
				return true;
			}

			foreach (var i in type.AllInterfaces)
			{
				if (i.IsGenericType && SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, enumerableOpen))
				{
					itemType = i.TypeArguments[0];
					return true;
				}
			}
		}

		itemType = null!;
		return false;
	}

	static bool IsSupportedScalar(ITypeSymbol type)
	{
		if (type.TypeKind == TypeKind.Enum)
			return true;

		return type.SpecialType is
				SpecialType.System_Boolean or
				SpecialType.System_SByte or SpecialType.System_Byte or
				SpecialType.System_Int16 or SpecialType.System_UInt16 or
				SpecialType.System_Int32 or SpecialType.System_UInt32 or
				SpecialType.System_Int64 or SpecialType.System_UInt64 or
				SpecialType.System_Decimal or
				SpecialType.System_Single or SpecialType.System_Double or
				SpecialType.System_Char or
				SpecialType.System_String
			|| IsKnownScalarStruct(type);
	}

	static bool IsKnownScalarStruct(ITypeSymbol type) =>
		type is INamedTypeSymbol { ContainingNamespace.Name: "System" } named &&
		named.Name is "Guid" or "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan";

	static EquatableArray<EnumValueModel> BuildEnumTable(INamedTypeSymbol enumType) =>
		EquatableArray<EnumValueModel>.Create(
			enumType.GetMembers().OfType<IFieldSymbol>()
				.Where(f => f is { IsConst: true, HasConstantValue: true })
				.Select(f => new EnumValueModel(f.Name, NameCasing.ApplyAll(f.Name), ToBits(f.ConstantValue!))));

	/// <summary>
	///     Zero-extends a boxed enum-member constant into the shared 64-bit table representation — the
	///     build-time twin of the runtime law (<c>EnumLexical.ToBits</c>): 1/2/4-byte underlying types
	///     zero-extend through the unsigned same-width type, 8-byte types carry their bits identically
	///     (bit 63 genuinely is the sign bit there). A bare <c>Convert.ToInt64</c> would sign-extend
	///     instead, landing an int-backed <c>1 &lt;&lt; 31</c> member at -2147483648L — which fails every
	///     downstream single-bit test (generation-time mask and emitted table alike) and misclassifies
	///     the member composite.
	/// </summary>
	static long ToBits(object constantValue) => constantValue switch
	{
		sbyte value => unchecked((byte)value),
		byte value => value,
		short value => unchecked((ushort)value),
		ushort value => value,
		int value => unchecked((uint)value),
		uint value => value,
		long value => value,
		ulong value => unchecked((long)value),
		_ => Convert.ToInt64(constantValue, CultureInfo.InvariantCulture)
	};

	static string TaxonomyMessage(TaxonomyProblem problem, IPropertySymbol property, INamedTypeSymbol owner) =>
		problem switch
		{
			TaxonomyProblem.UnsupportedScalar =>
				$"Member '{property.Name}' on '{owner.ToDisplayString(_displayFormat)}' has type '{property.Type.ToDisplayString(_displayFormat)}', which is outside Futhark's closed scalar taxonomy",
			TaxonomyProblem.Dictionary =>
				$"Member '{property.Name}' on '{owner.ToDisplayString(_displayFormat)}' is a dictionary — dictionaries have no Futhark shape",
			TaxonomyProblem.ScalarCollection =>
				$"Member '{property.Name}' on '{owner.ToDisplayString(_displayFormat)}' is a collection of scalars — collection items must be complex types",
			TaxonomyProblem.NestedCollection =>
				$"Member '{property.Name}' on '{owner.ToDisplayString(_displayFormat)}' is a collection of collections (or a collection of dictionaries) — nested collections have no Futhark shape",
			_ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unrecognized TaxonomyProblem.")
		};

	readonly record struct TaxonomyContext(
		INamedTypeSymbol? ResultType,
		INamedTypeSymbol? EnumerableOpen,
		INamedTypeSymbol? DictionaryOpen,
		INamedTypeSymbol? ReadOnlyDictionaryOpen,
		INamedTypeSymbol? FlagsAttribute,
		INamedTypeSymbol? DataMemberAttribute);

	readonly record struct MemberClassification(
		MemberKind Kind,
		bool IsResultWrapped,
		bool IsNullable,
		ITypeSymbol? ScalarType,
		INamedTypeSymbol? ComplexType,
		TaxonomyProblem Problem);

	enum TaxonomyProblem
	{
		None,
		UnsupportedScalar,
		Dictionary,
		ScalarCollection,
		NestedCollection
	}
}
