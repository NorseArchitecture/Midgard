using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;
using Norse.Infrastructure.Web.Grpc.Generator.Shared;

namespace Norse.Infrastructure.Web.Server.Generator.Policies;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
///     Discovers every <c>[NorsePolicy]</c> declaration reachable in the compilation's resolved reference
///     set (<see cref="PolicyDeclarationDiscovery" />) and emits <c>AddNorsePolicies()</c> — the one call
///     that replaces every hand-written policy lambda. Reports NORSE015 for every malformed declaration and
///     NORSE014 for every duplicated policy name before emitting; a malformed or duplicated declaration
///     never reaches generated code, but a file is still produced regardless of whether either diagnostic
///     fired -- Yggdrasil calls <c>AddNorsePolicies()</c> unconditionally, so the shape must always compile.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PolicyRegistrationGenerator : IIncrementalGenerator
{
	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var models = context.CompilationProvider
			.Combine(context.AnalyzerConfigOptionsProvider)
			.Select(Discover);
		context.RegisterSourceOutput(models, static (productionContext, result) =>
		{
			foreach (var diagnostic in result.Diagnostics)
				productionContext.ReportDiagnostic(diagnostic);
			productionContext.AddSource("NorsePolicyRegistration.g.cs",
				SourceText.From(
					PolicyRegistrationEmitter.Emit(result.RootNamespace, result.Declarations), Utf8NoBom.Encoding));
		});
	}

	static DiscoveryResult Discover(
		(Compilation Compilation, AnalyzerConfigOptionsProvider Options) input, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var (compilation, options) = input;
		var rootNamespace = RootNamespaceResolution.Resolve(compilation, options);
		var discovery = PolicyDeclarationDiscovery.Discover(compilation);

		var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
		foreach (var invalid in discovery.Invalid)
			diagnostics.Add(Diagnostic.Create(
				Diagnostics.InvalidPolicyDeclaration, invalid.Location, invalid.QualifiedMethod, invalid.Reason));

		var duplicates = discovery.Valid
			.GroupBy(d => d.Name, StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.OrderBy(group => group.Key, StringComparer.Ordinal)
			.ToImmutableArray();
		foreach (var duplicate in duplicates)
			diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicatePolicyName, Location.None, duplicate.Key,
				string.Join(", ",
					duplicate.Select(d => $"{d.DeclaringType}.{d.MethodName}").OrderBy(s => s, StringComparer.Ordinal))));

		// A build error must not also produce ambiguous code -- a duplicated name is dropped from what
		// gets emitted, same treatment as an Invalid entry, even though it lives in discovery.Valid.
		var duplicateNames = new HashSet<string>(duplicates.Select(group => group.Key), StringComparer.Ordinal);
		var declarations = discovery.Valid
			.Where(d => !duplicateNames.Contains(d.Name))
			.ToImmutableArray();

		return new DiscoveryResult(rootNamespace, declarations, diagnostics.ToImmutable());
	}

	sealed record DiscoveryResult(
		string RootNamespace, ImmutableArray<PolicyDeclaration> Declarations, ImmutableArray<Diagnostic> Diagnostics);
}
