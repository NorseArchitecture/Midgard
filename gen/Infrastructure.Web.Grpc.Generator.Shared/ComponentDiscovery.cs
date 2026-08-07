using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Grpc.Generator.Shared;

// Linked into both Infrastructure.Web.Server.Generator and Infrastructure.Web.Client.Generator via
// <Compile Include> -- same shared-source-per-consumer shape as ContractDiscovery.cs, for the same
// reason (Roslyn generators can't reference other analyzer-only assemblies). Transport-agnostic
// discovery of FluentValidation validators and Blazor-routable assemblies for the generator heads
// (Tasks 4/5) to emit host registration/composition code from. Validator discovery and route
// discovery are independent concerns -- a compilation without FluentValidation referenced still
// yields route results, and a compilation without Norse.Hosting.Web.Components.Routes referenced
// still yields validator results, so non-Yggdrasil consumers get validators-only output.
static class ComponentDiscovery
{
	const string ValidatorInterfaceMetadataName = "FluentValidation.IValidator`1";
	const string RouteAttributeMetadataName = "Microsoft.AspNetCore.Components.RouteAttribute";
	const string RoutesMetadataName = "Norse.Hosting.Web.Components.Routes";
	const string RoutesAdditionalAssembliesMetadataName = "Norse.Hosting.Web.Components.RoutesAdditionalAssemblies";

	/// <summary>
	/// Discovers every FluentValidation validator and Blazor-routable assembly visible to
	/// <paramref name="compilation"/> -- its own assembly plus every referenced assembly, mirroring
	/// <c>ContractDiscovery</c>'s walk (<c>compilation.Assembly</c> plus
	/// <c>compilation.SourceModule.ReferencedAssemblySymbols</c>, each swept via a recursive walk of
	/// its global namespace). References <c>ContractDiscovery</c> by name only (no <c>cref</c>) --
	/// not every consumer linking this file also links ContractDiscovery.cs.
	/// </summary>
	public static ComponentDiscoveryResult Discover(Compilation compilation)
	{
		var format = SymbolDisplayFormat.FullyQualifiedFormat;
		IAssemblySymbol[] assemblies = [compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols];

		var validators = DiscoverValidators(compilation, assemblies, format);
		var (routableMarkers, routesHolderMarker, routesHolderIsOwnAssembly, ownAssemblyRoutableMarker) = DiscoverRoutes(compilation, assemblies, format);
		var routesAdditionalAssembliesTypeExists = compilation.GetTypeByMetadataName(RoutesAdditionalAssembliesMetadataName) is not null;

		return new ComponentDiscoveryResult(validators, routableMarkers, routesHolderMarker, routesHolderIsOwnAssembly, routesAdditionalAssembliesTypeExists, ownAssemblyRoutableMarker);
	}

	/// <summary>
	/// A non-abstract named type implementing <c>FluentValidation.IValidator&lt;T&gt;</c>, matched by
	/// symbol on the interface's original definition. Empty (not an error) when FluentValidation isn't
	/// referenced. Two further guards keep a discovered validator usable by the emitted registration
	/// code rather than merely discoverable: <c>Compilation.IsSymbolAccessibleWithin</c> excludes a
	/// validator the discovering compilation can't legally reference via <c>typeof(...)</c>
	/// (own-assembly internal types pass this check for free -- same assembly means accessible-to-self
	/// -- so this is only ever a restriction on referenced-assembly validators), and <see
	/// cref="HasAccessiblePublicInstanceConstructor"/> excludes a validator
	/// Microsoft.Extensions.DependencyInjection could reference but never actually construct (its
	/// reflection-based activation only ever sees public constructors -- an explicitly-declared
	/// internal, protected, or no-modifier (private, for a class member) constructor fails this even
	/// for an own-assembly validator, though a fully implicit, no-constructor-declared class does not:
	/// the compiler emits that constructor as IL-public regardless of the containing type's own
	/// accessibility).
	/// </summary>
	static ImmutableArray<ValidatorModel> DiscoverValidators(Compilation compilation, IAssemblySymbol[] assemblies, SymbolDisplayFormat format)
	{
		var validatorInterface = compilation.GetTypeByMetadataName(ValidatorInterfaceMetadataName);
		if (validatorInterface is null)
			return [];

		return
		[
			.. assemblies
				.SelectMany(a => AllTypes(a.GlobalNamespace))
				// IsGenericType excludes open generic validator definitions (e.g. FluentValidation's own
				// InlineValidator<T>, always present once FluentValidation itself is a referenced
				// assembly) -- Tasks 4/5 need a concrete, closed request type to key a
				// typeof(IValidator<TRequest>) registration on; an open T can't back one.
				.Where(t => t is { TypeKind: TypeKind.Class, IsAbstract: false, IsGenericType: false })
				.Where(t => compilation.IsSymbolAccessibleWithin(t, compilation.Assembly))
				.Where(HasAccessiblePublicInstanceConstructor)
				// A validator implementing IValidator<T> more than once (legal, if unusual, in
				// FluentValidation) gets one ValidatorModel per implemented interface -- otherwise every
				// T past the first silently never gets a registration.
				.SelectMany(t => ValidatedRequestTypes(t, validatorInterface)
					.Select(request => new ValidatorModel(t.ToDisplayString(format), request.ToDisplayString(format))))
				.OrderBy(v => v.ValidatorTypeName, StringComparer.Ordinal)
				.ThenBy(v => v.RequestTypeName, StringComparer.Ordinal)
		];
	}

