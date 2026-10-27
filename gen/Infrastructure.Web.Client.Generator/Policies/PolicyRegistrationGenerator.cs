using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;
using Norse.Infrastructure.Web.Grpc.Generator.Shared;
using Norse.Infrastructure.Web.Server.Generator.Policies;
using Diagnostics = Norse.Infrastructure.Web.Server.Generator.Diagnostics;

namespace Norse.Infrastructure.Web.Client.Generator.Policies;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
///     The client half of <c>AddNorsePolicies()</c>: Blazor WebAssembly evaluates
///     <c>[Authorize(Policy = …)]</c> in the router, so a WASM host needs the same registration the server
///     generator emits. Discovery, diagnostics (NORSE014/NORSE015), and the emitter are the server's own
///     source, linked in — only the bootstrap differs: <c>AddAuthorizationBuilder()</c> lives in the ASP.NET
///     Core shared framework a WASM host cannot reference, so the builder is constructed over
///     <c>AddAuthorizationCore()</c> instead. Emits only when the authorization seam
///     (<c>Microsoft.AspNetCore.Authorization.AuthorizationBuilder</c>) is visible to the compilation; emits
///     nothing -- no source, no diagnostics -- when it is not, since such a compilation (this package's own
///     gRPC client library among them) cannot host a policy. Also stands down when the ASP.NET Core shared
///     framework itself is visible (<c>Microsoft.AspNetCore.Builder.WebApplication</c>): that host belongs to
///     the server generator, which emits the same <c>NorsePolicyRegistration</c> type, so a server project that
///     also references this package would otherwise collide (CS0101). Where the seam is visible and the
///     framework is not, a file is always produced, so the call always compiles.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PolicyRegistrationGenerator : IIncrementalGenerator
{
	const string AuthorizationBuilderMetadataName = "Microsoft.AspNetCore.Authorization.AuthorizationBuilder";

	const string WebApplicationMetadataName = "Microsoft.AspNetCore.Builder.WebApplication";

	const string Bootstrap =
		"new global::Microsoft.AspNetCore.Authorization.AuthorizationBuilder(services.AddAuthorizationCore())";

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var models = context.CompilationProvider
			.Combine(context.AnalyzerConfigOptionsProvider)
			.Select(Discover);
		context.RegisterSourceOutput(models, static (productionContext, result) =>
		{
			if (!result.ShouldEmit)
				return;
			foreach (var diagnostic in result.Diagnostics)
				productionContext.ReportDiagnostic(diagnostic);
			productionContext.AddSource("NorsePolicyRegistration.g.cs",
				SourceText.From(
					PolicyRegistrationEmitter.Emit(result.RootNamespace, Bootstrap, result.Declarations),
					Utf8NoBom.Encoding));
		});
	}

	static DiscoveryResult Discover(
		(Compilation Compilation, AnalyzerConfigOptionsProvider Options) input, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var (compilation, options) = input;
		if (compilation.GetTypeByMetadataName(AuthorizationBuilderMetadataName) is null
			|| compilation.GetTypeByMetadataName(WebApplicationMetadataName) is not null)
			return new DiscoveryResult(false, string.Empty, [], []);

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

		return new DiscoveryResult(true, rootNamespace, declarations, diagnostics.ToImmutable());
	}

	sealed record DiscoveryResult(
		bool ShouldEmit,
		string RootNamespace, ImmutableArray<PolicyDeclaration> Declarations, ImmutableArray<Diagnostic> Diagnostics);
}
