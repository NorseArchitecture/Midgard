using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Infrastructure.Web.Grpc.Generator.Shared.Tests;

/// <summary>
///     Direct coverage of <see cref="ContractDiscovery.AllTypes(INamespaceSymbol)" /> -- the recursive metadata walk both
///     wiring generators and Infrastructure.Web.Server.Generator's XML reference-closure discovery
///     (<c>XmlShapeGenerator.DiscoverReferenced</c>) depend on for every type visible in a referenced
///     assembly. The 2026-08-09 codex review hardening pass (finding 5) extended it to also recurse
///     <c>INamedTypeSymbol.GetTypeMembers()</c> -- this class proves that half directly, mirroring
///     <c>ComponentDiscoveryTests</c>' own nested-type coverage for its deliberately separate local
///     <c>AllTypes</c>.
/// </summary>
public sealed class ContractDiscoveryTests
{
	static readonly MetadataReference[] _extraReferences =
	[
		.. ReferenceAssemblies.Net110
	];

	[Fact]
	void AllTypes_enumerates_a_type_nested_inside_another_type()
	{
		const string Source = """
			namespace Own;

			public sealed record TopLevel;

			public static class Container
			{
				public sealed class Nested;
			}
			""";
		var compilation = CSharpCompilation.Create(
			"Norse.Fixtures.NestedWalk",
			[CSharpSyntaxTree.ParseText(Source, cancellationToken: TestContext.Current.CancellationToken)],
			_extraReferences,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var names = ContractDiscovery.AllTypes(compilation.Assembly.GlobalNamespace)
			.Select(static t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
			.ToList();

		names.ShouldContain("global::Own.Container.Nested");
	}
}
