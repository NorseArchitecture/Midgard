using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;
using Norse.Infrastructure.Web.Grpc.Generator.Shared;

namespace Norse.Infrastructure.Web.Server.Generator;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
///     Discovers a compilation's Norse gRPC contracts and their implementations at compile time and
///     emits <c>MapNorseGrpcServices()</c> — code-first <c>MapGrpcService</c>/<c>EnableGrpcWeb</c>
///     registration for every discovered service, plus idempotent <c>Outcome&lt;T&gt;</c> surrogate
///     wiring against <c>RuntimeTypeModel.Default</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GrpcServerRegistrationGenerator : IIncrementalGenerator
{
	static readonly DiagnosticDescriptor _missingImplementation = new(
		"NORSE020", "Norse gRPC contract has no implementation",
		"Contract '{0}' is visible to this compilation but no non-abstract implementing class was found", "Norse.Grpc",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor _payloadShortNameCollision = new(
		"NORSE021", "Outcome<T> payload short-name collision",
		"Payload type short name '{0}' is used by multiple distinct types across namespaces: {1}", "Norse.Grpc",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var models = context.CompilationProvider.Select(Discover);
		context.RegisterSourceOutput(models, static (productionContext, result) =>
		{
			foreach (var diagnostic in result.Diagnostics)
				productionContext.ReportDiagnostic(diagnostic);
			if (result.Services.Length > 0 && !result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
				productionContext.AddSource("NorseGrpcServerRegistration.g.cs",
					SourceText.From(
						ServerRegistrationEmitter.Emit(result.RootNamespace, result.Services, result.Payloads),
						Utf8NoBom.Encoding));
		});
	}

	static DiscoveryResult Discover(Compilation compilation, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var contracts = ContractDiscovery.Discover(compilation);
		var rootNamespace = compilation.AssemblyName ?? "Norse.Generated";
		if (contracts.Length == 0)
			return new DiscoveryResult(rootNamespace, [], [], []);

		IAssemblySymbol[] assemblies = [compilation.Assembly, .. compilation.SourceModule.ReferencedAssemblySymbols];
		var implementations = assemblies
			.SelectMany(a => ContractDiscovery.AllTypes(a.GlobalNamespace))
			.Where(t => t is { IsAbstract: false, TypeKind: TypeKind.Class })
			.ToImmutableArray();

		var format = SymbolDisplayFormat.FullyQualifiedFormat;
		var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
		var services = ImmutableArray.CreateBuilder<ServiceModel>();

		foreach (var contract in contracts)
		{
			var implementation = implementations.FirstOrDefault(t =>
				t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, contract.InterfaceSymbol)));
			if (implementation is null)
			{
				diagnostics.Add(Diagnostic.Create(_missingImplementation, Location.None, contract.InterfaceName));
				continue;
			}

			services.Add(new ServiceModel(contract.InterfaceName, implementation.ToDisplayString(format)));
		}

		var payloads = ContractDiscovery.DistinctPayloads(contracts);
		foreach (var (shortName, fullNames) in ContractDiscovery.PayloadShortNameCollisions(payloads))
			diagnostics.Add(Diagnostic.Create(_payloadShortNameCollision, Location.None, shortName,
				string.Join(", ", fullNames)));

		var sortedServices = services
			.OrderBy(s => s.InterfaceName, StringComparer.Ordinal)
			.ToImmutableArray();

		return new DiscoveryResult(rootNamespace, sortedServices, payloads, diagnostics.ToImmutable());
	}

	sealed record DiscoveryResult(
		string RootNamespace,
		ImmutableArray<ServiceModel> Services,
		ImmutableArray<string> Payloads,
		ImmutableArray<Diagnostic> Diagnostics);
}
