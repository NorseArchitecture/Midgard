using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Norse.Infrastructure.Web.Grpc.Generator.Shared;

// Linked into both Infrastructure.Web.Server.Generator and Infrastructure.Web.Client.Generator via
// <Compile Include> -- transport-agnostic discovery of Norse gRPC contracts (System.ServiceModel's
// ServiceContractAttribute + I{Context}Service naming + >=1 method returning Task<Outcome<T>>/
// ValueTask<Outcome<T>>). Roslyn generators can't reference other analyzer-only assemblies, so this
// is plain shared source (compiled once per consuming assembly), not a shared package reference.
static class ContractDiscovery
{
	const string ServiceContractAttributeMetadataName = "System.ServiceModel.ServiceContractAttribute";
	const string OutcomeMetadataName = "Norse.Abstractions.Contracts.Outcome`1";
	const string TaskMetadataName = "System.Threading.Tasks.Task`1";
	const string ValueTaskMetadataName = "System.Threading.Tasks.ValueTask`1";

	/// <summary>
	///     Discovers every Norse gRPC contract visible to <paramref name="compilation" /> — its own
	///     assembly plus every referenced assembly (PackageReference-mode parity, matching Asgard's
	///     handler-registration generator). A Norse contract is an interface that carries
	///     <c>[ServiceContract]</c>, is named <c>I{Context}Service</c>, and declares at least one method
	///     whose return type is <c>Task&lt;Outcome&lt;T&gt;&gt;</c>/<c>ValueTask&lt;Outcome&lt;T&gt;&gt;</c>
	///     — matched by symbol via <see cref="SymbolEqualityComparer" /> on the original definition, never
	///     by unqualified name.
	/// </summary>
	public static ImmutableArray<ContractModel> Discover(Compilation compilation)
	{
		var serviceContractAttribute = compilation.GetTypeByMetadataName(ServiceContractAttributeMetadataName);
		var outcomeType = compilation.GetTypeByMetadataName(OutcomeMetadataName);
		if (serviceContractAttribute is null || outcomeType is null)
			return [];

		var taskType = compilation.GetTypeByMetadataName(TaskMetadataName);
		var valueTaskType = compilation.GetTypeByMetadataName(ValueTaskMetadataName);
		var format = SymbolDisplayFormat.FullyQualifiedFormat;

		IAssemblySymbol[] assemblies = [compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols];

		return
		[
			.. assemblies
				.SelectMany(a => AllTypes(a.GlobalNamespace))
				.Where(t => t.TypeKind == TypeKind.Interface &&
					t.Name.Length > 1 &&
					t.Name[0] == 'I' &&
					t.Name.EndsWith("Service", StringComparison.Ordinal))
				.Where(t => t.GetAttributes().Any(a =>
					SymbolEqualityComparer.Default.Equals(a.AttributeClass, serviceContractAttribute)))
				.Select(t => (Interface: t, Payloads: t.GetMembers().OfType<IMethodSymbol>()
					.Select(m => ExtractOutcomePayload(m.ReturnType, outcomeType, taskType, valueTaskType))
					.Where(p => p is not null)
					.Select(p => p!.ToDisplayString(format))
					.Distinct(StringComparer.Ordinal)
					.OrderBy(p => p, StringComparer.Ordinal)
					.ToImmutableArray()))
				.Where(x => x.Payloads.Length > 0)
				.Select(x => new ContractModel(x.Interface, x.Interface.ToDisplayString(format), x.Payloads))
		];
	}

	/// <summary>
	///     Distinct, ordinal-sorted global-qualified payload type names across every discovered contract — the surrogate
	///     registration set.
	/// </summary>
	public static ImmutableArray<string> DistinctPayloads(ImmutableArray<ContractModel> contracts) =>
	[
		.. contracts
			.SelectMany(c => c.PayloadTypeNames)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(p => p, StringComparer.Ordinal)
	];

	/// <summary>
	///     NORSE021 belt-and-braces: payload type names that share a short (unqualified) name across
	///     distinct, differently-namespaced full names. Emitters use fully-qualified names throughout, so
	///     this never mis-emits — it exists purely as a dedup safety net on the surrogate set.
	/// </summary>
	public static IEnumerable<(string ShortName, string[] FullNames)> PayloadShortNameCollisions(
		ImmutableArray<string> distinctPayloads) =>
		distinctPayloads
			.GroupBy(ShortName, StringComparer.Ordinal)
			.Where(g => g.Count() > 1)
			.Select(g => (g.Key, g.ToArray()));

	static string ShortName(string globalQualifiedName) =>
		globalQualifiedName.Substring(globalQualifiedName.LastIndexOf('.') + 1);

	static ITypeSymbol? ExtractOutcomePayload(ITypeSymbol returnType, INamedTypeSymbol outcomeType,
		INamedTypeSymbol? taskType, INamedTypeSymbol? valueTaskType)
	{
		if (returnType is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } wrapper)
			return null;

		var isTaskLike =
			SymbolEqualityComparer.Default.Equals(wrapper.OriginalDefinition, taskType) ||
			SymbolEqualityComparer.Default.Equals(wrapper.OriginalDefinition, valueTaskType);
		if (!isTaskLike)
			return null;

		if (wrapper.TypeArguments[0] is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } outcome)
			return null;

		return SymbolEqualityComparer.Default.Equals(outcome.OriginalDefinition, outcomeType) ?
			outcome.TypeArguments[0] :
			null;
	}

	/// <summary>
	///     Recursive walk of every named type reachable from <paramref name="root" />, including nested
	///     namespaces and each type's own nested types -- same shape as
	///     <c>ComponentDiscovery</c>'s deliberately separate local <c>AllTypes</c> pair. A contract
	///     interface or facade controller declared as a nested type (scoped inside a partial/static
	///     container, a realm's grouping idiom) is otherwise silently unreachable from a namespace-only
	///     walk.
	/// </summary>
	public static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol root)
	{
		foreach (var type in root.GetTypeMembers())
			foreach (var nested in AllTypes(type))
				yield return nested;

		foreach (var child in root.GetNamespaceMembers())
			foreach (var type in AllTypes(child))
				yield return type;
	}

	/// <summary>Yields <paramref name="type" /> itself followed by every type nested inside it, at any depth.</summary>
	static IEnumerable<INamedTypeSymbol> AllTypes(INamedTypeSymbol type)
	{
		yield return type;

		foreach (var nested in type.GetTypeMembers())
			foreach (var descendant in AllTypes(nested))
				yield return descendant;
	}
}

/// <summary>
///     A discovered Norse gRPC contract — the interface symbol (server-side implementation lookup only, never crosses
///     an incremental-generator caching boundary) plus its global-qualified name and distinct, ordinal-sorted payload type
///     names.
/// </summary>
sealed record ContractModel(
	INamedTypeSymbol InterfaceSymbol,
	string InterfaceName,
	ImmutableArray<string> PayloadTypeNames);