	/// <summary>
	/// DI resolves a validator via reflection-based activation, which only ever considers public
	/// instance constructors (<c>Type.GetConstructors()</c>'s default, no-<c>BindingFlags</c> overload)
	/// -- an explicitly-declared internal, protected, or no-modifier (private, for a class member)
	/// constructor is invisible to it even when the validator type itself is perfectly accessible (e.g.
	/// a public validator class with only a private constructor). A class with NO constructor declared
	/// at all is a different case, deliberately not excluded here: the C# compiler emits that implicit
	/// constructor as IL-public regardless of the containing type's own accessibility (verified against
	/// real <c>Type.GetConstructors()</c>/<c>Activator.CreateInstance</c> reflection behavior, not just
	/// <see cref="Accessibility"/> naming), so <see cref="INamedTypeSymbol.InstanceConstructors"/>
	/// already reports it as <see cref="Accessibility.Public"/> and this filter needs no special case
	/// for it.
	/// </summary>
	static bool HasAccessiblePublicInstanceConstructor(INamedTypeSymbol type) =>
		type.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public);

	static IEnumerable<ITypeSymbol> ValidatedRequestTypes(INamedTypeSymbol type, INamedTypeSymbol validatorInterface) =>
		type.AllInterfaces
			.Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, validatorInterface))
			.Select(i => i.TypeArguments[0]);

	/// <summary>
	/// A routable assembly carries at least one <c>[Route]</c>-attributed type; its marker is the
	/// first such type, ordinal. The assembly declaring <c>Norse.Hosting.Web.Components.Routes</c> is
	/// reported separately as <see cref="ComponentDiscoveryResult.RoutesHolderMarker"/> (the Routes
	/// type itself -- always unambiguous, unlike a per-assembly first-of-many pick) and excluded from
	/// <see cref="ComponentDiscoveryResult.RoutableAssemblyMarkers"/> entirely: the Router's
	/// <c>AppAssembly</c> already covers it, and Blazor throws on duplicate route discovery if it also
	/// shows up in <c>AdditionalAssemblies</c>. Also reports, separately again, whichever of those
	/// markers (if any) belongs to <paramref name="compilation"/>'s own assembly -- Task 5's Razor
	/// endpoint discovery excludes it (<c>MapRazorComponents&lt;App&gt;</c>'s implicit root already
	/// covers it) even though Task 4/5's Router registration does not (the Router has no equivalent
	/// implicit-root exception), so the two consumers need this split, not just the raw marker list.
	/// Route markers get the same <c>Compilation.IsSymbolAccessibleWithin</c> guard <see
	/// cref="DiscoverValidators"/> applies -- an inaccessible routed type in a referenced assembly
	/// can't back a <c>typeof(...)</c> in the emitted registration either. Also reports whether the
	/// routes-holder assembly <em>is</em>
	/// <paramref name="compilation"/>'s own assembly (<see
	/// cref="ComponentDiscoveryResult.RoutesHolderIsOwnAssembly"/>) -- the routes-holder assembly is
	/// always excluded from the per-assembly walk above (it's covered by <c>RoutesHolderMarker</c>
	/// itself, not the generic first-of-many pick), so when Routes lives in the compilation's own
	/// assembly, <c>OwnAssemblyRoutableMarker</c> comes back null with nothing left to match -- Task 5's
	/// endpoint-list composition needs the separate boolean to still exclude that in-compilation holder.
	/// </summary>
	static (ImmutableArray<string> RoutableMarkers, string? RoutesHolderMarker, bool RoutesHolderIsOwnAssembly, string? OwnAssemblyRoutableMarker) DiscoverRoutes(Compilation compilation, IAssemblySymbol[] assemblies, SymbolDisplayFormat format)
	{
		var routesType = compilation.GetTypeByMetadataName(RoutesMetadataName);
		var routesHolderAssembly = routesType?.ContainingAssembly;
		var routesHolderMarker = routesType?.ToDisplayString(format);
		var routesHolderIsOwnAssembly = routesHolderAssembly is not null && SymbolEqualityComparer.Default.Equals(routesHolderAssembly, compilation.Assembly);

		var routeAttribute = compilation.GetTypeByMetadataName(RouteAttributeMetadataName);
		if (routeAttribute is null)
			return ([], routesHolderMarker, routesHolderIsOwnAssembly, null);

		var perAssemblyMarkers =
			assemblies
				.Where(a => !SymbolEqualityComparer.Default.Equals(a, routesHolderAssembly))
				.Select(a => (Assembly: a, Marker: AllTypes(a.GlobalNamespace)
					.Where(t => t.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, routeAttribute)))
					.Where(t => compilation.IsSymbolAccessibleWithin(t, compilation.Assembly))
					.Select(t => t.ToDisplayString(format))
					.OrderBy(name => name, StringComparer.Ordinal)
					.FirstOrDefault()))
				.Where(x => x.Marker is not null)
				.ToImmutableArray();

		ImmutableArray<string> routableMarkers =
		[
			.. perAssemblyMarkers
				.Select(x => x.Marker!)
				.OrderBy(marker => marker, StringComparer.Ordinal)
		];

		var ownAssemblyRoutableMarker = perAssemblyMarkers
			.Where(x => SymbolEqualityComparer.Default.Equals(x.Assembly, compilation.Assembly))
			.Select(x => x.Marker)
			.FirstOrDefault();

		return (routableMarkers, routesHolderMarker, routesHolderIsOwnAssembly, ownAssemblyRoutableMarker);
	}

	/// <summary>Recursive walk of every named type reachable from <paramref name="root"/>, including nested namespaces and each type's own nested types -- same shape as <c>ContractDiscovery.AllTypes</c> plus nested-type recursion, kept local rather than shared so this file has no compile-time dependency on ContractDiscovery.cs being linked into the same consumer.</summary>
	static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
	{
		foreach (var type in root.GetTypeMembers())
			foreach (var nested in AllTypes(type))
				yield return nested;

		foreach (var child in root.GetNamespaceMembers())
			foreach (var type in AllTypes(child))
				yield return type;
	}

	/// <summary>Yields <paramref name="type"/> itself followed by every type nested inside it, at any depth -- a validator or routed component declared as a nested class (scoped inside a partial class, a common test-fixture-grouping pattern) is otherwise silently unreachable from the namespace-only walk above.</summary>
	static IEnumerable<INamedTypeSymbol> AllTypes(INamedTypeSymbol type)
	{
		yield return type;

		foreach (var nested in type.GetTypeMembers())
			foreach (var descendant in AllTypes(nested))
				yield return descendant;
	}
}

/// <summary>Discovered validators, routable-assembly markers, and the routes-holder assembly's own marker (plus whether that holder assembly is the discovering compilation's own), plus whether a routing composition seam (<c>RoutesAdditionalAssemblies</c>) is present for Tasks 4/5 to emit against.</summary>
sealed record ComponentDiscoveryResult(
	ImmutableArray<ValidatorModel> Validators,
	ImmutableArray<string> RoutableAssemblyMarkers,
	string? RoutesHolderMarker,
	bool RoutesHolderIsOwnAssembly,
	bool RoutesAdditionalAssembliesTypeExists,
	string? OwnAssemblyRoutableMarker);

/// <summary>A discovered FluentValidation validator -- both names global::-qualified.</summary>
sealed record ValidatorModel(string ValidatorTypeName, string RequestTypeName);
