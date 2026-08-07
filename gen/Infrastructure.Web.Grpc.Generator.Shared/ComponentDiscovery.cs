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
		var (routableMarkers, routesHolderMarker) = DiscoverRoutes(compilation, assemblies, format);
		var routesAdditionalAssembliesTypeExists = compilation.GetTypeByMetadataName(RoutesAdditionalAssembliesMetadataName) is not null;

		return new ComponentDiscoveryResult(validators, routableMarkers, routesHolderMarker, routesAdditionalAssembliesTypeExists);
	}

	/// <summary>A non-abstract named type implementing <c>FluentValidation.IValidator&lt;T&gt;</c>, matched by symbol on the interface's original definition. Empty (not an error) when FluentValidation isn't referenced.</summary>
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
				.Select(t => (Validator: t, Request: ValidatedRequestType(t, validatorInterface)))
				.Where(x => x.Request is not null)
				.Select(x => new ValidatorModel(x.Validator.ToDisplayString(format), x.Request!.ToDisplayString(format)))
				.OrderBy(v => v.ValidatorTypeName, StringComparer.Ordinal)
		];
	}

	static ITypeSymbol? ValidatedRequestType(INamedTypeSymbol type, INamedTypeSymbol validatorInterface) =>
		type.AllInterfaces
			.FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, validatorInterface))
			?.TypeArguments[0];

	/// <summary>
	/// A routable assembly carries at least one <c>[Route]</c>-attributed type; its marker is the
	/// first such type, ordinal. The assembly declaring <c>Norse.Hosting.Web.Components.Routes</c> is
	/// reported separately as <see cref="ComponentDiscoveryResult.RoutesHolderMarker"/> (the Routes
	/// type itself -- always unambiguous, unlike a per-assembly first-of-many pick) and excluded from
	/// <see cref="ComponentDiscoveryResult.RoutableAssemblyMarkers"/> entirely: the Router's
	/// <c>AppAssembly</c> already covers it, and Blazor throws on duplicate route discovery if it also
	/// shows up in <c>AdditionalAssemblies</c>.
	/// </summary>
	static (ImmutableArray<string> RoutableMarkers, string? RoutesHolderMarker) DiscoverRoutes(Compilation compilation, IAssemblySymbol[] assemblies, SymbolDisplayFormat format)
	{
		var routesType = compilation.GetTypeByMetadataName(RoutesMetadataName);
		var routesHolderAssembly = routesType?.ContainingAssembly;
		var routesHolderMarker = routesType?.ToDisplayString(format);

		var routeAttribute = compilation.GetTypeByMetadataName(RouteAttributeMetadataName);
		if (routeAttribute is null)
			return ([], routesHolderMarker);

		ImmutableArray<string> routableMarkers =
		[
			.. assemblies
				.Where(a => !SymbolEqualityComparer.Default.Equals(a, routesHolderAssembly))
				.Select(a => AllTypes(a.GlobalNamespace)
					.Where(t => t.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, routeAttribute)))
					.Select(t => t.ToDisplayString(format))
					.OrderBy(name => name, StringComparer.Ordinal)
					.FirstOrDefault())
				.Where(marker => marker is not null)
				.Select(marker => marker!)
				.OrderBy(marker => marker, StringComparer.Ordinal)
		];

		return (routableMarkers, routesHolderMarker);
	}

	/// <summary>Recursive walk of every named type reachable from <paramref name="root"/>, including nested namespaces -- same shape as <c>ContractDiscovery.AllTypes</c>, kept local rather than shared so this file has no compile-time dependency on ContractDiscovery.cs being linked into the same consumer.</summary>
	static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
	{
		foreach (var type in root.GetTypeMembers())
			yield return type;

		foreach (var child in root.GetNamespaceMembers())
			foreach (var type in AllTypes(child))
				yield return type;
	}
}

/// <summary>Discovered validators, routable-assembly markers, and the routes-holder assembly's own marker, plus whether a routing composition seam (<c>RoutesAdditionalAssemblies</c>) is present for Tasks 4/5 to emit against.</summary>
sealed record ComponentDiscoveryResult(
	ImmutableArray<ValidatorModel> Validators,
	ImmutableArray<string> RoutableAssemblyMarkers,
	string? RoutesHolderMarker,
	bool RoutesAdditionalAssembliesTypeExists);

/// <summary>A discovered FluentValidation validator -- both names global::-qualified.</summary>
sealed record ValidatorModel(string ValidatorTypeName, string RequestTypeName);
