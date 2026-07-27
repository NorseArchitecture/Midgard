using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;
using Norse.Infrastructure.Web.Grpc.Generator.Shared;

namespace Norse.Infrastructure.Web.Client.Generator;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
/// Discovers a compilation's Norse gRPC contracts at compile time and emits
/// <c>AddNorseGrpcClients()</c> — a code-first client proxy per contract over an
/// <c>OutcomeClientInterceptor</c>-decorated invoker, plus idempotent <c>Outcome&lt;T&gt;</c>
/// surrogate wiring against <c>RuntimeTypeModel.Default</c>. Emits nothing when no contract is
/// visible to the compilation.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GrpcClientRegistrationGenerator : IIncrementalGenerator
{
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
			if (result.ContractTypeNames.Length > 0 && !result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
				productionContext.AddSource("NorseGrpcClientRegistration.g.cs",
					SourceText.From(ClientRegistrationEmitter.Emit(result.RootNamespace, result.ContractTypeNames, result.Payloads), Utf8NoBom.Encoding));
		});
	}

	static DiscoveryResult Discover(Compilation compilation, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var contracts = ContractDiscovery.Discover(compilation);
		var rootNamespace = compilation.AssemblyName ?? "Norse.Generated";
		if (contracts.Length == 0)
			return new DiscoveryResult(rootNamespace, [], [], []);

		var payloads = ContractDiscovery.DistinctPayloads(contracts);
		var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
		foreach (var (shortName, fullNames) in ContractDiscovery.PayloadShortNameCollisions(payloads))
			diagnostics.Add(Diagnostic.Create(_payloadShortNameCollision, Location.None, shortName, string.Join(", ", fullNames)));

		var contractTypeNames = contracts
			.Select(c => c.InterfaceName)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToImmutableArray();

		return new DiscoveryResult(rootNamespace, contractTypeNames, payloads, diagnostics.ToImmutable());
	}

	sealed record DiscoveryResult(string RootNamespace, ImmutableArray<string> ContractTypeNames, ImmutableArray<string> Payloads, ImmutableArray<Diagnostic> Diagnostics);
}
